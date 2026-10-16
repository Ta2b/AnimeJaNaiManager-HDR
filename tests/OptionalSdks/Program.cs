using AnimeJaNaiConfEditor.ViewModels;
using AnimeJaNaiConfEditor.Views;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ReactiveUI.Avalonia;
using System.Text.Json;
using AnimeJaNaiConfEditor.Services;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Threading;
using System.Diagnostics;

string output=Path.GetFullPath(args[0]);Directory.CreateDirectory(output);
if(File.Exists(Path.Combine(output,"summary.json")))throw new Exception("Receipt already exists; do not repeat a conclusive check.");
string data=Path.Combine(output,"fixture"), inf=Path.Combine(data,"inference");
foreach(var dir in new[]{data,inf,Path.Combine(data,"onnx"),Path.Combine(data,"rife")})Directory.CreateDirectory(dir);
Environment.SetEnvironmentVariable("ANIMEJANAI_DATA_DIR",data);Environment.SetEnvironmentVariable("ANIMEJANAI_INFERENCE_DIR",inf);Environment.SetEnvironmentVariable("ANIMEJANAI_ROOT",output);
foreach(var name in new[]{"libnvinfer.so.11","libaji_pipeline.so","aji_pipeline.fatbin","libaji_trt.so","libaji_fruc.so","aji_fruc.fatbin"})File.WriteAllText(Path.Combine(inf,name),"UI fixture only");
File.WriteAllText(Path.Combine(data,"rife/rife_v4.26.onnx"),"UI fixture only");File.WriteAllText(Path.Combine(data,"onnx/span.onnx"),"UI fixture only");
File.WriteAllText(Path.Combine(data,"animejanai.conf"),"# user comment\n[global]\nconfig_version=3\nbackend=TensorRT\nrife_engine=quality\ndefault_slot=1\n[slot_1]\nprofile_name=UX\nchain_1_rife=yes\nchain_1_rife_model=426\nchain_1_rife_factor_numerator=2\nchain_1_rife_factor_denominator=1\nchain_1_rife_before_upscale=yes\nchain_1_model_1_name=span\nchain_1_model_1_resize_height_before_upscale=720\n");
AppBuilder.Configure<AnimeJaNaiConfEditor.App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions{UseHeadlessDrawing=false}).WithInterFont().UseReactiveUI(_=>{}).SetupWithoutStarting();
var checks=new List<string>();void Check(string name,bool ok){if(!ok)throw new Exception(name);checks.Add(name);Console.WriteLine("PASS "+name);}
string archive=Path.GetFullPath(args[1]);string Hash(string path){using var stream=File.OpenRead(path);return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();}
string sourceHash=Hash(archive);var store=new FrucSdkStore(data);var vm=new MainWindowViewModel();vm.HandleShowCustomProfile("1");vm.AnimeJaNaiConf.SetTensorRtSelected();var chain=vm.CurrentSlot.Chains[0];chain.EnableOrderedPipeline();
var view=new InterpolationStages{DataContext=chain};var window=new Window{Content=new ScrollViewer{Content=view},Width=1050,Height=1200};window.Show();Dispatcher.UIThread.RunJobs();
var add=view.GetVisualDescendants().OfType<Button>().Single(c=>Equals(c.Content,"Add interpolation"));Check("FRUC add button is grey while the SDK is absent",store.Read()==null&&!chain.Stages[0].TechnologyChoices.Single(c=>c.Name=="NVIDIA FRUC").Available);
Check("first-launch optional setup is offered once",vm.OptionalSdks.NeedsOnboarding);vm.OptionalSdks.AcknowledgeOnboarding();Check("setup stays available without repeating onboarding",!vm.OptionalSdks.NeedsOnboarding);
var sw=Stopwatch.StartNew();var task=vm.OptionalSdks.ImportAsync(archive);while(!task.IsCompleted){Dispatcher.UIThread.RunJobs();Thread.Sleep(1);if(sw.Elapsed.TotalSeconds>30)throw new Exception("Import timed out");}task.GetAwaiter().GetResult();Dispatcher.UIThread.RunJobs();
var installed=store.Read()??throw new Exception(vm.OptionalSdks.Status);double importMs=sw.Elapsed.TotalMilliseconds;
Check("real Optical Flow ZIP installs its library and CUDART11 together",Path.GetFileName(installed.Library)=="libNvOFFRUC.so"&&Path.GetFileName(installed.Runtime)=="libcudart.so.11.6.55"&&installed.ArchiveSha256==sourceHash);
byte[] libBytes,runtimeBytes;using(var zip=ZipFile.OpenRead(archive)) {
 byte[] Bytes(ZipArchiveEntry e){using var src=e.Open();using var dst=new MemoryStream();src.CopyTo(dst);return dst.ToArray();}
 libBytes=Bytes(zip.Entries.Single(e=>e.FullName.EndsWith("/ubuntu/libNvOFFRUC.so")));runtimeBytes=Bytes(zip.Entries.Single(e=>e.FullName.EndsWith("/ubuntu/libcudart.so.11.6.55")));
}
Check("installed binaries are byte-exact and license is retained",File.ReadAllBytes(installed.Library).SequenceEqual(libBytes)&&File.ReadAllBytes(installed.Runtime).SequenceEqual(runtimeBytes)&&Directory.EnumerateFiles(Path.GetDirectoryName(installed.Library)!,"license-*").Any());
Check("SDK installation unlocks the real FRUC add control",chain.Stages[0].TechnologyChoices.Single(c=>c.Name=="NVIDIA FRUC").Available&&vm.OptionalSdks.FrucReady);
add.Command!.Execute(add.CommandParameter);Dispatcher.UIThread.RunJobs();var fruc=chain.Stages.Last();fruc.MethodChoice="NVIDIA FRUC";Dispatcher.UIThread.RunJobs();Check("new stage auto-resolves SDK paths and needs no manual fields",fruc.SdkLibrary==installed.Library&&fruc.SdkRuntime==installed.Runtime&&fruc.CanEnable&&!view.GetVisualDescendants().OfType<TextBox>().Any(t=>t.Text==installed.Library));
fruc.Enabled=true;vm.WriteAnimeJaNaiConf();var round=vm.ReadAnimeJaNaiConf(Path.Combine(data,"animejanai.conf")).UpscaleSlots[0].Chains[0].Stages.Last();Check("saved profiles contain the automatically selected runtime paths",round.SdkLibrary==installed.Library&&round.SdkRuntime==installed.Runtime);
string current=Path.Combine(data,"optional-sdks/fruc/current.json"),originalRegistry=File.ReadAllText(current);string MakeZip(string name,Dictionary<string,byte[]> entries){string path=Path.Combine(output,name);using var zip=ZipFile.Open(path,ZipArchiveMode.Create);foreach(var e in entries){using var target=zip.CreateEntry(e.Key).Open();target.Write(e.Value);}return path;}
var runtimeEntries=new Dictionary<string,byte[]>{{"SDK/NvOFFRUCSample/bin/ubuntu/libNvOFFRUC.so",libBytes},{"SDK/NvOFFRUCSample/bin/ubuntu/libcudart.so.11.6.55",runtimeBytes},{"SDK/LicenseAgreement.txt",System.Text.Encoding.UTF8.GetBytes("Qualification fixture only")}};
void Reject(string name,string zip){bool rejected=false;try{store.Import(zip);}catch(InvalidDataException){rejected=true;}Check(name,rejected&&File.ReadAllText(current)==originalRegistry);}
var unsafeEntries=new Dictionary<string,byte[]>(runtimeEntries){{"../escape.txt",[1]}};Reject("unsafe archive is rejected without changing the selected SDK",MakeZip("unsafe.zip",unsafeEntries));
var wrongEntries=new Dictionary<string,byte[]>(runtimeEntries);var wrongLib=libBytes.ToArray();wrongLib[18]=183;wrongEntries["SDK/NvOFFRUCSample/bin/ubuntu/libNvOFFRUC.so"]=wrongLib;Reject("wrong library architecture is rejected atomically",MakeZip("wrong-architecture.zip",wrongEntries));
using(var cancel=new CancellationTokenSource()){cancel.Cancel();bool stopped=false;try{store.Import(archive,cancel.Token);}catch(OperationCanceledException){stopped=true;}Check("cancel keeps the previous SDK selected",stopped&&File.ReadAllText(current)==originalRegistry);}
fruc.SdkLibrary="/custom/user-sdk.so";fruc.SdkRuntime="/custom/user-cudart.so";vm.OptionalSdks.Refresh();Check("custom profile paths survive a managed SDK refresh",fruc.SdkLibrary=="/custom/user-sdk.so"&&fruc.CanUseInstalledSdk);fruc.UseInstalledSdk();Check("one action switches a custom profile to the installed SDK",fruc.SdkLibrary==installed.Library&&fruc.SdkRuntime==installed.Runtime);
// A repair must preserve the old modified folder and redirect managed profiles.
using(var file=File.Open(installed.Library,FileMode.Append))file.WriteByte(0x5a);long changedLength=new FileInfo(installed.Library).Length;var repaired=store.Import(archive);vm.OptionalSdks.Refresh();
Check("repair preserves old files and redirects managed profiles",repaired.Library!=installed.Library&&new FileInfo(installed.Library).Length==changedLength&&File.ReadAllBytes(repaired.Library).SequenceEqual(libBytes)&&fruc.SdkLibrary==repaired.Library);
MainWindowViewModel.Platform=MainWindowViewModel.Os.Windows;vm.OptionalSdks.Refresh();Check("Windows FRUC stays grey until its reader exists",!vm.OptionalSdks.FrucReady&&!chain.CanAddFruc);MainWindowViewModel.Platform=MainWindowViewModel.Os.Linux;vm.OptionalSdks.Refresh();
Check("source archive stays unchanged and staging directories are cleaned",Hash(archive)==sourceHash&&!Directory.EnumerateDirectories(Path.Combine(data,"optional-sdks/fruc"),".import-*").Any());
var sdkView=new OptionalEngines{DataContext=vm};window.Content=new ScrollViewer{Content=sdkView};Dispatcher.UIThread.RunJobs();using(var frame=window.CaptureRenderedFrame()){if(frame==null)throw new Exception("No frame");frame.Save(Path.Combine(output,"optional-engines.png"));}window.Close();
File.WriteAllText(Path.Combine(output,"summary.json"),JsonSerializer.Serialize(new{passed=true,checks,archive_sha256=sourceHash,installed_library=repaired.Library,installed_runtime=repaired.Runtime,import_ms=importMs,scope="Real NVIDIA SDK ZIP import and actual Avalonia controls; GPU/provider setup checked separately"},new JsonSerializerOptions{WriteIndented=true}));
