using AnimeJaNaiConfEditor.ViewModels;
using AnimeJaNaiConfEditor.Views;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ReactiveUI.Avalonia;
using System.Text.Json;
bool resume=args.Length>4&&args[4]=="resume";
string output=Path.GetFullPath(args[0]);Directory.CreateDirectory(output);if(File.Exists(Path.Combine(output,"summary.json")))throw new Exception("Conclusive UI receipt exists; no replay.");
string data=Path.Combine(output,"data"),inf=Path.Combine(data,"inference");foreach(var dir in new[]{data,inf,Path.Combine(data,"onnx"),Path.Combine(data,"rife")})Directory.CreateDirectory(dir);
Environment.SetEnvironmentVariable("ANIMEJANAI_DATA_DIR",data);Environment.SetEnvironmentVariable("ANIMEJANAI_INFERENCE_DIR",inf);Environment.SetEnvironmentVariable("ANIMEJANAI_ROOT",output);Environment.SetEnvironmentVariable("ANIMEJANAI_VFX_SDK",Path.Combine(output,"absent-system-sdk"));
File.Copy(Path.GetFullPath(args[1]),Path.Combine(inf,"libaji_pipeline.so"),true);
File.WriteAllText(Path.Combine(data,"animejanai.conf"),"[global]\nconfig_version=3\nbackend=TensorRT\ndefault_slot=1\n[slot_1]\nprofile_name=Imported HDR profile\nchain_1_pipeline_version=1\nchain_1_stage_count=1\nchain_1_stage_1_id=existing-hdr\nchain_1_stage_1_kind=hdr\nchain_1_stage_1_method=nvidia-truehdr\nchain_1_stage_1_runtime=cuda\nchain_1_stage_1_enabled=no\nchain_1_stage_1_sdk_root=/custom/preserved-sdk\n");
AppBuilder.Configure<AnimeJaNaiConfEditor.App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions{UseHeadlessDrawing=false}).WithInterFont().UseReactiveUI(_=>{}).SetupWithoutStarting();
var checks=new List<string>();void Check(string name,bool ok){if(!ok)throw new Exception(name);checks.Add(name);Console.WriteLine("PASS "+name);}
var vm=new MainWindowViewModel();vm.HandleShowCustomProfile("1");vm.AnimeJaNaiConf.SetTensorRtSelected();var optional=new OptionalEngines{DataContext=vm};var window=new Window{Content=optional,Width=1050,Height=1050};window.Show();Dispatcher.UIThread.RunJobs();
var button=optional.GetVisualDescendants().OfType<Button>().Single(b=>b.Name=="ImportTrueHdrSdk");if(!resume)Check("optional engine view exposes one usable Core and feature archive import",button.IsEnabled&&!vm.OptionalSdks.TrueHdrReady);
bool checkProgress=!resume;
void Import(string path){var t=vm.OptionalSdks.ImportTrueHdrAsync(path);if(checkProgress){Check("only TrueHDR section shows import progress and both import controls are locked",vm.OptionalSdks.IsTrueHdrBusy&&!vm.OptionalSdks.IsFrucBusy&&!vm.OptionalSdks.CanImport);checkProgress=false;}var deadline=DateTime.UtcNow.AddSeconds(15);while(!t.IsCompleted){Dispatcher.UIThread.RunJobs();Thread.Sleep(1);if(DateTime.UtcNow>deadline)throw new Exception("UI fixture import timeout");}t.GetAwaiter().GetResult();Dispatcher.UIThread.RunJobs();}
if(!resume)Import(Path.GetFullPath(args[2]));Check("Core-only import reports missing feature without unlocking HDR",vm.OptionalSdks.InstalledTrueHdr is {Core:true,Feature:false,Root:null}&&!vm.OptionalSdks.TrueHdrReady&&vm.OptionalSdks.TrueHdrImportStatus.Contains("feature"));
Import(Path.GetFullPath(args[3]));Environment.SetEnvironmentVariable("ANIMEJANAI_VFX_SDK",null);vm.OptionalSdks.Refresh();Dispatcher.UIThread.RunJobs();Check("complete managed SDK becomes the automatic root with reader capability",vm.OptionalSdks.TrueHdrReady&&vm.OptionalSdks.TrueHdrRoot.Contains("optional-sdks/truehdr/versions/"));
var hdr=vm.CurrentSlot.Chains[0].Stages.Single();Check("import does not rewrite an existing custom profile SDK",hdr.HdrSdkRoot=="/custom/preserved-sdk"&&hdr.CanUseInstalledHdrSdk);hdr.UseInstalledHdrSdk();Check("one action adopts the installed SDK without a manual path field",hdr.HdrSdkRoot==vm.OptionalSdks.TrueHdrRoot&&!hdr.CanUseInstalledHdrSdk);
Check("busy state clears after both archive operations",!vm.OptionalSdks.IsBusy&&vm.OptionalSdks.CanImport);
using(var frame=window.CaptureRenderedFrame()){if(frame==null)throw new Exception("No rendered frame");frame.Save(Path.Combine(output,"optional-truehdr.png"));}
File.WriteAllText(Path.Combine(output,"summary.json"),JsonSerializer.Serialize(new{passed=true,checks,prior_two_checks_reused=resume,managed_root=vm.OptionalSdks.TrueHdrRoot,scope="New archive importer UI and profile adoption with synthetic ELF archives; official Core and installed feature-file archive qualified independently; no video or GPU measurement"},new JsonSerializerOptions{WriteIndented=true}));window.Close();
