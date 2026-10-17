using System;
using System.Collections.Generic;
using System.Formats.Tar;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;

namespace AnimeJaNaiConfEditor.Services;
public sealed record InstalledTrueHdr(string? Root,bool Core,bool Feature,string? CoreSha256,string? FeatureSha256);

// Read archives as data. Only known runtime/library/license paths are installed,
// with internal aliases checked against the selected files before creating links.
// Components are immutable versions; the selected complete SDK changes atomically.
public sealed class TrueHdrSdkStore
{
    readonly string root;
    const long GiB=1024L*1024*1024,FileLimit=2*GiB,ArchiveLimit=8*GiB,ExpandedLimit=12*GiB;
    sealed record FileRecord(string Path,long Bytes,string Sha256,string? Target);
    sealed record Component(string Id,string ArchiveSha256,FileRecord[] Files);
    sealed record Registry(int Version,Component? Core,Component? Feature,string? RuntimeId);
    sealed record Alias(string Source,string Destination,string Target);
    static readonly Regex Identity=new("^[0-9a-f]{64}(-[0-9a-f]{32})?$",RegexOptions.CultureInvariant);
    static readonly Regex Library=new(@"^lib[A-Za-z0-9_-]+\.so(?:\.[0-9]+)*$",RegexOptions.CultureInvariant);
    static readonly string[] CoreRequired=["lib/libNVCVImage.so","lib/libVideoFX.so","lib/libVideoFXLocal.so","lib/libnvngxruntime.so","external/cuda/lib/libcudart.so.12","external/cuda/lib/libnppc.so.12","external/cuda/lib/libnppial.so.12","external/cuda/lib/libnppicc.so.12","external/cuda/lib/libnppidei.so.12","external/cuda/lib/libnppig.so.12","external/tensorrt/lib/libnvinfer.so.10"];
    public TrueHdrSdkStore(string dataDirectory)=>root=Path.Combine(Path.GetFullPath(dataDirectory),"optional-sdks","truehdr");
    string ComponentRoot(Component c)=>Path.Combine(root,"components",c.Id);
    Registry ReadRegistry()
    {
        NoLink(Path.GetDirectoryName(root)!);NoLink(root);
        string path=Path.Combine(root,"current.json");NoLink(path);
        if(!File.Exists(path))return new(1,null,null,null);
        if(new FileInfo(path).Length>1024*1024)throw new InvalidDataException("TrueHDR installation record is too large.");
        var r=JsonSerializer.Deserialize<Registry>(File.ReadAllText(path))??throw new InvalidDataException("Invalid TrueHDR installation record.");
        if(r.Version!=1)throw new InvalidDataException("Newer TrueHDR installation format; update the Manager.");
        return r;
    }
    public InstalledTrueHdr Read()
    {
        var r=ReadRegistry();if(r.Core!=null)Validate(r.Core,true);if(r.Feature!=null)Validate(r.Feature,false);
        string? sdk=null;
        if(r.Core!=null&&r.Feature!=null) {
            string id=RuntimeId(r.Core,r.Feature);if(r.RuntimeId!=id)throw new InvalidDataException("Invalid TrueHDR runtime identity.");
            sdk=Path.Combine(root,"versions",id);NoLink(Path.GetDirectoryName(sdk)!);NoLink(sdk);
            CheckDirectoryLink(Path.Combine(sdk,"lib"),Path.Combine(ComponentRoot(r.Core),"lib"));
            CheckDirectoryLink(Path.Combine(sdk,"external"),Path.Combine(ComponentRoot(r.Core),"external"));
            NoLink(Path.Combine(sdk,"features"));
            CheckDirectoryLink(Path.Combine(sdk,"features","nvvfxtruehdr"),ComponentRoot(r.Feature));
        } else if(r.RuntimeId!=null)throw new InvalidDataException("Incomplete TrueHDR SDK cannot select a runtime.");
        return new(sdk,r.Core!=null,r.Feature!=null,r.Core?.ArchiveSha256,r.Feature?.ArchiveSha256);
    }
    public InstalledTrueHdr Import(string archive,CancellationToken cancel=default)
    {
        if(!OperatingSystem.IsLinux()||System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture!=System.Runtime.InteropServices.Architecture.X64)throw new InvalidDataException("The delivered TrueHDR stage requires Linux x64. Windows uses its own RTX Video runtime.");
        ReadRegistry();var info=new FileInfo(archive);
        if(!info.Exists||info.Length>ArchiveLimit)throw new InvalidDataException("Choose a NVIDIA Maxine Core or TrueHDR ZIP, .tgz or .tar.gz (maximum8GiB).");
        long originalLength=info.Length;var originalTime=info.LastWriteTimeUtc;
        using var input=new FileStream(archive,FileMode.Open,FileAccess.Read,FileShare.Read);
        string hash=Hash(input,cancel);input.Position=0;
        Directory.CreateDirectory(root);string components=Path.Combine(root,"components");NoLink(components);Directory.CreateDirectory(components);
        string staging=Path.Combine(root,".import-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(staging);
        var records=new Dictionary<string,List<FileRecord>>{{"core",[]},{"feature",[]}};
        var mapped=new Dictionary<string,string>(StringComparer.Ordinal);var destinations=new HashSet<string>(StringComparer.Ordinal);var aliases=new List<Alias>();
        long expanded=0;int entries=0;
        void Entry(string source,long length,Stream? data,string? link,bool hardLink,uint? crc=null)
        {
            cancel.ThrowIfCancellationRequested();source=Safe(source);
            if(length<0||length>ExpandedLimit-expanded)throw new InvalidDataException("SDK archive exceeds the import bounds.");expanded+=length;
            string? destination=Select(source);if(destination==null)return;
            if(link!=null)link=ResolveLink(source,link,hardLink);
            if(!destinations.Add(destination)||!mapped.TryAdd(source,destination))throw new InvalidDataException("SDK archive contains ambiguous duplicate libraries: "+source+" -> "+destination);
            if(link!=null){aliases.Add(new(source,destination,link));return;}
            if(data==null||length>FileLimit)throw new InvalidDataException("SDK runtime file is missing or exceeds2GiB.");
            string target=Path.Combine(staging,destination);Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            using var output=new FileStream(target,FileMode.CreateNew,FileAccess.Write,FileShare.None);using var sha=IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            byte[] buffer=new byte[1024*1024];long written=0;uint checksum=0xffffffff;int n;
            while((n=data.Read(buffer))>0){cancel.ThrowIfCancellationRequested();written+=n;if(written>length)throw new InvalidDataException("SDK expanded size mismatch.");output.Write(buffer,0,n);sha.AppendData(buffer,0,n);if(crc.HasValue)for(int i=0;i<n;i++)checksum=(checksum>>8)^CrcTable[(checksum^buffer[i])&255];}
            if(written!=length||crc.HasValue&&~checksum!=crc.Value)throw new InvalidDataException("SDK archive checksum or size mismatch.");
            string kind=destination.Split('/')[0],relative=destination[(kind.Length+1)..];records[kind].Add(new(relative,written,Convert.ToHexString(sha.GetHashAndReset()).ToLowerInvariant(),null));
        }
        try {
            if(archive.EndsWith(".zip",StringComparison.OrdinalIgnoreCase)) {
                using var zip=new ZipArchive(input,ZipArchiveMode.Read,true);
                if(zip.Entries.Count>20000)throw new InvalidDataException("SDK archive exceeds the entry limit.");
                foreach(var e in zip.Entries) {
                    string name=Safe(e.FullName);int type=(e.ExternalAttributes>>16)&0xf000;
                    if(name.EndsWith('/'))continue;
                    using var data=e.Open();
                    if(type==0xa000) {if(e.Length>4096)throw new InvalidDataException("SDK alias is too long.");using var reader=new StreamReader(data,Encoding.UTF8);Entry(name,0,null,reader.ReadToEnd(),false);}
                    else {if(type!=0&&type!=0x8000)throw new InvalidDataException("SDK ZIP contains a special file.");Entry(name,e.Length,data,null,false,e.Crc32);}
                }
            } else if(archive.EndsWith(".tgz",StringComparison.OrdinalIgnoreCase)||archive.EndsWith(".tar.gz",StringComparison.OrdinalIgnoreCase)||archive.EndsWith(".tar",StringComparison.OrdinalIgnoreCase)) {
                using var borrowed=new BorrowedStream(input,cancel);
                using Stream tar=archive.EndsWith(".tar",StringComparison.OrdinalIgnoreCase)?new BorrowedStream(input,cancel):new GZipStream(borrowed,CompressionMode.Decompress,true);
                using var reader=new TarReader(tar,leaveOpen:true);TarEntry? e;
                while((e=reader.GetNextEntry())!=null) {
                    if(++entries>20000)throw new InvalidDataException("SDK archive exceeds the entry limit.");Safe(e.Name);switch(e.EntryType) {
                        case TarEntryType.Directory:break;
                        case TarEntryType.RegularFile:case TarEntryType.V7RegularFile:Entry(e.Name,e.Length,e.DataStream,null,false);break;
                        case TarEntryType.SymbolicLink:case TarEntryType.HardLink:Entry(e.Name,0,null,e.LinkName,e.EntryType==TarEntryType.HardLink);break;
                        default:throw new InvalidDataException("SDK tar contains an unsupported special file.");
                    }
                }
                // Finish gzip input to validate its trailer even when tar ended earlier.
                byte[] tail=new byte[65536];while(tar.Read(tail)>0)cancel.ThrowIfCancellationRequested();
            } else throw new InvalidDataException("Choose a ZIP, .tgz, .tar.gz or .tar SDK archive.");
            foreach(var alias in aliases) {
                // Some SDK versions ship unused broken aliases. Omit those; all
                // required sonames below still have to resolve to real ELF files.
                string targetSource=alias.Target;var seen=new HashSet<string>{alias.Source};
                while(aliases.Find(a=>a.Source==targetSource) is {} next){if(!seen.Add(next.Source))throw new InvalidDataException("SDK alias cycle.");targetSource=next.Target;}
                if(!mapped.TryGetValue(targetSource,out string? target)||target.Split('/')[0]!=alias.Destination.Split('/')[0])continue;
                string absolute=Path.Combine(staging,target);if(!File.Exists(absolute))continue;
                string path=Path.Combine(staging,alias.Destination);Directory.CreateDirectory(Path.GetDirectoryName(path)!);string relative=Path.GetRelativePath(Path.GetDirectoryName(path)!,absolute);File.CreateSymbolicLink(path,relative);
                string kind=alias.Destination.Split('/')[0];records[kind].Add(new(alias.Destination[(kind.Length+1)..],0,"",target[(kind.Length+1)..]));
            }
            // Feature archives may put their license at the archive root.
            if(records["feature"].Any(f=>f.Path.StartsWith("lib/",StringComparison.Ordinal))&&records["core"].All(f=>f.Path.StartsWith("share/",StringComparison.Ordinal))) {
                foreach(var f in records["core"]) {string from=Path.Combine(staging,"core",f.Path),to=Path.Combine(staging,"feature",f.Path);Directory.CreateDirectory(Path.GetDirectoryName(to)!);File.Move(from,to);records["feature"].Add(f);}records["core"].Clear();
            }
            if(records.All(kv=>kv.Value.Count==0))throw new InvalidDataException("Archive does not contain the Linux Maxine Core or TrueHDR feature libraries.");
            // Validate every imported library's architecture before publishing anything.
            foreach(var kind in records.Keys)foreach(var f in records[kind].Where(f=>f.Target==null&&Library.IsMatch(Path.GetFileName(f.Path))))ValidateElf(Path.Combine(staging,kind,f.Path));
            Component? Publish(string kind) {
                if(records[kind].Count==0)return null;
                var files=records[kind].OrderBy(f=>f.Path,StringComparer.Ordinal).ToArray();string candidate=Path.Combine(staging,kind);ValidateRequired(candidate,files,kind=="core");
                string id=hash+"-"+Guid.NewGuid().ToString("N"),final=Path.Combine(components,id);Directory.Move(candidate,final);
                return new(id,hash,files);
            }
            info.Refresh();if(info.Length!=originalLength||info.LastWriteTimeUtc!=originalTime)throw new IOException("SDK archive changed during import.");cancel.ThrowIfCancellationRequested();
            // Re-read at commit time so separately imported components are retained.
            var old=ReadRegistry();var core=Publish("core")??old.Core;var feature=Publish("feature")??old.Feature;
            if(core!=null)Validate(core,true);if(feature!=null)Validate(feature,false);
            string? runtime=null;
            if(core!=null&&feature!=null) {
                runtime=RuntimeId(core,feature);string versions=Path.Combine(root,"versions");NoLink(versions);Directory.CreateDirectory(versions);
                string folder=Path.Combine(versions,runtime);NoLink(folder);
                if(!Directory.Exists(folder)) {
                    string compose=Path.Combine(staging,"runtime");Directory.CreateDirectory(Path.Combine(compose,"features"));
                    Directory.CreateSymbolicLink(Path.Combine(compose,"lib"),Path.Combine(ComponentRoot(core),"lib"));
                    Directory.CreateSymbolicLink(Path.Combine(compose,"external"),Path.Combine(ComponentRoot(core),"external"));
                    Directory.CreateSymbolicLink(Path.Combine(compose,"share"),Path.Combine(ComponentRoot(core),"share"));
                    Directory.CreateSymbolicLink(Path.Combine(compose,"features","nvvfxtruehdr"),ComponentRoot(feature));Directory.Move(compose,folder);
                }
            }
            cancel.ThrowIfCancellationRequested();string temp=Path.Combine(root,".current-"+Guid.NewGuid().ToString("N"));
            try{File.WriteAllText(temp,JsonSerializer.Serialize(new Registry(1,core,feature,runtime),new JsonSerializerOptions{WriteIndented=true}));File.Move(temp,Path.Combine(root,"current.json"),true);}finally{if(File.Exists(temp))File.Delete(temp);}
            return Read();
        }finally {if(Directory.Exists(staging))Directory.Delete(staging,true);}
    }
    void Validate(Component c,bool core)
    {
        if(c.Id==null||!Identity.IsMatch(c.Id)||c.ArchiveSha256==null||!Regex.IsMatch(c.ArchiveSha256,"^[0-9a-f]{64}$")||!c.Id.StartsWith(c.ArchiveSha256,StringComparison.Ordinal)||c.Files==null||c.Files.Length>2000||c.Files.Any(f=>f==null))throw new InvalidDataException("Invalid TrueHDR component identity.");
        string folder=ComponentRoot(c);NoLink(Path.GetDirectoryName(folder)!);NoLink(folder);var seen=new HashSet<string>();
        foreach(var f in c.Files) {
            if(f==null||f.Path==null||Safe(f.Path)!=f.Path||f.Path.EndsWith('/')||!seen.Add(f.Path)||f.Bytes<0||f.Bytes>FileLimit)throw new InvalidDataException("Invalid TrueHDR runtime file record.");
            string path=Path.Combine(folder,f.Path);for(string? parent=Path.GetDirectoryName(path);parent!=null&&parent!=folder;parent=Path.GetDirectoryName(parent))NoLink(parent);
            if(f.Target!=null) {
                if(Safe(f.Target)!=f.Target||!c.Files.Any(t=>t.Path==f.Target&&t.Target==null))throw new InvalidDataException("Invalid TrueHDR alias target.");
                if(new FileInfo(path).LinkTarget!=Path.GetRelativePath(Path.GetDirectoryName(path)!,Path.Combine(folder,f.Target)))throw new InvalidDataException("Managed SDK alias changed. Import the SDK again.");
            } else {NoLink(path);if(!File.Exists(path)||new FileInfo(path).Length!=f.Bytes||!Regex.IsMatch(f.Sha256??"","^[0-9a-f]{64}$"))throw new InvalidDataException("Managed TrueHDR file missing or changed. Import the SDK again.");}
        }
        ValidateRequired(folder,c.Files,core);
    }
    static void ValidateRequired(string folder,IEnumerable<FileRecord> files,bool core)
    {
        var records=files.ToArray();string[] required=core?CoreRequired:["lib/libnvVFXTrueHDR.so"];
        foreach(string p in required){if(!records.Any(f=>f.Path==p)||!File.Exists(Path.Combine(folder,p)))throw new InvalidDataException(core?"Core archive is incomplete: Linux x64 Core, CUDA12/NPP and TensorRT10 runtime files are required.":"Choose the Linux TrueHDR feature archive, not the Core archive.");ValidateElf(Path.Combine(folder,p));}
        if(core&&!records.Any(f=>f.Path.StartsWith("share/",StringComparison.Ordinal)&&f.Target==null))throw new InvalidDataException("Core archive must include NVIDIA license agreements.");
        if(!core&&!records.Any(f=>Path.GetFileName(f.Path).StartsWith("libnvidia-ngx-truehdr.so",StringComparison.Ordinal)&&f.Target==null))throw new InvalidDataException("TrueHDR feature is missing its NVIDIA NGX runtime.");
    }
    static string? Select(string source)
    {
        string name=Path.GetFileName(source);if(name.Length==0)return null;
        bool license=Regex.IsMatch(name,"license|agreement|terms",RegexOptions.IgnoreCase|RegexOptions.CultureInvariant);
        if(Library.IsMatch(name)) {
            if(name.StartsWith("libnvVFXTrueHDR.so",StringComparison.Ordinal)||name.StartsWith("libnvidia-ngx-truehdr.so",StringComparison.Ordinal))return "feature/lib/"+name;
            if((source=="lib/"+name||source.EndsWith("/VideoFX/lib/"+name,StringComparison.Ordinal)||source=="VideoFX/lib/"+name)&&(name.StartsWith("libNVCVImage.so",StringComparison.Ordinal)||name.StartsWith("libVideoFX.so",StringComparison.Ordinal)||name.StartsWith("libVideoFXLocal.so",StringComparison.Ordinal)||name.StartsWith("libnvngxruntime.so",StringComparison.Ordinal)))return "core/lib/"+name;
            foreach(string kind in new[]{"cuda","tensorrt"})if(source=="external/"+kind+"/lib/"+name||source.EndsWith("/external/"+kind+"/lib/"+name,StringComparison.Ordinal))return "core/external/"+kind+"/lib/"+name;
        }
        if(license&&!source.Contains("/docs/",StringComparison.Ordinal)) {
            // Preserve separately sourced feature licenses as well as Core terms.
            string kind=source.Contains("truehdr",StringComparison.OrdinalIgnoreCase)?"feature":"core";
            string id=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(source))).ToLowerInvariant()[..12];return kind+"/share/"+id+"-"+Regex.Replace(name,"[^A-Za-z0-9_.-]","_");
        }
        return null;
    }
    static string Safe(string name)
    {
        name=name.Replace('\\','/');while(name.StartsWith("./",StringComparison.Ordinal))name=name[2..];
        if(name.Length==0||name.Length>4096||name.StartsWith('/')||name.Contains(':')||name.Contains('\0')||name.Split('/').Any(p=>p==".."))throw new InvalidDataException("SDK archive contains an unsafe path.");return name;
    }
    static string ResolveLink(string source,string target,bool hard)
    {
        target=target.Replace('\\','/');if(target.StartsWith('/')||target.Contains(':')||target.Contains('\0'))throw new InvalidDataException("SDK runtime alias escapes its archive: "+source+" -> "+target);
        var parts=new List<string>();string joined=hard?target:(source.Contains('/')?source[..(source.LastIndexOf('/')+1)]:"")+target;
        foreach(string p in joined.Split('/')){if(p==""||p==".")continue;if(p==".."){if(parts.Count==0)throw new InvalidDataException("SDK alias escapes its archive.");parts.RemoveAt(parts.Count-1);}else parts.Add(p);}
        return Safe(string.Join('/',parts));
    }
    static string RuntimeId(Component core,Component feature)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(core.Id+":"+feature.Id))).ToLowerInvariant();
    static void NoLink(string path){if(new FileInfo(path).LinkTarget!=null||new DirectoryInfo(path).LinkTarget!=null)throw new InvalidDataException("Managed SDK parent directories must not be symbolic links.");}
    static void CheckDirectoryLink(string path,string target){if(new DirectoryInfo(path).LinkTarget!=target||!Directory.Exists(path))throw new InvalidDataException("Managed TrueHDR runtime directory changed. Import the SDK again.");}
    static void ValidateElf(string path){using var file=File.OpenRead(path);Span<byte> b=stackalloc byte[20];file.ReadExactly(b);if(b[0]!=127||b[1]!='E'||b[2]!='L'||b[3]!='F'||b[4]!=2||b[5]!=1||b[16]!=3||b[17]!=0||b[18]!=62||b[19]!=0)throw new InvalidDataException("SDK library is not Linux x64 ELF. Windows/ARM SDK archives cannot enable this reader.");}
    static string Hash(Stream input,CancellationToken cancel){using var hash=IncrementalHash.CreateHash(HashAlgorithmName.SHA256);byte[] buffer=new byte[1024*1024];int n;while((n=input.Read(buffer))>0){cancel.ThrowIfCancellationRequested();hash.AppendData(buffer,0,n);}return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();}
    static readonly uint[] CrcTable=MakeCrcTable();
    static uint[] MakeCrcTable(){var table=new uint[256];for(uint i=0;i<256;i++){uint n=i;for(int j=0;j<8;j++)n=(n&1)!=0?0xedb88320^(n>>1):n>>1;table[i]=n;}return table;}
    sealed class BorrowedStream(Stream inner,CancellationToken cancel):Stream
    {
        public override bool CanRead=>inner.CanRead;public override bool CanSeek=>false;public override bool CanWrite=>false;public override long Length=>inner.Length;public override long Position{get=>inner.Position;set=>throw new NotSupportedException();}
        public override int Read(byte[] buffer,int offset,int count){cancel.ThrowIfCancellationRequested();return inner.Read(buffer,offset,count);}public override int Read(Span<byte> buffer){cancel.ThrowIfCancellationRequested();return inner.Read(buffer);}public override void Flush(){}public override long Seek(long o,SeekOrigin s)=>throw new NotSupportedException();public override void SetLength(long v)=>throw new NotSupportedException();public override void Write(byte[] b,int o,int c)=>throw new NotSupportedException();
    }
}
