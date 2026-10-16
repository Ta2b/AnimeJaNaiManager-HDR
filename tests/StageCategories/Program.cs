using AnimeJaNaiConfEditor.ViewModels;
using AnimeJaNaiConfEditor.Views;
using AnimeJaNaiConfEditor.Services;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Avalonia.LogicalTree;
using ReactiveUI.Avalonia;
using System.Text.Json;
string output=Path.GetFullPath(args[0]),data=Path.GetFullPath(args[1]);Directory.CreateDirectory(output);
if(args.Length<5&&File.Exists(Path.Combine(output,"summary.json")))throw new Exception("Existing conclusive receipt.");
Environment.SetEnvironmentVariable("ANIMEJANAI_DATA_DIR",data);Environment.SetEnvironmentVariable("ANIMEJANAI_INFERENCE_DIR",Path.Combine(data,"inference"));Environment.SetEnvironmentVariable("ANIMEJANAI_ROOT",Path.GetDirectoryName(data));
AppBuilder.Configure<AnimeJaNaiConfEditor.App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions{UseHeadlessDrawing=false}).WithInterFont().UseReactiveUI(_=>{}).SetupWithoutStarting();
var checks=new List<string>();void Check(string name,bool ok){if(!ok)throw new Exception(name);checks.Add(name);Console.WriteLine("PASS "+name);}
var vm=new MainWindowViewModel();vm.HandleShowCustomProfile("1");vm.AnimeJaNaiConf.SetTensorRtSelected();var chain=vm.CurrentSlot.Chains[0];chain.EnableOrderedPipeline();
var view=new InterpolationStages{DataContext=chain};var window=new Window{Content=new ScrollViewer{Content=view},Width=1100,Height=1200};window.Show();Dispatcher.UIThread.RunJobs();
if(args.Length>=5&&args[4]=="--render-only") {
 using(var frame=window.CaptureRenderedFrame()){if(frame==null)throw new Exception("No frame");frame.Save(Path.Combine(output,"stage-categories-compact.png"));}window.Close();Console.WriteLine("Rendered compact category editor; no conclusive cases replayed.");return;
}
var buttons=view.GetVisualDescendants().OfType<Button>().Where(b=>b.Content is string s&&s.StartsWith("Add ")).ToArray();
if(args.Length<4)Check("four category buttons replace per-engine additions",buttons.Select(b=>b.Content).SequenceEqual(new[]{"Add interpolation","Add upscale","Add downscale","Add HDR"}));
if(args.Length<4)Check("uninstalled downscale and HDR stages are explicitly disabled",!buttons[2].IsEnabled&&!buttons[3].IsEnabled);
buttons[0].Command!.Execute(null);Dispatcher.UIThread.RunJobs();var stage=chain.Stages.Last();string id=stage.Id;
if(args.Length<4)Check("interpolation technology list contains only RIFE and FRUC",stage.Category=="Interpolation"&&stage.MethodChoices.SequenceEqual(new[]{"RIFE","NVIDIA FRUC"}));
var choice=view.GetVisualDescendants().OfType<ComboBox>().Single(c=>c.Name=="StageTechnology"&&ReferenceEquals(c.DataContext,stage));
choice.SelectedItem=stage.TechnologyChoices.Single(t=>t.Name=="NVIDIA FRUC");Dispatcher.UIThread.RunJobs();
Check("technology selection changes RIFE to FRUC within the same category",stage.IsFruc&&stage.Category=="Interpolation"&&stage.Kind=="interpolate"&&stage.Id==id&&stage.Policy=="fast"&&stage.SdkLibrary==vm.OptionalSdks.InstalledFruc!.Library);
buttons[1].Command!.Execute(null);Dispatcher.UIThread.RunJobs();var sr=chain.Stages.Last();string original=sr.MethodChoice;sr.MethodChoice="NVIDIA FRUC";
Check("an upscaler cannot be transformed into interpolation",sr.Category=="Upscale"&&sr.MethodChoice==original&&sr.MethodChoices.SequenceEqual(new[]{"ONNX upscale"}));
stage.MethodChoice="ONNX upscale";Check("interpolation cannot be transformed into an upscaler",stage.IsFruc&&stage.Kind=="interpolate");
// Test disabled choice containers with a preserved current FRUC profile.
MainWindowViewModel.Platform=MainWindowViewModel.Os.Windows;vm.OptionalSdks.Refresh();choice.IsDropDownOpen=true;Dispatcher.UIThread.RunJobs();
Check("actual popup item disables FRUC and keeps RIFE active",choice.ContainerFromIndex(1) is ComboBoxItem f&&!f.IsEnabled&&choice.ContainerFromIndex(0) is ComboBoxItem r&&r.IsEnabled);
Check("FRUC choice is disabled on Windows while RIFE stays selectable",!stage.TechnologyChoices.Single(t=>t.Name=="NVIDIA FRUC").Available&&stage.TechnologyChoices.Single(t=>t.Name=="RIFE").Available);
choice.IsDropDownOpen=false;MainWindowViewModel.Platform=MainWindowViewModel.Os.Linux;vm.OptionalSdks.Refresh();Dispatcher.UIThread.RunJobs();
string saved=Path.Combine(output,"categories.conf");vm.WriteAnimeJaNaiConf(saved);var reloaded=vm.ReadAnimeJaNaiConf(saved).UpscaleSlots[0].Chains[0];
Check("save/reload preserves fixed categories and chosen technologies",reloaded.Stages.Any(s=>s.Id==id&&s.Kind=="interpolate"&&s.Method=="nvidia-fruc")&&reloaded.Stages.Last().Kind=="upscale");
// New safety guards only: do not repeat the16 conclusive archive-import checks.
string archive=Path.GetFullPath(args[2]),futureData=Path.Combine(output,"future-data"),current=Path.Combine(futureData,"optional-sdks/fruc/current.json");Directory.CreateDirectory(Path.GetDirectoryName(current)!);File.WriteAllText(current,"{\"Version\":9,\"future\":\"keep\"}");string before=File.ReadAllText(current);bool rejected=false;
try{new FrucSdkStore(futureData).Import(archive);}catch(InvalidDataException){rejected=true;}Check("newer installation registry is preserved without overwrite",rejected&&File.ReadAllText(current)==before);
string linkedData=Path.Combine(output,"linked-data"),userFolder=Path.Combine(output,"preserved-user-folder");Directory.CreateDirectory(userFolder);File.WriteAllText(Path.Combine(userFolder,"keep.txt"),"untouched");Directory.CreateDirectory(Path.Combine(linkedData,"optional-sdks"));Directory.CreateSymbolicLink(Path.Combine(linkedData,"optional-sdks/fruc"),userFolder);rejected=false;
try{new FrucSdkStore(linkedData).Import(archive);}catch(InvalidDataException){rejected=true;}Check("managed directory symlinks cannot redirect an import into user files",rejected&&Directory.EnumerateFiles(userFolder).Count()==1&&File.ReadAllText(Path.Combine(userFolder,"keep.txt"))=="untouched");
using(var frame=window.CaptureRenderedFrame()){if(frame==null)throw new Exception("No frame");frame.Save(Path.Combine(output,"stage-categories.png"));}window.Close();
File.WriteAllText(Path.Combine(output,"summary.json"),JsonSerializer.Serialize(new{passed=true,checks,scope="New category controls/configuration and new registry/symlink guards only; previous SDK cases not replayed"},new JsonSerializerOptions{WriteIndented=true}));
