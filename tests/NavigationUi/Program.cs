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
string data=Path.Combine(output,"data");Directory.CreateDirectory(Path.Combine(data,"onnx"));Directory.CreateDirectory(Path.Combine(data,"rife"));
Directory.CreateDirectory(Path.Combine(output,"portable_config"));
Environment.SetEnvironmentVariable("ANIMEJANAI_DATA_DIR",data);Environment.SetEnvironmentVariable("ANIMEJANAI_ROOT",output);
File.WriteAllText(Path.Combine(output,"portable_config","mpv.conf"),"# fixture\n");
File.WriteAllText(Path.Combine(data,"onnx","SPAN.onnx"),"UI fixture, not inference weights");
File.WriteAllText(Path.Combine(data,"rife","rife_v4.26.onnx"),"UI fixture");
File.WriteAllText(Path.Combine(data,"animejanai.conf"),"""
# preserve user comment
[global]
config_version=3
backend=TensorRT
default_slot=1
future_global=keep
[slot_1]
profile_name=Anime · 1440p
hdr_stage_controlled=yes
chain_1_pipeline_version=1
chain_1_stage_count=3
chain_1_stage_1_id=downscale
chain_1_stage_1_kind=resize
chain_1_stage_1_method=area
chain_1_stage_1_runtime=cuda
chain_1_stage_1_enabled=yes
chain_1_stage_1_policy=quality
chain_1_stage_1_target_height=720
chain_1_stage_2_id=rife
chain_1_stage_2_kind=interpolate
chain_1_stage_2_method=rife
chain_1_stage_2_runtime=tensorrt
chain_1_stage_2_enabled=yes
chain_1_stage_2_model=426
chain_1_stage_2_factor_numerator=2
chain_1_stage_2_factor_denominator=1
chain_1_stage_2_policy=quality
chain_1_stage_3_id=upscale
chain_1_stage_3_kind=upscale
chain_1_stage_3_method=onnx
chain_1_stage_3_runtime=tensorrt
chain_1_stage_3_enabled=yes
chain_1_stage_3_policy=quality
chain_1_stage_3_model=SPAN
[slot_5]
profile_name=Film · original cadence
hdr_stage_controlled=yes
chain_1_pipeline_version=1
chain_1_stage_count=0
[slot_20]
profile_name=Future reader profile
future_option=keep
chain_1_pipeline_version=99
chain_1_stage_count=1
chain_1_stage_1_future_option=keep
""");
AppBuilder.Configure<AnimeJaNaiConfEditor.App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions{UseHeadlessDrawing=false}).WithInterFont().UseReactiveUI(_=>{}).SetupWithoutStarting();
var checks=new List<string>();void Check(string name,bool ok){if(!ok)throw new Exception(name);checks.Add(name);Console.WriteLine("PASS "+name);}
var vm=new MainWindowViewModel();vm.NavigateProfiles();
var window=new MainWindow{DataContext=vm,Width=1320,Height=980};window.Show();vm.NavigateProfiles();Dispatcher.UIThread.RunJobs();
Check("only named configured playback profiles are visible",vm.VisibleProfiles.Count==2&&vm.VisibleProfiles.All(p=>p.SlotNumber is "1" or "5"));
Check("profile identity uses the actual slot rather than its list index",vm.AnimeJaNaiConf.UpscaleSlots.Single(p=>p.SlotNumber=="5").MpvProfileName=="upscale-on-5");
foreach(var entry in new[]{("PreparationNavigation",1),("EnginesNavigation",2),("SettingsNavigation",3),("ProfilesNavigation",0)})
{window.FindControl<Button>(entry.Item1)!.Command!.Execute(null);Dispatcher.UIThread.RunJobs();Check(entry.Item1+" reaches stable destination",vm.SelectedTabIndex==entry.Item2);}
vm.ShowOptionalEngines();Check("SDK entry and preparation settings share engines destination",vm.SelectedTabIndex==2);vm.ShowPreparationEngineSettings();Check("preparation helper routes to engines",vm.SelectedTabIndex==2);vm.NavigateProfiles();
Check("activation conditions are secondary and collapsed",window.GetVisualDescendants().OfType<Expander>().Where(e=>Equals(e.Header,"When to use this rule")).All(e=>!e.IsExpanded));
vm.AddProfile();Check("new profile uses a free slot without replacing existing profiles",vm.CurrentSlot.SlotNumber=="2"&&vm.VisibleProfiles.Count==3&&vm.CurrentSlot.Chains.Count==1);
vm.CurrentSlot.ProfileName="My custom chain";vm.DuplicateProfile();Check("duplicate keeps source and creates independently named profile",vm.CurrentSlot.SlotNumber=="3"&&vm.CurrentSlot.ProfileName=="My custom chain copy"&&vm.VisibleProfiles.Count==4);
vm.DeleteCurrentProfile();Check("explicit deletion releases only the selected profile",vm.VisibleProfiles.Count==3&&vm.AnimeJaNaiConf.UpscaleSlots.Single(p=>p.SlotNumber=="2").ProfileName=="My custom chain");
string saved=File.ReadAllText(Path.Combine(data,"animejanai.conf"));Check("unknown and future data survive profile operations",saved.Contains("future_global=keep")&&saved.Contains("future_option=keep")&&saved.Contains("chain_1_stage_1_future_option=keep")&&saved.Contains("# preserve user comment"));
Check("profile edits are persisted and reload with their identity",saved.Contains("profile_name=My custom chain")&&vm.ReadAnimeJaNaiConf(Path.Combine(data,"animejanai.conf")).UpscaleSlots.Single(p=>p.SlotNumber=="2").ProfileName=="My custom chain");
vm.HandleShowCustomProfile("1");Dispatcher.UIThread.RunJobs();
void Capture(string name){Dispatcher.UIThread.RunJobs();using var frame=window.CaptureRenderedFrame();Check(name+" renders",frame is not null);frame!.Save(Path.Combine(output,name+".png"));}
Capture("profiles-wide");
window.Width=760;window.Height=980;Dispatcher.UIThread.RunJobs();
Check("narrow layout uses profile picker and hides duplicate sidebar",window.FindControl<ComboBox>("CompactProfilePicker")!.IsVisible&&!window.FindControl<Border>("ProfileSidebar")!.IsVisible);
Check("narrow navigation shows icons without clipped labels",window.GetVisualDescendants().OfType<TextBlock>().Where(t=>t.Classes.Contains("nav-label")).All(t=>!t.IsVisible));
Capture("profiles-narrow");
vm.ShowEngines();Capture("engines-narrow");vm.HandleShowGlobalSettings();Capture("settings-narrow");vm.ShowPreparation();Capture("preparation-narrow");
window.Width=1320;vm.ShowPreparation();Capture("preparation-wide");
File.WriteAllText(Path.Combine(output,"summary.json"),JsonSerializer.Serialize(new{passed=true,checks,scope="Headless native Avalonia UI; no GPU inference or video benchmark"},new JsonSerializerOptions{WriteIndented=true}));window.Close();
