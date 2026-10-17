using AnimeJaNaiConfEditor.Services;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
static class Guards
{
    public static void Run(string output,bool resume=false)
    {
        if(File.Exists(Path.Combine(output,"guards.json")))throw new Exception("Conclusive guards receipt exists; no replay.");
        var checks=new List<string>();void Check(string name,bool ok){if(!ok)throw new Exception(name);checks.Add(name);Console.WriteLine("PASS "+name);}
        byte[] Elf(){byte[] b=new byte[32];b[0]=127;b[1]=(byte)'E';b[2]=(byte)'L';b[3]=(byte)'F';b[4]=2;b[5]=1;b[16]=3;b[18]=62;for(int i=20;i<32;i++)b[i]=(byte)i;return b;}
        var core=new Dictionary<string,byte[]>();foreach(string n in new[]{"lib/libNVCVImage.so","lib/libVideoFX.so","lib/libVideoFXLocal.so","lib/libnvngxruntime.so","external/cuda/lib/libcudart.so.12","external/cuda/lib/libnppc.so.12","external/cuda/lib/libnppial.so.12","external/cuda/lib/libnppicc.so.12","external/cuda/lib/libnppidei.so.12","external/cuda/lib/libnppig.so.12","external/tensorrt/lib/libnvinfer.so.10"})core["VideoFX/"+n]=Elf();core["VideoFX/share/LICENSE.txt"]=Encoding.UTF8.GetBytes("Qualification fixture only, no vendor runtime.");
        var feature=new Dictionary<string,byte[]>{{"feature/lib/libnvVFXTrueHDR.so",Elf()},{"feature/lib/libnvidia-ngx-truehdr.so.1.2.3",Elf()},{"LICENSE.txt",Encoding.UTF8.GetBytes("Qualification fixture only")}};
        string Zip(string name,Dictionary<string,byte[]> entries,Dictionary<string,string>? links=null){string p=Path.Combine(output,name+".zip");if(File.Exists(p))return p;using var z=ZipFile.Open(p,ZipArchiveMode.Create);foreach(var kv in entries){var e=z.CreateEntry(kv.Key,CompressionLevel.NoCompression);using var s=e.Open();s.Write(kv.Value);}if(links!=null)foreach(var kv in links){var e=z.CreateEntry(kv.Key);e.ExternalAttributes=0xa000<<16;using var s=e.Open();s.Write(Encoding.UTF8.GetBytes(kv.Value));}return p;}
        string data=Path.Combine(output,"guards-data");var store=new TrueHdrSdkStore(data);string coreZip=Zip("core-fixture",core),featureZip=Zip("feature-fixture",feature);
        InstalledTrueHdr ready=store.Read();
        if(!resume) {
        var first=store.Import(featureZip);Check("feature-first import preserves licenses and stays incomplete",first.Feature&&!first.Core&&first.Root==null);
        ready=store.Import(coreZip);Check("separate Core completes the feature-first SDK with automatic runtime root",ready.Root!=null&&ready.Core&&ready.Feature&&File.Exists(Path.Combine(ready.Root,"features/nvvfxtruehdr/share",Directory.GetFiles(Path.Combine(ready.Root,"features/nvvfxtruehdr/share")).Select(Path.GetFileName).First()!)));
        }
        string record=Path.Combine(data,"optional-sdks/truehdr/current.json"),original=File.ReadAllText(record);
        void Reject(string name,string zip){bool rejected=false;try{store.Import(zip);}catch(InvalidDataException){rejected=true;}Check(name,rejected&&File.ReadAllText(record)==original);}
        if(!resume) {
        Reject("unsafe archive path cannot alter the selected SDK",Zip("unsafe",new(feature){{"../escape.txt",[1]}}));
        var wrong=new Dictionary<string,byte[]>(feature);wrong["feature/lib/libnvVFXTrueHDR.so"]=Elf();wrong["feature/lib/libnvVFXTrueHDR.so"][18]=183;Reject("ARM ELF cannot enable Linux x64 TrueHDR",Zip("arm",wrong));
        var missing=new Dictionary<string,byte[]>(feature);missing.Remove("feature/lib/libnvidia-ngx-truehdr.so.1.2.3");Reject("feature without NGX runtime is rejected atomically",Zip("missing",missing));
        var onlyLicense=new Dictionary<string,byte[]>{{"LICENSE.txt",[1]}};Reject("license alone is not a Core SDK",Zip("license-only",onlyLicense));
        }
        Reject("selected absolute alias cannot escape the archive",Zip("external-alias-selected",feature,new(){{"feature/lib/libnvVFXTrueHDR.so.99","/tmp/escape"}}));
        Reject("selected alias cycle is rejected",Zip("cycle-selected",feature,new(){{"feature/lib/libnvVFXTrueHDR.so.98","libnvVFXTrueHDR.so.99"},{"feature/lib/libnvVFXTrueHDR.so.99","libnvVFXTrueHDR.so.98"}}));
        string crc=Zip("checksum",feature);var bytes=File.ReadAllBytes(crc);byte[] marker=Elf();int at=-1;for(int i=0;i<=bytes.Length-marker.Length;i++)if(bytes.AsSpan(i,marker.Length).SequenceEqual(marker)){at=i;break;}if(at<0)throw new Exception("Fixture data not found");bytes[at+31]^=1;File.WriteAllBytes(crc,bytes);Reject("ZIP checksum corruption is rejected before selection changes",crc);
        using(var cancel=new CancellationTokenSource()){cancel.Cancel();bool stopped=false;try{store.Import(coreZip,cancel.Token);}catch(OperationCanceledException){stopped=true;}Check("cancellation preserves selected complete SDK",stopped&&File.ReadAllText(record)==original);}
        string oldRoot=ready.Root!;File.AppendAllText(Path.Combine(oldRoot,"features/nvvfxtruehdr/lib/libnvVFXTrueHDR.so"),"changed");bool invalid=false;try{store.Read();}catch(InvalidDataException){invalid=true;}Check("modified managed runtime is detected",invalid);
        var repaired=store.Import(featureZip);Check("repair publishes a fresh version without overwriting the previous component",repaired.Root!=oldRoot&&new FileInfo(Path.Combine(oldRoot,"features/nvvfxtruehdr/lib/libnvVFXTrueHDR.so")).Length==39&&new FileInfo(Path.Combine(repaired.Root!,"features/nvvfxtruehdr/lib/libnvVFXTrueHDR.so")).Length==32);
        string current=File.ReadAllText(record);File.WriteAllText(record,"{\"Version\":999}");bool future=false;try{store.Import(coreZip);}catch(InvalidDataException){future=true;}Check("newer installation format is never overwritten",future&&File.ReadAllText(record)=="{\"Version\":999}");File.WriteAllText(record,current);
        Check("failed and cancelled imports clean temporary staging",!Directory.EnumerateDirectories(Path.Combine(data,"optional-sdks/truehdr"),".import-*").Any());
        File.WriteAllText(Path.Combine(output,"guards.json"),JsonSerializer.Serialize(new{passed=true,checks,prior_six_checks_reused=resume,coreZip,featureZip,scope="New importer guards with explicitly synthetic ELF fixtures; no GPU/SDK pixel execution or original feature archive claim"},new JsonSerializerOptions{WriteIndented=true}));
    }
}
