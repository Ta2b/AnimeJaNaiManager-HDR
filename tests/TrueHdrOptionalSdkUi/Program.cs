using AnimeJaNaiConfEditor.ViewModels;
using AnimeJaNaiConfEditor.Views;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ReactiveUI.Avalonia;
using System.Text.Json;
// args: <output> <libaji_pipeline.so> <core archive> <feature archive>. Synthetic ELF archives
// (TrueHdrSdkStore guards); no video, GPU or SDK execution.
string output=Path.GetFullPath(args[0]);Directory.CreateDirectory(output);if(File.Exists(Path.Combine(output,"summary.json")))throw new Exception("Conclusive UI receipt exists; no replay.");
string data=Path.Combine(output,"data"),inf=Path.Combine(data,"inference"),opts=Path.Combine(output,"portable_config","script-opts");foreach(var dir in new[]{data,inf,opts,Path.Combine(data,"onnx"),Path.Combine(data,"rife")})Directory.CreateDirectory(dir);
Environment.SetEnvironmentVariable("ANIMEJANAI_DATA_DIR",data);Environment.SetEnvironmentVariable("ANIMEJANAI_INFERENCE_DIR",inf);Environment.SetEnvironmentVariable("ANIMEJANAI_ROOT",output);Environment.SetEnvironmentVariable("ANIMEJANAI_VFX_SDK",Path.Combine(output,"absent-system-sdk"));
File.Copy(Path.GetFullPath(args[1]),Path.Combine(inf,"libaji_pipeline.so"),true);
File.WriteAllText(Path.Combine(opts,"animejanai_hdr_auto.conf"),"hdr_conversion=yes\nhdr_backend=mpv\nlinux_hdr_output=sdr\n");
File.WriteAllText(Path.Combine(data,"animejanai.conf"),"[global]\nconfig_version=3\nbackend=TensorRT\ndefault_slot=1\n[slot_1]\nprofile_name=Imported HDR profile\nchain_1_pipeline_version=1\nchain_1_stage_count=1\nchain_1_stage_1_id=existing-hdr\nchain_1_stage_1_kind=hdr\nchain_1_stage_1_method=nvidia-truehdr\nchain_1_stage_1_runtime=cuda\nchain_1_stage_1_enabled=no\nchain_1_stage_1_sdk_root=/custom/preserved-sdk\n");
AppBuilder.Configure<AnimeJaNaiConfEditor.App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions{UseHeadlessDrawing=false}).WithInterFont().UseReactiveUI(_=>{}).SetupWithoutStarting();
var checks=new List<string>();void Check(string name,bool ok){if(!ok)throw new Exception(name);checks.Add(name);Console.WriteLine("PASS "+name);}
var vm=new MainWindowViewModel();vm.HandleShowCustomProfile("1");vm.AnimeJaNaiConf.SetTensorRtSelected();
Check("an inactive profile HDR step is removed and the user is told HDR is global",vm.CurrentSlot.Chains[0].Stages.Count==0&&vm.ProfileMigrationNotice.Contains("global setting"));
var optional=new OptionalEngines{DataContext=vm};var window=new Window{Content=new ScrollViewer{Content=optional},Width=1050,Height=1050};window.Show();Dispatcher.UIThread.RunJobs();
var button=optional.GetVisualDescendants().OfType<Button>().Single(b=>b.Name=="ImportTrueHdrSdk");
Check("without the SDK TrueHDR is unavailable and the first cause says to import it",button.IsEnabled&&!vm.OptionalSdks.TrueHdrReady&&vm.OptionalSdks.TrueHdrStatus.StartsWith("Import the NVIDIA Maxine SDK")&&!vm.TrueHdrSelectable&&vm.TrueHdrSdkMissing);
bool checkProgress=true;
void Import(string path){var t=vm.OptionalSdks.ImportTrueHdrAsync(path);if(checkProgress){Check("TrueHDR section shows import progress and import controls are locked",vm.OptionalSdks.IsTrueHdrBusy&&!vm.OptionalSdks.CanImport);checkProgress=false;}var deadline=DateTime.UtcNow.AddSeconds(15);while(!t.IsCompleted){Dispatcher.UIThread.RunJobs();Thread.Sleep(1);if(DateTime.UtcNow>deadline)throw new Exception("UI fixture import timeout");}t.GetAwaiter().GetResult();Dispatcher.UIThread.RunJobs();}
Import(Path.GetFullPath(args[2]));Check("Core-only import reports the missing feature without unlocking TrueHDR",vm.OptionalSdks.InstalledTrueHdr is {Core:true,Feature:false,Root:null}&&!vm.OptionalSdks.TrueHdrReady&&vm.OptionalSdks.TrueHdrImportStatus.Contains("feature"));
Import(Path.GetFullPath(args[3]));Environment.SetEnvironmentVariable("ANIMEJANAI_VFX_SDK",null);vm.OptionalSdks.Refresh();Dispatcher.UIThread.RunJobs();
Check("the complete managed SDK becomes the automatic root; the next cause is named",vm.OptionalSdks.TrueHdrRoot.Contains("optional-sdks/truehdr/versions/")&&!vm.TrueHdrSdkMissing&&vm.OptionalSdks.TrueHdrStatus.Contains("libaji_trt_rtx.so"));
File.WriteAllText(Path.Combine(inf,"libaji_trt_rtx.so"),"fixture");vm.OptionalSdks.Refresh();Dispatcher.UIThread.RunJobs();
Check("the SDK bridge module is named with the reason it is not in the release",vm.OptionalSdks.TrueHdrStatus.Contains("libaji_rtx.so")&&vm.OptionalSdks.TrueHdrStatus.Contains("no release includes it"));
File.WriteAllText(Path.Combine(inf,"libaji_rtx.so"),"fixture");vm.OptionalSdks.Refresh();Dispatcher.UIThread.RunJobs();
Check("with SDR output the remaining cause is the PQ output",!vm.OptionalSdks.TrueHdrReady&&vm.OptionalSdks.TrueHdrStatus.Contains("HDR PQ output"));
vm.HdrSettings.LinuxPq=true;vm.OptionalSdks.Refresh();vm.RefreshTrueHdrChoice();Dispatcher.UIThread.RunJobs();
Check("everything present: TrueHDR is ready and offered in Settings",vm.OptionalSdks.TrueHdrReady&&vm.TrueHdrSelectable&&vm.OptionalSdks.TrueHdrStatus.StartsWith("NVIDIA TrueHDR is ready"));
var main=new MainWindow{DataContext=vm,Width=1200,Height=1000};main.Show();vm.HandleShowGlobalSettings();Dispatcher.UIThread.RunJobs();
var rtx=main.GetVisualDescendants().OfType<ToggleButton>().Single(b=>b.Name=="HdrModeRtx");
Check("the NVIDIA TrueHDR choice is enabled",rtx.IsEffectivelyEnabled);
rtx.IsChecked=true;Dispatcher.UIThread.RunJobs();vm.HdrSettings.Save();
Check("choosing it makes NVIDIA TrueHDR the global conversion",vm.HdrSettings.ModeRtx&&File.ReadAllText(Path.Combine(opts,"animejanai_hdr_auto.conf")).Contains("hdr_backend=rtx")&&File.ReadAllText(Path.Combine(opts,"animejanai_hdr_auto.conf")).Contains("hdr_conversion=yes"));
Check("busy state clears after both archive operations",!vm.OptionalSdks.IsBusy&&vm.OptionalSdks.CanImport);
using(var frame=main.CaptureRenderedFrame()){if(frame==null)throw new Exception("No rendered frame");frame.Save(Path.Combine(output,"settings-truehdr.png"));}
using(var frame=window.CaptureRenderedFrame()){if(frame==null)throw new Exception("No rendered frame");frame.Save(Path.Combine(output,"optional-truehdr.png"));}
File.WriteAllText(Path.Combine(output,"summary.json"),JsonSerializer.Serialize(new{passed=true,checks,managed_root=vm.OptionalSdks.TrueHdrRoot,scope="Archive importer and the global TrueHDR choice with synthetic ELF archives and fixture modules; no video or GPU execution"},new JsonSerializerOptions{WriteIndented=true}));main.Close();window.Close();
