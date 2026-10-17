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

string output=Path.GetFullPath(args[0]);Directory.CreateDirectory(output);
if(File.Exists(Path.Combine(output,"summary.json")))throw new Exception("Conclusive receipt exists; no replay.");
string data=Path.Combine(output,"data");Directory.CreateDirectory(data);
Environment.SetEnvironmentVariable("ANIMEJANAI_DATA_DIR",data);
Environment.SetEnvironmentVariable("ANIMEJANAI_INFERENCE_DIR",Path.Combine(data,"inference"));
Environment.SetEnvironmentVariable("ANIMEJANAI_ROOT",output);
Directory.CreateDirectory(Path.Combine(output,"portable_config"));
File.WriteAllText(Path.Combine(data,"animejanai.conf"),"[global]\nbackend=TensorRT\ndefault_slot=1\n[slot_1]\nprofile_name=Add step fixture\nhdr_stage_controlled=yes\nchain_1_pipeline_version=1\nchain_1_stage_count=0\n");
AppBuilder.Configure<AnimeJaNaiConfEditor.App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions{UseHeadlessDrawing=false}).WithInterFont().UseReactiveUI(_=>{}).SetupWithoutStarting();
var checks=new List<string>();
void Check(string name,bool ok){if(!ok)throw new Exception(name);checks.Add(name);Console.WriteLine("PASS "+name);}
var vm=new MainWindowViewModel();vm.HandleShowCustomProfile("1");
var chain=vm.CurrentSlot.Chains.Single();
var view=new InterpolationStages{DataContext=chain};
var window=new Window{Content=new ScrollViewer{Content=view},Width=1100,Height=1100};window.Show();Dispatcher.UIThread.RunJobs();
var add=view.FindControl<Button>("AddProcessingStep")!;
var flyout=(Flyout)add.Flyout!;
Button Category(string label)=>((Control)flyout.Content!).GetVisualDescendants().OfType<Button>().Single(b=>Equals(b.Content,label));
void Press(Button button)
{
    Check("button enabled and focused: "+button.Content,button.IsEffectivelyEnabled&&button.Focus());
    var root=TopLevel.GetTopLevel(button)??throw new Exception("Button is not attached to a TopLevel");
    root.KeyPress(Key.Space,RawInputModifiers.None,PhysicalKey.Space," ");
    root.KeyRelease(Key.Space,RawInputModifiers.None,PhysicalKey.Space," ");
    Dispatcher.UIThread.RunJobs();
}
void Add(string label,string kind)
{
    Press(add);Check("category menu opened: "+label,flyout.IsOpen);
    var button=Category(label);
    Console.WriteLine(JsonSerializer.Serialize(new{category=label,context=button.DataContext?.GetType().Name,command=button.Command?.GetType().Name,enabled=button.IsEffectivelyEnabled,count=chain.Stages.Count}));
    int before=chain.Stages.Count;Press(button);
    Check("menu click adds and selects "+kind,chain.Stages.Count==before+1&&chain.SelectedStage?.Kind==kind&&!chain.SelectedStage.Enabled);
    Check("menu closes after "+kind,!flyout.IsOpen);
}
try
{
    Add("Interpolation","interpolate");Add("Upscale","upscale");Add("Downscale","resize");Add("HDR conversion","hdr");
    Press(add);Check("second HDR is unavailable in menu",!Category("HDR conversion").IsEffectivelyEnabled);flyout.Hide();
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
    File.WriteAllText(Path.Combine(output,"summary.json"),JsonSerializer.Serialize(new{passed=true,checks,scope="Real headless keyboard clicks on the production Flyout buttons; no direct AddStage calls; private profile fixture only."},new JsonSerializerOptions{WriteIndented=true}));
}
catch(Exception e){File.WriteAllText(Path.Combine(output,"failure.json"),JsonSerializer.Serialize(new{passed=false,error=e.Message,checks},new JsonSerializerOptions{WriteIndented=true}));throw;}
finally{window.Close();}
