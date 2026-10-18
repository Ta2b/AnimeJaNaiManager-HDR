using AnimeJaNaiConfEditor.ViewModels;
using AnimeJaNaiConfEditor.Views;
using AnimeJaNai.Platform;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ReactiveUI.Avalonia;
using Salaros.Configuration;
using System.Text.Json;

// Real headless keyboard clicks on the production Flyout buttons (no direct AddStage calls).
string output=Path.GetFullPath(args[0]);Directory.CreateDirectory(output);
if(File.Exists(Path.Combine(output,"summary.json")))throw new Exception("Conclusive receipt exists; no replay.");
string data=Path.Combine(output,"data"),inference=Path.Combine(data,"inference");Directory.CreateDirectory(Path.Combine(data,"onnx"));Directory.CreateDirectory(Path.Combine(data,"rife"));Directory.CreateDirectory(inference);
Environment.SetEnvironmentVariable("ANIMEJANAI_DATA_DIR",data);
Environment.SetEnvironmentVariable("ANIMEJANAI_INFERENCE_DIR",inference);
Environment.SetEnvironmentVariable("ANIMEJANAI_ROOT",output);
Directory.CreateDirectory(Path.Combine(output,"portable_config"));
File.WriteAllText(Path.Combine(data,"onnx","SPAN.onnx"),"UI fixture");
File.WriteAllText(Path.Combine(data,"animejanai.conf"),"[global]\nconfig_version=4\nbackend=TensorRT\ndefault_slot=1\n[slot_1]\nprofile_name=Add step fixture\nchain_1_pipeline_version=1\nchain_1_stage_count=0\n");
AppBuilder.Configure<AnimeJaNaiConfEditor.App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions{UseHeadlessDrawing=false}).WithInterFont().UseReactiveUI(_=>{}).SetupWithoutStarting();
var checks=new List<string>();
void Check(string name,bool ok){if(!ok)throw new Exception(name);checks.Add(name);Console.WriteLine("PASS "+name);}
var vm=new MainWindowViewModel();vm.HandleShowCustomProfile("1");
var chain=vm.CurrentSlot.Chains.Single();
var view=new InterpolationStages{DataContext=chain};
var window=new Window{Content=new ScrollViewer{Content=view},Width=1100,Height=1100};window.Show();Dispatcher.UIThread.RunJobs();
var add=view.FindControl<Button>("AddProcessingStep")!;
var flyout=(Flyout)add.Flyout!;
Button[] Categories()=>((Control)flyout.Content!).GetVisualDescendants().OfType<Button>().ToArray();
Button Category(string label)=>Categories().Single(b=>Equals(b.Content,label));
void Press(Button button)
{
    Check("button enabled and focused: "+button.Content,button.IsEffectivelyEnabled&&button.Focus());
    var root=TopLevel.GetTopLevel(button)??throw new Exception("Button is not attached to a TopLevel");
    root.KeyPress(Key.Space,RawInputModifiers.None,PhysicalKey.Space," ");
    root.KeyRelease(Key.Space,RawInputModifiers.None,PhysicalKey.Space," ");
    Dispatcher.UIThread.RunJobs();
}
InterpolationStage Add(string label,string kind)
{
    Press(add);Check("category menu opened: "+label,flyout.IsOpen);
    int before=chain.Stages.Count;Press(Category(label));
    Check("menu click adds and selects "+kind,chain.Stages.Count==before+1&&chain.SelectedStage?.Kind==kind);
    Check("menu closes after "+kind,!flyout.IsOpen);
    return chain.SelectedStage!;
}
try
{
    Press(add);
    Check("the menu offers three categories and no HDR step",Categories().Select(b=>b.Content?.ToString()).SequenceEqual(new[]{"Interpolation","Upscale","Downscale"}));
    flyout.Hide();Dispatcher.UIThread.RunJobs();
    var rife=Add("Interpolation","interpolate");var upscale=Add("Upscale","upscale");var resize=Add("Downscale","resize");
    Check("without TensorRT the new steps stay inactive and say why",new[]{rife,upscale,resize}.All(s=>!s.Enabled&&s.IsBlocked==false&&s.State=="inactive")&&upscale.Availability.StartsWith("TensorRT is not installed"));
    // Install the files the selected engine needs (fixtures): a new step is then created active.
    foreach(var file in new[]{"libaji_trt.so","libnvinfer.so.11","libaji.so","trtexec"})File.WriteAllText(Path.Combine(inference,file),"UI fixture");
    chain.RefreshPipeline();Dispatcher.UIThread.RunJobs();
    var second=Add("Upscale","upscale");
    Check("a step whose files are present is created active",second.Enabled&&second.State=="active");
    Check("a step whose model is missing stays inactive with its cause",!rife.Enabled&&rife.Availability.Contains("RIFE"));
    string saved=Path.Combine(output,"saved.conf");vm.WriteAnimeJaNaiConf(saved);
    var loaded=vm.ReadAnimeJaNaiConf(saved).UpscaleSlots.Single(s=>s.SlotNumber=="1").Chains.Single();
    Check("save/reload preserves categories, ids, order and inactive settings",loaded.Stages.Select(s=>(s.Id,s.Kind,s.Method,s.Enabled)).SequenceEqual(chain.Stages.Select(s=>(s.Id,s.Kind,s.Method,s.Enabled))));
    // Set up the boundary without replaying the successful click cases.
    while(chain.Stages.Count<InterpolationPipeline.MaxStages)chain.AddDownscaleStage();
    Dispatcher.UIThread.RunJobs();
    Check("reader limit disables the add menu",chain.Stages.Count==16&&!add.IsEffectivelyEnabled);
    var future=vm.ReadAnimeJaNaiConf(new ConfigParser("[global]\nbackend=TensorRT\n[slot_1]\nprofile_name=Future\nchain_1_pipeline_version=99\nchain_1_stage_count=1\nchain_1_stage_1_future_option=keep\n"));
    view.DataContext=future.UpscaleSlots[0].Chains[0];Dispatcher.UIThread.RunJobs();
    Check("unsupported schema remains read-only",!add.IsEffectivelyEnabled);
    File.WriteAllText(Path.Combine(output,"summary.json"),JsonSerializer.Serialize(new{passed=true,checks,scope="Real headless keyboard clicks on the production Flyout buttons; no direct AddStage calls except the boundary; fixture files only."},new JsonSerializerOptions{WriteIndented=true}));
}
catch(Exception e){File.WriteAllText(Path.Combine(output,"failure.json"),JsonSerializer.Serialize(new{passed=false,error=e.Message,checks},new JsonSerializerOptions{WriteIndented=true}));throw;}
finally{window.Close();}
