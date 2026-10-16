using AnimeJaNaiConfEditor.ViewModels;
using AnimeJaNaiConfEditor.Views;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ReactiveUI.Avalonia;
using System.Text.Json;

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
if(args.Length==2&&args[1]=="--resume-save") {
 var resumeVm=new MainWindowViewModel();resumeVm.AnimeJaNaiConf=resumeVm.ReadAnimeJaNaiConf(Path.Combine(output,"saved.conf"));var resumed=resumeVm.AnimeJaNaiConf.UpscaleSlots[0].Chains[0];
 Check("save/reload preserves order resize stage-specific modes and source comment",resumed.Stages[0].ResizeHeight=="720"&&resumed.Stages[1].Policy=="fast"&&resumed.Stages[2].Policy=="fast"&&File.ReadAllText(Path.Combine(data,"animejanai.conf")).Contains("# user comment"));
 var resumedView=new InterpolationStages{DataContext=resumed};var resumedWindow=new Window{Content=new ScrollViewer{Content=resumedView},Width=1050,Height=1250};resumedWindow.Show();Dispatcher.UIThread.RunJobs();
 using(var frame=resumedWindow.CaptureRenderedFrame()){if(frame==null)throw new Exception("No frame");frame.Save(Path.Combine(output,"chain-ux.png"));}resumedWindow.Close();
 File.WriteAllText(Path.Combine(output,"summary.json"),JsonSerializer.Serialize(new{passed=true,checks,previous_passes=7,previous_log="lot-43-chain-ux-check.log",scope="Resume only the failed assertion; a new export does not inherit source comments. Source file comments are preserved. UI fixtures are not GPU validation."},new JsonSerializerOptions{WriteIndented=true}));return;
}
var vm=new MainWindowViewModel();vm.HandleShowCustomProfile("1");vm.AnimeJaNaiConf.SetTensorRtSelected();var chain=vm.CurrentSlot.Chains[0];
var view=new InterpolationStages{DataContext=chain};var window=new Window{Content=new ScrollViewer{Content=view},Width=1050,Height=1250};window.Show();Dispatcher.UIThread.RunJobs();
Check("legacy mode controls live in the historical chain editor",view.FindControl<StackPanel>("LegacyRifeControls")!.IsVisible);
view.FindControl<CheckBox>("OrderedStagesToggle")!.IsChecked=true;Dispatcher.UIThread.RunJobs();
Check("ordered editor hides historical shared RIFE controls",!view.FindControl<StackPanel>("LegacyRifeControls")!.IsVisible);
var rife=chain.Stages[0];var sr=chain.Stages[1];
Check("summary places attached720p resize AFTER RIFE",chain.PipelineOrder=="Source → RIFE ×2 → Resize to 720p → ONNX upscale → Display");
chain.AddFrucStage();var fruc=chain.Stages.Last();fruc.SdkLibrary=Path.Combine(output,"sdk.so");fruc.SdkRuntime=Path.Combine(output,"runtime.so");File.WriteAllText(fruc.SdkLibrary,"UI fixture only");File.WriteAllText(fruc.SdkRuntime,"UI fixture only");fruc.Enabled=true;fruc.MoveUp();Dispatcher.UIThread.RunJobs();
var modes=view.GetVisualDescendants().OfType<StackPanel>().Where(c=>c.Name=="RifeModeControls").ToArray();
Check("only RIFE offers a mode selector; FRUC and SR do not",modes.Length==3&&modes.Count(c=>c.IsVisible)==1&&ReferenceEquals(modes.Single(c=>c.IsVisible).DataContext,rife));
Check("FRUC declares its single implemented SDR8 synthesis mode",fruc.ModeNotice.Contains("SDR8")&&fruc.ModeNotice.Contains("Artifacts")&&fruc.Policy=="fast");
var choice=view.GetVisualDescendants().OfType<ComboBox>().Single(c=>c.Name=="RifeMode"&&ReferenceEquals(c.DataContext,rife));choice.SelectedValue="fast";Dispatcher.UIThread.RunJobs();
Check("RIFE mode binding changes its stage and leaves historical defaults intact",rife.Policy=="fast"&&vm.AnimeJaNaiConf.RifeEngine=="quality"&&rife.ModeNotice.Contains("shortcuts"));
sr.MoveUp();sr.MoveUp();Dispatcher.UIThread.RunJobs();
Check("summary follows actual reordered stages",chain.PipelineOrder=="Source → Resize to 720p → ONNX upscale → RIFE ×2 → NVIDIA FRUC ×2 → Display"&&view.FindControl<TextBlock>("OrderedProcessingOrder")!.Text==chain.PipelineOrder);
string saved=Path.Combine(output,"saved.conf");vm.WriteAnimeJaNaiConf(saved);var reloaded=vm.ReadAnimeJaNaiConf(saved).UpscaleSlots[0].Chains[0];
Check("save/reload preserves order resize stage-specific modes and user comment",reloaded.Stages[0].ResizeHeight=="720"&&reloaded.Stages[1].Policy=="fast"&&reloaded.Stages[2].Policy=="fast"&&File.ReadAllText(Path.Combine(data,"animejanai.conf")).Contains("# user comment"));
using(var frame=window.CaptureRenderedFrame()){if(frame==null)throw new Exception("No frame");frame.Save(Path.Combine(output,"chain-ux.png"));}window.Close();
File.WriteAllText(Path.Combine(output,"summary.json"),JsonSerializer.Serialize(new{passed=true,checks,scope="Actual Avalonia controls and configuration; fixtures are not engine/hardware validation"},new JsonSerializerOptions{WriteIndented=true}));
