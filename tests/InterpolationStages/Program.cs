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

if(args.Length!=1)throw new Exception("Usage: InterpolationStages OUTPUT_DIRECTORY (private owned directory)");
string output=Path.GetFullPath(args[0]);Directory.CreateDirectory(output);
if(File.Exists(Path.Combine(output,"summary.json")))throw new Exception("Qualification receipt already exists; do not repeat a conclusive check.");
string data=Path.Combine(output,"fixture"),inference=Path.Combine(data,"inference");
foreach(string dir in new[]{data,inference,Path.Combine(data,"onnx"),Path.Combine(data,"rife")})Directory.CreateDirectory(dir);
Environment.SetEnvironmentVariable("ANIMEJANAI_DATA_DIR",data);
Environment.SetEnvironmentVariable("ANIMEJANAI_INFERENCE_DIR",inference);
Environment.SetEnvironmentVariable("ANIMEJANAI_ROOT",output);
const string span="2x_AnimeJaNai_HD_V3.1Sharp1_Performance_SPANF3_b5f48_unshuffle_fp16";
// These are filesystem/UI fixtures, never loaded as an engine or model.
foreach(string name in new[]{"libnvinfer.so.11","libaji_pipeline.so","aji_pipeline.fatbin","libaji_trt.so","libaji_fruc.so","aji_fruc.fatbin"})File.WriteAllText(Path.Combine(inference,name),"UI fixture");
File.WriteAllText(Path.Combine(data,"rife/rife_v4.26.onnx"),"UI fixture");
File.WriteAllText(Path.Combine(data,"onnx",span+".onnx"),"UI fixture");
string path=Path.Combine(data,"animejanai.conf");
File.WriteAllText(path,"# user comment\n[global]\nconfig_version=3\nbackend=TensorRT\nrife_engine=quality\ndefault_slot=1\ncustom_global=keep\n[slot_1]\nprofile_name=Stage check\nchain_1_rife=yes\nchain_1_rife_model=426\nchain_1_rife_factor_numerator=5\nchain_1_rife_factor_denominator=2\nchain_1_rife_before_upscale=yes\nchain_1_rife_scene_detect_threshold=0.150\nchain_1_model_1_name="+span+"\n");
AppBuilder.Configure<AnimeJaNaiConfEditor.App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions{UseHeadlessDrawing=false}).WithInterFont().UseReactiveUI(_=>{}).SetupWithoutStarting();
var checks=new List<string>();
void Check(string name,bool ok){if(!ok)throw new Exception(name);checks.Add(name);Console.WriteLine("PASS "+name);}
var vm=new MainWindowViewModel();vm.HandleShowCustomProfile("1");
vm.AnimeJaNaiConf.SetTensorRtSelected();
var chain=vm.CurrentSlot.Chains[0];
var control=new InterpolationStages{DataContext=chain};
var window=new Window{Content=new ScrollViewer{Content=control},Width=1000,Height=1000};window.Show();Dispatcher.UIThread.RunJobs();
var toggle=control.FindControl<CheckBox>("OrderedStagesToggle")!;
toggle.IsChecked=true;Dispatcher.UIThread.RunJobs();
Check("UI toggle migrates legacy RIFE5/2 and SPAN in original order",chain.OrderedPipeline&&chain.Stages.Count==2&&chain.Stages[0].Numerator=="5"&&chain.Stages[0].Denominator=="2"&&chain.Stages[1].Model==span);
string legacy=Path.Combine(output,"legacy-compatible.conf");vm.WriteAnimeJaNaiConf(legacy);
Check("lossless compatibility keys preserve legacy fraction and upscaler",File.ReadAllText(legacy).Contains("chain_1_rife_factor_denominator=2")&&File.ReadAllText(legacy).Contains("chain_1_model_1_name="+span));
toggle.IsChecked=false;Dispatcher.UIThread.RunJobs();
Check("UI can return to unchanged legacy editor",!chain.OrderedPipeline&&chain.RifeFactorNumerator==5&&chain.RifeFactorDenominator==2);
toggle.IsChecked=true;Dispatcher.UIThread.RunJobs();
var rife=chain.Stages[0];rife.Numerator="2";rife.Denominator="1";Dispatcher.UIThread.RunJobs();
var box=control.GetVisualDescendants().OfType<TextBox>().First(x=>x.Name=="StageNumerator"&&ReferenceEquals(x.DataContext,rife));
Check("stage property notification reaches factor textbox",box.Text=="2");
void Click(string text,object? context=null)
{
 var b=control.GetVisualDescendants().OfType<Button>().First(x=>Equals(x.Content,text)&&(context==null||ReferenceEquals(x.DataContext,context)));
 if(!b.IsEnabled||b.Command==null)throw new Exception("Button disabled or unbound: "+text);
 b.Command.Execute(b.CommandParameter);Dispatcher.UIThread.RunJobs();
}
Click("Add NVIDIA FRUC");var fruc=chain.Stages.Last();
Check("new FRUC starts inactive with explicit Fast policy and missing SDK diagnosis",!fruc.Enabled&&fruc.Policy=="fast"&&!fruc.CanEnable&&fruc.Availability.Contains("SDK"));
string sdk=Path.Combine(output,"sdk-fixture.so"),runtime=Path.Combine(output,"runtime-fixture.so");File.WriteAllText(sdk,"UI fixture");File.WriteAllText(runtime,"UI fixture");
fruc.SdkLibrary=sdk;fruc.SdkRuntime=runtime;Dispatcher.UIThread.RunJobs();
Check("complete SDK paths permit explicit activation without claiming GPU validation",fruc.CanEnable&&fruc.Availability.Contains("negotiated"));
var active=control.GetVisualDescendants().OfType<CheckBox>().First(x=>Equals(x.Content,"Active")&&ReferenceEquals(x.DataContext,fruc));active.IsChecked=true;Dispatcher.UIThread.RunJobs();
Check("active checkbox writes stage",fruc.Enabled);
string id=fruc.Id;Click("↑",fruc);
Check("UI moves FRUC between RIFE and SPAN with stable identity",chain.Stages[1]==fruc&&fruc.Id==id&&chain.Stages[2].Model==span);
Check("cumulative example cadence is24→48→96",chain.PipelineCadence.Contains("24 → 48 → 96")&&!chain.CanUseLegacyEditor);
string mixed=Path.Combine(output,"mixed.conf");vm.WriteAnimeJaNaiConf(mixed);
var round=vm.ReadAnimeJaNaiConf(mixed).UpscaleSlots[0].Chains[0];
Check("full export reload preserves order factors IDs and SDK paths",round.Stages.Count==3&&round.Stages[1].Id==id&&round.Stages[1].SdkLibrary==sdk&&round.Stages[2].Model==span);
string profile=Path.Combine(output,"profile.conf");vm.WriteAnimeJaNaiCurrentProfileConf(profile);vm.ReadAnimeJaNaiConfToCurrentSlot(profile);
Check("profile export import preserves stages",vm.CurrentSlot.Chains[0].Stages[1].Id==id);
// Unknown options stay attached to their stable stage through reorder and save.
string unknown=Path.Combine(output,"unknown.conf");File.WriteAllText(unknown,File.ReadAllText(mixed)+"chain_1_stage_2_future_option=opaque\n");
var unknownConf=vm.ReadAnimeJaNaiConf(unknown);vm.AnimeJaNaiConf=unknownConf;var uc=unknownConf.UpscaleSlots[0].Chains[0];uc.Stages[1].MoveDown();vm.WriteAnimeJaNaiConf(unknown);
Check("unknown option survives reorder and has explicit unavailable diagnosis",File.ReadAllText(unknown).Contains("chain_1_stage_3_future_option=opaque")&&uc.Stages[2].Availability.Contains("not implemented"));
string future=Path.Combine(output,"future.conf");File.WriteAllText(future,File.ReadAllText(mixed).Replace("pipeline_version=1","pipeline_version=9"));
vm.AnimeJaNaiConf=vm.ReadAnimeJaNaiConf(future);var fc=vm.AnimeJaNaiConf.UpscaleSlots[0].Chains[0];vm.WriteAnimeJaNaiConf(future);
Check("future version preserved read-only",!fc.PipelineEditable&&File.ReadAllText(future).Contains("pipeline_version=9")&&File.ReadAllText(future).Contains("chain_1_stage_2_sdk_library="+sdk));
vm.AnimeJaNaiConf=vm.ReadAnimeJaNaiConf(mixed,true);chain=vm.AnimeJaNaiConf.UpscaleSlots[0].Chains[0];control.DataContext=chain;Dispatcher.UIThread.RunJobs();
chain.Stages[1].Enabled=false;string disabled=Path.Combine(output,"disabled.conf");vm.WriteAnimeJaNaiConf(disabled);
Check("disabled stage preserved on reload",!vm.ReadAnimeJaNaiConf(disabled).UpscaleSlots[0].Chains[0].Stages[1].Enabled);
chain.Stages[1].Enabled=true;
Click("Add RIFE");var extra=chain.Stages.Last();Click("Remove stage",extra);
Check("UI remove writes a complete consistent stage list",chain.Stages.Count==3&&File.ReadAllText(path).Contains("chain_1_stage_count=3"));
Check("source comments and unrelated global keys preserved",File.ReadAllText(path).Contains("# user comment")&&File.ReadAllText(path).Contains("custom_global=keep"));
using(var frame=window.CaptureRenderedFrame()){if(frame==null)throw new Exception("No rendered frame");frame.Save(Path.Combine(output,"stages.png"));}
window.Close();
File.WriteAllText(Path.Combine(output,"summary.json"),JsonSerializer.Serialize(new{passed=true,checks,scope="Headless actual control/config only; filesystem fixtures are not real SDK/GPU validation"},new JsonSerializerOptions{WriteIndented=true}));
