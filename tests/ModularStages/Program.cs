using AnimeJaNaiConfEditor.ViewModels;
using AnimeJaNaiConfEditor.Views;
using AnimeJaNai.Platform;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.LogicalTree;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ReactiveUI.Avalonia;
using Salaros.Configuration;
using System.Text.Json;

// UI contract only: fixture files do not represent qualified models or GPU engines.
string output=Path.GetFullPath(args[0]);Directory.CreateDirectory(output);
if(File.Exists(Path.Combine(output,"summary.json")))throw new Exception("Conclusive receipt exists; choose a new output only after a relevant change.");
string data=Path.Combine(output,"data"),inf=Path.Combine(data,"inference");
foreach(string d in new[]{data,inf,Path.Combine(output,"portable_config","script-opts"),Path.Combine(data,"onnx"),Path.Combine(data,"rife")})Directory.CreateDirectory(d);
Environment.SetEnvironmentVariable("ANIMEJANAI_DATA_DIR",data);Environment.SetEnvironmentVariable("ANIMEJANAI_INFERENCE_DIR",inf);Environment.SetEnvironmentVariable("ANIMEJANAI_ROOT",output);
File.WriteAllText(Path.Combine(data,"onnx","SPAN.onnx"),"UI fixture");File.WriteAllText(Path.Combine(data,"rife","rife_v4.26.onnx"),"UI fixture");
File.WriteAllText(Path.Combine(data,"animejanai.conf"),"[global]\nbackend=TensorRT\nrife_engine=quality\ndefault_slot=1\n[slot_1]\nprofile_name=My modular profile\nhdr_stage_controlled=yes\nchain_1_pipeline_version=1\nchain_1_stage_count=0\n");
AppBuilder.Configure<AnimeJaNaiConfEditor.App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions{UseHeadlessDrawing=false}).WithInterFont().UseReactiveUI(_=>{}).SetupWithoutStarting();
var checks=new List<string>();void Check(string name,bool ok){if(!ok)throw new Exception(name);checks.Add(name);Console.WriteLine("PASS "+name);}
var vm=new MainWindowViewModel();vm.HandleShowCustomProfile("1");vm.AnimeJaNaiConf.SetTensorRtSelected();var chain=vm.CurrentSlot.Chains.Single();chain.AutoSave=false;
if(args.Contains("--backends"))
{
    chain.AddDownscaleStage();Check("Linux TensorRT offers independent Area and Spline36",chain.Stages.Single().Method=="area"&&chain.Stages.Single().MethodChoices.SequenceEqual(new[]{"Area","Spline36"}));
    MainWindowViewModel.Platform=MainWindowViewModel.Os.Windows;vm.AnimeJaNaiConf.SetDirectMlSelected();
    chain.RefreshPipeline();Check("changing backend refreshes available technologies without rewriting imported step",chain.Stages[0].Method=="area"&&chain.Stages[0].TechnologyChoices.Any(t=>t.Name=="Spline36"&&t.Available));
    var dml=new UpscaleChain(false){Vm=vm,ChainNumber="2"};dml.AddDownscaleStage();dml.AddUpscaleStage();
    Check("DirectML creates historical Spline36 and keeps its runtime",dml.Stages[0].Method=="spline36"&&dml.Stages[0].Runtime=="directml"&&dml.Stages[0].MethodChoices.SequenceEqual(new[]{"Spline36"}));
    vm.AnimeJaNaiConf.SetTensorRtSelected();var winTrt=new UpscaleChain(false){Vm=vm,ChainNumber="3"};winTrt.AddDownscaleStage();
    Check("Windows TensorRT offers historical Spline36 without claiming independent CUDA",winTrt.Stages[0].Method=="spline36"&&winTrt.Stages[0].Runtime=="tensorrt"&&winTrt.Stages[0].MethodChoices.SequenceEqual(new[]{"Spline36"}));
    vm.AnimeJaNaiConf.SetOnnxRuntimeSelected(false);var winMi=new UpscaleChain(false){Vm=vm,ChainNumber="7"};winMi.AddDownscaleStage();
    MainWindowViewModel.Platform=MainWindowViewModel.Os.Linux;var linuxMi=new UpscaleChain(false){Vm=vm,ChainNumber="8"};linuxMi.AddDownscaleStage();
    Check("MIGraphX downscale follows platform arithmetic without changing imports",winMi.Stages.Single().Method=="spline36"&&linuxMi.Stages.Single().Method=="bilinear");
    MainWindowViewModel.Platform=MainWindowViewModel.Os.Linux;vm.AnimeJaNaiConf.SetVulkanSelected();var vulkan=new UpscaleChain(false){Vm=vm,ChainNumber="4"};vulkan.AddRifeStage();vulkan.AddDownscaleStage();
    Check("Vulkan starts with supported RIFE4.6 and historical Bilinear",vulkan.Stages[0].Model=="46"&&vulkan.Stages[0].Runtime=="vulkan"&&vulkan.Stages[1].Method=="bilinear");
    var legacy=new UpscaleChain(false){Vm=vm,ChainNumber="5"};legacy.LoadPipeline(new Dictionary<string,string>{{"chain_5_model_1_name","SPAN"},{"chain_5_model_1_resize_height_before_upscale","720"}},"vulkan","quality");
    Check("legacy non-TensorRT downscale remains labelled Bilinear",legacy.Stages[0].Method=="bilinear"&&legacy.Stages[0].MethodChoice=="Bilinear");
    MainWindowViewModel.Platform=MainWindowViewModel.Os.MacOS;vm.AnimeJaNaiConf.SetAppleEngine("Metal");var apple=new UpscaleChain(false){Vm=vm,ChainNumber="6"};apple.AddUpscaleStage();apple.AddDownscaleStage();
    Check("Apple stages use selected Apple engine, not TensorRT",apple.Stages.All(s=>s.Runtime=="metal")&&apple.Stages.Last().Method=="bilinear");
    File.WriteAllText(Path.Combine(output,"summary.json"),JsonSerializer.Serialize(new{passed=true,checks,scope="CPU new downscale defaults and imported resampling labels only"},new JsonSerializerOptions{WriteIndented=true}));return;
}
if(args.Contains("--backend-switch"))
{
    chain.AddRifeStage();var stage=chain.Stages.Single();stage.Enabled=true;stage.Policy="fast";stage.Model="426";stage.SceneThreshold="0.125";
    MainWindowViewModel.Platform=MainWindowViewModel.Os.Windows;vm.AnimeJaNaiConf.SetDirectMlSelected();chain.RefreshPipeline();
    Check("changing selected backend preserves imported execution until explicit action",stage.Runtime=="tensorrt"&&stage.Enabled&&stage.CanUseSelectedBackend);
    stage.UseSelectedBackend();Check("explicit runtime change disables stage and preserves model, policy, threshold",stage.Runtime=="directml"&&!stage.Enabled&&stage.Policy=="fast"&&stage.Model=="426"&&stage.SceneThreshold=="0.125"&&!stage.CanUseSelectedBackend);
    MainWindowViewModel.Platform=MainWindowViewModel.Os.Linux;vm.AnimeJaNaiConf.SetTensorRtSelected();chain.AddDownscaleStage();var area=chain.SelectedStage!;
    MainWindowViewModel.Platform=MainWindowViewModel.Os.Windows;vm.AnimeJaNaiConf.SetDirectMlSelected();chain.RefreshPipeline();area.UseSelectedBackend();
    Check("runtime action does not replace Area with a different algorithm",area.Method=="area"&&area.Runtime=="cuda"&&!area.CanUseSelectedBackend&&area.NeedsResizeTechnologyChange);
    File.WriteAllText(Path.Combine(output,"summary.json"),JsonSerializer.Serialize(new{passed=true,checks,scope="CPU explicit backend switch only; models and algorithms never substituted"},new JsonSerializerOptions{WriteIndented=true}));return;
}
if(args.Contains("--legacy-sizing"))
{
    var legacyImport=new UpscaleChain(false){Vm=vm,ChainNumber="2"};
    legacyImport.LoadPipeline(new Dictionary<string,string>{{"chain_2_model_1_name","SPAN"},{"chain_2_model_1_resize_height_before_upscale","720"}},"tensorrt","quality");
    var legacyResize=legacyImport.Stages.First();
    Check("imported geometry is marked and never advertised as a downscale ceiling",legacyResize.IsLegacySizing&&legacyResize.ModeNotice.StartsWith("Legacy sizing preserved")&&legacyResize.OrderDescription.StartsWith("Legacy resize")&&!legacyResize.OrderDescription.Contains("≤"));
    Check("legacy geometry stays in the raw options until explicit action",legacyResize.OptionsText.Contains("legacy_geometry=")&&legacyResize.TechnologyEditable);
    legacyResize.UseDownscaleOnlySizing();
    Check("downscale-only action removes only geometry marker and disables step",!legacyResize.IsLegacySizing&&!legacyResize.Enabled&&legacyResize.Method=="spline36"&&legacyResize.TargetHeight=="720"&&legacyResize.OrderDescription.Contains("≤"));
    File.WriteAllText(Path.Combine(output,"summary.json"),JsonSerializer.Serialize(new{passed=true,checks,scope="CPU imported geometry display and explicit opt-in only"},new JsonSerializerOptions{WriteIndented=true}));return;
}
var view=new InterpolationStages{DataContext=chain};var window=new Window{Content=new ScrollViewer{Content=view,Padding=new Thickness(24)},Width=1000,Height=1080};window.Show();Dispatcher.UIThread.RunJobs();
Check("empty profile stays empty and presents one add action",!chain.HasStages&&chain.SelectedStage==null&&view.GetVisualDescendants().OfType<Button>().Count(b=>b.Name=="AddProcessingStep")==1);
var add=view.FindControl<Button>("AddProcessingStep")!;add.Flyout!.ShowAt(add);Dispatcher.UIThread.RunJobs();
var menu=(StackPanel)((Flyout)add.Flyout).Content!;var choices=menu.GetLogicalDescendants().OfType<Button>().ToArray();
Check("add menu binds exactly four fixed categories",choices.Select(b=>b.Content?.ToString()).SequenceEqual(new[]{"Interpolation","Upscale","Downscale","HDR conversion"})&&choices.All(b=>b.Command!=null));
foreach(string name in new[]{"Downscale","Interpolation","Upscale","HDR conversion"})choices.Single(b=>b.Content?.ToString()==name).Command!.Execute(null);
add.Flyout.Hide();Dispatcher.UIThread.RunJobs();
Check("new stages are inactive, ordered and selection follows the new step",chain.Stages.Select(s=>s.Kind).SequenceEqual(new[]{"resize","interpolate","upscale","hdr"})&&chain.Stages.All(s=>!s.Enabled)&&chain.SelectedStage==chain.Stages.Last());
var resize=chain.Stages[0];var rife=chain.Stages[1];var upscale=chain.Stages[2];var hdr=chain.Stages[3];
Check("category technology choices do not cross categories",resize.MethodChoices.SequenceEqual(new[]{"Area","Spline36"})&&rife.MethodChoices.SequenceEqual(new[]{"RIFE"})&&upscale.MethodChoices.SequenceEqual(new[]{"ONNX upscale"})&&hdr.MethodChoices.SequenceEqual(new[]{"mpv HDR","NVIDIA TrueHDR"}));
resize.MethodChoice="RIFE";Check("attempted cross-category replacement leaves the downscale intact",resize.Kind=="resize"&&resize.Method=="area");
chain.SelectedStage=resize;Dispatcher.UIThread.RunJobs();
Check("only selected settings are rendered and RIFE mode stays hidden for downscale",view.GetVisualDescendants().OfType<ComboBox>().Count(c=>c.Name=="StageTechnology"&&c.IsEffectivelyVisible)==1&&!view.GetVisualDescendants().OfType<ComboBox>().Any(c=>c.Name=="RifeMode"&&c.IsEffectivelyVisible));
chain.SelectedStage=rife;Dispatcher.UIThread.RunJobs();
Check("RIFE mode appears only for RIFE and advanced settings start collapsed",view.GetVisualDescendants().OfType<ComboBox>().Any(c=>c.Name=="RifeMode"&&c.IsEffectivelyVisible)&&view.GetVisualDescendants().OfType<Expander>().All(e=>!e.IsExpanded));
string rifeId=rife.Id;chain.MoveStage(rife,1);Check("reorder preserves stage identity, settings and selection",chain.Stages[2]==rife&&chain.SelectedStage==rife&&rife.Id==rifeId&&rife.Policy=="quality");chain.MoveStage(rife,-1);
var second=new UpscaleChain(false){Vm=vm,ChainNumber="2"};vm.CurrentSlot.Chains.Add(second);chain.RefreshPipeline();second.RefreshPipeline();second.AddHdrStage();
Check("one HDR per profile includes inactive steps and other rules",!chain.CanAddHdrStage&&!second.CanAddHdrStage&&second.Stages.Count==0);
Check("mpv HDR is SDK-free and missing TrueHDR remains explicit",hdr.Method=="mpv"&&hdr.CanEnable&&hdr.TechnologyChoices.Single(t=>t.Name=="mpv HDR").Available&&!hdr.TechnologyChoices.Single(t=>t.Name=="NVIDIA TrueHDR").Available);
chain.SelectedStage=hdr;Dispatcher.UIThread.RunJobs();
using(var frame=window.CaptureRenderedFrame()){if(frame==null)throw new Exception("No rendered frame");frame.Save(Path.Combine(output,"modular-hdr-light.png"));}
chain.DeleteStage(hdr);Check("removal selects a surviving neighbour and unlocks HDR in other rules",chain.SelectedStage==upscale&&chain.CanAddHdrStage&&second.CanAddHdrStage);vm.CurrentSlot.Chains.Remove(second);
chain.AddRifeStage();var fast=chain.SelectedStage!;fast.Policy="fast";rife.Enabled=true;fast.Enabled=true;
Check("independent RIFE modes compose a fourfold cadence",rife.Policy=="quality"&&fast.Policy=="fast"&&chain.PipelineCadence=="24 → 48 → 96 fps (24 fps source example)");
var saved=Path.Combine(output,"saved.conf");vm.WriteAnimeJaNaiConf(saved);var loaded=vm.ReadAnimeJaNaiConf(saved).UpscaleSlots.Single().Chains.Single();
Check("save reload keeps ordered IDs, per-stage modes and factors",loaded.Stages.Select(s=>s.Id).SequenceEqual(chain.Stages.Select(s=>s.Id))&&loaded.Stages.Where(s=>s.IsRife).Select(s=>s.Policy).SequenceEqual(new[]{"quality","fast"}));
// A removed provider remains visible, can be disabled, and is not rewritten by opening it.
var originalOptions=new Dictionary<string,string>{{"sdk_library","/custom/original.so"},{"sdk_runtime","/custom/runtime.so"},{"kernel_fatbin","/custom/original.fatbin"}};
var frucPlan=new InterpolationPipeline.Plan(false,[new("imported-fruc","interpolate","nvidia-fruc","cuda",true,"",new(2,1),"fast",originalOptions)]);
var imported=new UpscaleChain(false){Vm=vm,ChainNumber="3"};imported.LoadPipeline(InterpolationPipeline.Write(frucPlan,3),"tensorrt","quality");var fruc=imported.Stages.Single();
Check("imported FRUC stays explicit and can always be disabled",fruc.IsFruc&&fruc.CanEnable&&fruc.Availability.Contains("no longer supported")&&!fruc.TechnologyChoices.Single(t=>t.Name.Contains("FRUC")).Available);
string raw=fruc.OptionsText;fruc.Enabled=false;Check("retired FRUC cannot be reenabled and its paths remain unchanged",!fruc.CanEnable&&fruc.OptionsText==raw&&fruc.TechnologyChoices.Any(t=>t.Name=="RIFE"));
MainWindowViewModel.Platform=MainWindowViewModel.Os.Windows;vm.AnimeJaNaiConf.SetDirectMlSelected();var direct=new UpscaleChain(false){Vm=vm,ChainNumber="4"};direct.AddRifeStage();direct.AddUpscaleStage();Check("new platform stages follow DirectML instead of forcing TensorRT",direct.Stages.All(s=>s.Runtime=="directml"));
MainWindowViewModel.Platform=MainWindowViewModel.Os.Linux;vm.AnimeJaNaiConf.SetTensorRtSelected();
Application.Current!.RequestedThemeVariant=ThemeVariant.Dark;chain.SelectedStage=rife;window.Width=780;Dispatcher.UIThread.RunJobs();
using(var frame=window.CaptureRenderedFrame()){if(frame==null)throw new Exception("No rendered frame");frame.Save(Path.Combine(output,"modular-rife-dark-compact.png"));}
File.WriteAllText(Path.Combine(output,"summary.json"),JsonSerializer.Serialize(new{passed=true,checks,scope="CPU/headless editor only; no GPU or video quality/performance claim"},new JsonSerializerOptions{WriteIndented=true}));window.Close();
