using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;

namespace AnimeJaNaiConfEditor.Services;
public sealed record InstalledFruc(string Library,string Runtime,string ArchiveSha256);
// Import only known runtime files into an owned immutable directory. No archive
// paths become output paths; nothing is run, downloaded or written into the SDK.
public sealed class FrucSdkStore
{
    readonly string root;
    const long FileLimit=256L*1024*1024,ArchiveLimit=2L*1024*1024*1024;
    static readonly Regex RuntimeName=new(@"^libcudart\.so\.11(\.[0-9]+)*$",RegexOptions.CultureInvariant);
    sealed record FileRecord(string Name,long Bytes,string Sha256,string Source);
    sealed record Manifest(int Version,string ArchiveSha256,string InstallId,FileRecord[] Files);
    public FrucSdkStore(string dataDirectory)=>root=Path.Combine(Path.GetFullPath(dataDirectory),"optional-sdks","fruc");
    public InstalledFruc? Read()
    {
        NoLink(Path.GetDirectoryName(root)!);NoLink(root);
        string current=Path.Combine(root,"current.json");if(!File.Exists(current))return null;
        if(new FileInfo(current).Length>65536)throw new InvalidDataException("FRUC installation record is too large.");
        var m=JsonSerializer.Deserialize<Manifest>(File.ReadAllText(current))??throw new InvalidDataException("Invalid FRUC installation record.");
        if(m.Version!=1)throw new InvalidDataException("Newer FRUC installation format; update the Manager.");
        if(m.ArchiveSha256==null||!Regex.IsMatch(m.ArchiveSha256,@"^[0-9a-f]{64}$")||m.Files==null||m.Files.Length>100||m.Files.Any(f=>f==null||string.IsNullOrEmpty(f.Name)||string.IsNullOrEmpty(f.Sha256)))throw new InvalidDataException("Invalid FRUC installation identity.");
        var libs=m.Files.Where(f=>f.Name=="libNvOFFRUC.so").ToArray();var runtimes=m.Files.Where(f=>RuntimeName.IsMatch(f.Name)).ToArray();
        if(libs.Length!=1||runtimes.Length!=1)throw new InvalidDataException("FRUC installation needs its SDK library and CUDART11.");
        if(m.InstallId==null||!Regex.IsMatch(m.InstallId,@"^[0-9a-f]{64}(-[0-9a-f]{32})?$")||!m.InstallId.StartsWith(m.ArchiveSha256,StringComparison.Ordinal))throw new InvalidDataException("Invalid managed SDK version path.");
        string folder=Path.Combine(root,"versions",m.InstallId);NoLink(Path.GetDirectoryName(folder)!);NoLink(folder);
        foreach(var f in m.Files) {
            if(f.Name!=Path.GetFileName(f.Name)||f.Name.Contains('/')||f.Name.Contains('\\')||f.Bytes<0||f.Bytes>FileLimit)throw new InvalidDataException("Invalid managed SDK file.");
            string path=Path.Combine(folder,f.Name);NoLink(path);if(!File.Exists(path)||new FileInfo(path).Length!=f.Bytes)throw new InvalidDataException("Managed SDK file missing or changed. Import the NVIDIA ZIP again.");
        }
        ValidateElf(Path.Combine(folder,libs[0].Name));ValidateElf(Path.Combine(folder,runtimes[0].Name));
        return new(Path.Combine(folder,libs[0].Name),Path.Combine(folder,runtimes[0].Name),m.ArchiveSha256);
    }
    public InstalledFruc Import(string archive,CancellationToken cancel=default)
    {
        if(!OperatingSystem.IsLinux()||System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture!=System.Runtime.InteropServices.Architecture.X64)throw new InvalidDataException("The delivered FRUC reader requires Linux x64.");
        // Do not overwrite a newer registry version with this Manager.
        NoLink(Path.GetDirectoryName(root)!);NoLink(root);
        string current=Path.Combine(root,"current.json");
        if(File.Exists(current)) {
            if(new FileInfo(current).Length>65536)throw new InvalidDataException("FRUC installation record is too large.");
            using var record=JsonDocument.Parse(File.ReadAllText(current));
            if(!record.RootElement.TryGetProperty("Version",out var version)||version.ValueKind!=JsonValueKind.Number||!version.TryGetInt32(out int v)||v!=1)throw new InvalidDataException("Newer FRUC installation format; update the Manager.");
        }
        var info=new FileInfo(archive);if(!info.Exists||info.Length>ArchiveLimit)throw new InvalidDataException("Choose the NVIDIA Optical Flow SDK ZIP (maximum2GiB).");
        long sourceBytes=info.Length;var sourceTime=info.LastWriteTimeUtc;
        using var input=new FileStream(archive,FileMode.Open,FileAccess.Read,FileShare.Read);
        string identity=Hash(input,cancel);input.Position=0;using var zip=new ZipArchive(input,ZipArchiveMode.Read,true);
        const long expandedLimit=4L*1024*1024*1024;
        if(zip.Entries.Count>20000)throw new InvalidDataException("SDK ZIP exceeds the import bounds.");
        long expanded=0;foreach(var e in zip.Entries){if(e.Length<0||e.Length>expandedLimit-expanded)throw new InvalidDataException("SDK ZIP exceeds the import bounds.");expanded+=e.Length;}
        string Safe(ZipArchiveEntry e) {
            string name=e.FullName.Replace('\\','/');
            if(name.StartsWith('/')||name.Contains(':')||name.Split('/').Any(p=>p=="..")||((e.ExternalAttributes>>16)&0xf000)==0xa000)throw new InvalidDataException("SDK ZIP contains an unsafe path or symbolic link.");
            return name;
        }
        var entries=zip.Entries.Select(e=>(Entry:e,Name:Safe(e))).Where(e=>!e.Name.StartsWith("__MACOSX/",StringComparison.Ordinal)).ToArray();
        var libs=entries.Where(e=>e.Name.EndsWith("/NvOFFRUCSample/bin/ubuntu/libNvOFFRUC.so",StringComparison.Ordinal)).ToArray();
        var runtimes=entries.Where(e=>e.Name.Contains("/NvOFFRUCSample/bin/ubuntu/",StringComparison.Ordinal)&&RuntimeName.IsMatch(Path.GetFileName(e.Name))).ToArray();
        if(libs.Length!=1||runtimes.Length!=1)throw new InvalidDataException("ZIP missing the Linux x64 FRUC sample library or its CUDART11. Download the complete Optical Flow SDK from NVIDIA.");
        var licenses=entries.Where(e=>Regex.IsMatch(Path.GetFileName(e.Name),@"^(license|eula)",RegexOptions.IgnoreCase)).ToArray();
        if(licenses.Length==0||licenses.Length>64||licenses.Sum(e=>e.Entry.Length)>32L*1024*1024)throw new InvalidDataException("SDK ZIP must include NVIDIA's license agreement.");
        Directory.CreateDirectory(root);string versions=Path.Combine(root,"versions");NoLink(versions);Directory.CreateDirectory(versions);
        string staging=Path.Combine(root,".import-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(staging);
        try {
            var files=new List<FileRecord>();
            void Extract(ZipArchiveEntry e,string name) {
                if(e.Length>FileLimit)throw new InvalidDataException("SDK runtime/license file exceeds256MiB.");
                string path=Path.Combine(staging,name);using var src=e.Open();using var dst=new FileStream(path,FileMode.CreateNew,FileAccess.Write,FileShare.None);
                byte[] buffer=new byte[1024*1024];long size=0;uint crc=0xffffffff;using var sha=IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                int n;while((n=src.Read(buffer))>0){cancel.ThrowIfCancellationRequested();size+=n;if(size>FileLimit||size>e.Length)throw new InvalidDataException("SDK expanded size mismatch.");dst.Write(buffer,0,n);sha.AppendData(buffer,0,n);for(int j=0;j<n;j++)crc=(crc>>8)^CrcTable[(crc^buffer[j])&255];}
                if(size!=e.Length||~crc!=e.Crc32)throw new InvalidDataException("SDK ZIP checksum mismatch; download it again.");
                files.Add(new(name,size,Convert.ToHexString(sha.GetHashAndReset()).ToLowerInvariant(),e.FullName));
            }
            Extract(libs[0].Entry,"libNvOFFRUC.so");Extract(runtimes[0].Entry,Path.GetFileName(runtimes[0].Name));
            int index=0;foreach(var e in licenses)Extract(e.Entry,"license-"+(index++)+"-"+Regex.Replace(Path.GetFileName(e.Name),@"[^A-Za-z0-9_.-]","_")[..Math.Min(100,Path.GetFileName(e.Name).Length)]);
            ValidateElf(Path.Combine(staging,"libNvOFFRUC.so"));ValidateElf(Path.Combine(staging,Path.GetFileName(runtimes[0].Name)));
            info.Refresh();if(info.Length!=sourceBytes||info.LastWriteTimeUtc!=sourceTime)throw new IOException("SDK ZIP changed during import.");cancel.ThrowIfCancellationRequested();
            string installId=identity,final=Path.Combine(versions,identity);NoLink(final);bool reuse=Directory.Exists(final);
            if(reuse)foreach(var f in files) {
                string oldPath=Path.Combine(final,f.Name);
                if(!File.Exists(oldPath)){reuse=false;break;}
                using var old=File.OpenRead(oldPath);if(Hash(old,cancel)!=f.Sha256){reuse=false;break;}
            }
            if(Directory.Exists(final)&&!reuse){installId=identity+"-"+Guid.NewGuid().ToString("N");final=Path.Combine(versions,installId);}
            var manifest=new Manifest(1,identity,installId,files.ToArray());string json=JsonSerializer.Serialize(manifest,new JsonSerializerOptions{WriteIndented=true});File.WriteAllText(Path.Combine(staging,"install.json"),json);
            if(!reuse)Directory.Move(staging,final);
            string temp=Path.Combine(root,".current-"+Guid.NewGuid().ToString("N"));
            try {File.WriteAllText(temp,json);File.Move(temp,Path.Combine(root,"current.json"),true);}finally{if(File.Exists(temp))File.Delete(temp);}
            return Read()??throw new IOException("SDK installation did not commit.");
        }finally {if(Directory.Exists(staging))Directory.Delete(staging,true);}
    }
    static void NoLink(string path) {
        if((File.Exists(path)||Directory.Exists(path))&&(File.GetAttributes(path)&FileAttributes.ReparsePoint)!=0)throw new InvalidDataException("Managed SDK directories and files cannot be symbolic links.");
    }
    static void ValidateElf(string path) {
        using var f=File.OpenRead(path);Span<byte> header=stackalloc byte[20];f.ReadExactly(header);
        if(header[0]!=127||header[1]!='E'||header[2]!='L'||header[3]!='F'||header[4]!=2||header[5]!=1||header[16]!=3||header[17]!=0||header[18]!=62||header[19]!=0)throw new InvalidDataException("SDK library is not Linux x64 ELF. Use the complete NVIDIA Linux SDK.");
    }
    static string Hash(Stream input,CancellationToken cancel) {
        using var hash=IncrementalHash.CreateHash(HashAlgorithmName.SHA256);byte[] buffer=new byte[1024*1024];int n;
        while((n=input.Read(buffer))>0){cancel.ThrowIfCancellationRequested();hash.AppendData(buffer,0,n);}return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }
    static readonly uint[] CrcTable=MakeCrcTable();
    static uint[] MakeCrcTable(){var table=new uint[256];for(uint i=0;i<256;i++){uint n=i;for(int j=0;j<8;j++)n=(n&1)!=0?0xedb88320^(n>>1):n>>1;table[i]=n;}return table;}
}
