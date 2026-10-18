using AnimeJaNaiConfEditor.ViewModels;
using AnimeJaNaiConfEditor.Views;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ReactiveUI.Avalonia;
using System.Text.Json;

// Navigation, profile identity, preservation, recommended profiles, engine change and layout.
// Fixture files only: no GPU inference or video benchmark.
string output=Path.GetFullPath(args[0]);Directory.CreateDirectory(output);
string data=Path.Combine(output,"data");Directory.CreateDirectory(Path.Combine(data,"onnx"));Directory.CreateDirectory(Path.Combine(data,"rife"));
Directory.CreateDirectory(Path.Combine(output,"portable_config"));
Environment.SetEnvironmentVariable("ANIMEJANAI_DATA_DIR",data);Environment.SetEnvironmentVariable("ANIMEJANAI_ROOT",output);
Environment.SetEnvironmentVariable("ANIMEJANAI_INFERENCE_DIR",Path.Combine(data,"inference"));
File.WriteAllText(Path.Combine(output,"portable_config","mpv.conf"),"# fixture\n");
foreach(var m in new[]{"2x_AnimeJaNai_HD_V3.1Sharp1_Balanced_SPANF3_b8f64_unshuffle_fp16","2x_AnimeJaNai_HD_V3.1Sharp1_Performance_SPANF3_b5f48_unshuffle_fp16"})
    File.WriteAllText(Path.Combine(data,"onnx",m+".onnx"),"UI fixture, not inference weights");
File.WriteAllText(Path.Combine(data,"rife","rife_v4.26.onnx"),"UI fixture");
const string sharpPerformance="2x_AnimeJaNai_HD_V3.1Sharp1_Performance_SPANF3_b5f48_unshuffle_fp16";
File.WriteAllText(Path.Combine(data,"animejanai.conf"),$"""
# preserve user comment
[global]
config_version=3
backend=TensorRT
default_slot=1
future_global=keep
[slot_1]
profile_name=Anime · 1440p
hdr_stage_controlled=yes
chain__min_resolution=0x0
chain__rife=no
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
chain_1_stage_3_model={sharpPerformance}
chain_1_rife=yes
chain_1_model_1_name=stale-mirror
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
vm.HandleShowCustomProfile("1");Dispatcher.UIThread.RunJobs();
Check("activation conditions are secondary and collapsed",window.GetVisualDescendants().OfType<Expander>().Where(e=>Equals(e.Header,"When this rule applies")).All(e=>!e.IsExpanded));
Check("the profile shows what it produces",vm.CurrentSlot.HasRules&&vm.CurrentSlot.ResultSummary.Length>0&&window.FindControl<TextBlock>("ProfileResultSummary")!.IsEffectivelyVisible);
Console.WriteLine("  result: "+vm.CurrentSlot.ResultSummary+" | "+vm.CurrentSlot.ResultWarning);
vm.AddProfile();Check("new profile uses a free slot without replacing existing profiles",vm.CurrentSlot.SlotNumber=="2"&&vm.VisibleProfiles.Count==3&&vm.CurrentSlot.Chains.Count==1&&vm.CurrentSlot.Chains[0].ChainNumber=="1");
vm.CurrentSlot.ProfileName="My custom chain";vm.DuplicateProfile();Check("duplicate keeps source and creates independently named profile",vm.CurrentSlot.SlotNumber=="3"&&vm.CurrentSlot.ProfileName=="My custom chain copy"&&vm.VisibleProfiles.Count==4);
vm.DeleteCurrentProfile();Check("explicit deletion releases only the selected profile",vm.VisibleProfiles.Count==3&&vm.AnimeJaNaiConf.UpscaleSlots.Single(p=>p.SlotNumber=="2").ProfileName=="My custom chain");
vm.HandleShowCustomProfile("2");vm.AddChain();Check("an added rule is numbered before it is saved",vm.CurrentSlot.Chains.Select(c=>c.ChainNumber).SequenceEqual(new[]{"1","2"}));
string saved=File.ReadAllText(Path.Combine(data,"animejanai.conf"));
Check("unknown and future data survive profile operations",saved.Contains("future_global=keep")&&saved.Contains("future_option=keep")&&saved.Contains("chain_1_stage_1_future_option=keep")&&saved.Contains("# preserve user comment"));
Check("profile edits are persisted and reload with their identity",saved.Contains("profile_name=My custom chain")&&vm.ReadAnimeJaNaiConf(Path.Combine(data,"animejanai.conf")).UpscaleSlots.Single(p=>p.SlotNumber=="2").ProfileName=="My custom chain");
Check("format 4 without phantom rules, profile HDR markers or stale legacy keys",saved.Contains("config_version=4")&&!saved.Contains("chain__")&&!saved.Contains("hdr_stage_controlled")&&!saved.Contains("stale-mirror"));
string Section(string text,string name){int i=text.IndexOf("["+name+"]",StringComparison.Ordinal);int j=text.IndexOf("\n[",i+1,StringComparison.Ordinal);return j<0?text[i..]:text[i..j];}
Check("an ordered rule gets rife=no and no legacy model; its stages stay",Section(saved,"slot_1").Contains("chain_1_rife=no")&&!Section(saved,"slot_1").Contains("chain_1_model_1_name")&&Section(saved,"slot_1").Contains("chain_1_stage_1_method=area"));
// Recommended profiles: the starter bank definitions, in the free slots.
vm.AddRecommendedProfiles();Dispatcher.UIThread.RunJobs();
string[] starters=["Balanced · 1080p → 2160p","Performance · 1080p → 2160p + RIFE ×2","Performance · 720p → 1440p","Performance · 720p → 1440p + RIFE ×2","RIFE ×2 · source resolution"];
Check("recommended profiles are added once, in free slots",starters.All(n=>vm.VisibleProfiles.Count(p=>p.ProfileName==n)==1)&&vm.VisibleProfiles.Count==8&&vm.HasProfileMigrationNotice);
var rife2=vm.AnimeJaNaiConf.UpscaleSlots.Single(p=>p.ProfileName==starters[1]);
Check("RIFE starter: two rules, RIFE ×2 Quality before the upscale up to 31 fps",rife2.Chains.Count==2&&rife2.Chains[0].Stages.Select(s=>s.Kind).SequenceEqual(new[]{"interpolate","upscale"})&&rife2.Chains[0].Stages[0].Policy=="quality"&&rife2.Chains[0].Stages[0].Model=="426"&&rife2.Chains[0].MaxFps=="31"&&rife2.Chains[1].Stages.Single().Kind=="upscale");
var p720=vm.AnimeJaNaiConf.UpscaleSlots.Single(p=>p.ProfileName==starters[2]).Chains[0].Stages;
Check("720p starter: exact 720p Spline36 resize then the Performance model",p720[0].IsResize&&p720[0].IsLegacySizing&&p720[0].Method=="spline36"&&p720[0].TargetHeight=="720"&&p720[1].Model==sharpPerformance);
vm.AddRecommendedProfiles();Check("adding them again changes nothing and says so",vm.VisibleProfiles.Count==8&&vm.ProfileMigrationNotice.Contains("already"));
// Engine change: the steps that ran on TensorRT follow Vulkan, and come back.
(string,string,string,string)[] Tuples()=>vm.AnimeJaNaiConf.UpscaleSlots.SelectMany(p=>p.Chains).SelectMany(c=>c.Stages).Select(s=>(s.Id,s.Method,s.Runtime,s.Model)).ToArray();
var before=Tuples();
vm.AnimeJaNaiConf.UserSelectVulkan();Dispatcher.UIThread.RunJobs();
var vulkanStages=vm.AnimeJaNaiConf.UpscaleSlots.SelectMany(p=>p.Chains).SelectMany(c=>c.Stages).ToArray();
Check("selecting Vulkan moves the steps to Vulkan (RIFE 4.6, Bilinear) and says what changed",vulkanStages.Where(s=>!s.IsFruc).All(s=>s.Runtime=="vulkan")&&vulkanStages.Where(s=>s.IsRife).All(s=>s.Model=="46")&&vulkanStages.Where(s=>s.IsResize).All(s=>s.Method=="bilinear")&&vm.EngineChangeNotice.Contains("Vulkan")&&vm.EngineChangeNotice.Contains("RIFE"));
vm.AnimeJaNaiConf.UserSelectTensorRt();Dispatcher.UIThread.RunJobs();
Check("selecting TensorRT again restores every step (Area downscale, RIFE 4.26, Spline36 exact sizes)",Tuples().SequenceEqual(before));
string engineSaved=File.ReadAllText(Path.Combine(data,"animejanai.conf"));
Check("the engine change is saved with the steps",engineSaved.Contains("backend=TensorRT")&&!engineSaved.Contains("runtime=vulkan"));
vm.HandleShowCustomProfile("1");Dispatcher.UIThread.RunJobs();
void Capture(string name){Dispatcher.UIThread.RunJobs();using var frame=window.CaptureRenderedFrame();Check(name+" renders",frame is not null);frame!.Save(Path.Combine(output,name+".png"));}
Capture("profiles-wide");
vm.HandleShowCustomProfile(rife2.SlotNumber);Capture("profiles-rife-wide");
vm.ShowEngines();Capture("engines-wide");vm.HandleShowGlobalSettings();Capture("settings-wide");vm.NavigateProfiles();
window.Width=760;window.Height=980;Dispatcher.UIThread.RunJobs();
Check("narrow layout uses profile picker and hides duplicate sidebar",window.FindControl<ComboBox>("CompactProfilePicker")!.IsVisible&&!window.FindControl<Border>("ProfileSidebar")!.IsVisible);
Check("narrow navigation shows icons without clipped labels",window.GetVisualDescendants().OfType<TextBlock>().Where(t=>t.Classes.Contains("nav-label")).All(t=>!t.IsVisible));
Capture("profiles-narrow");
vm.ShowEngines();Capture("engines-narrow");vm.HandleShowGlobalSettings();Capture("settings-narrow");vm.ShowPreparation();Capture("preparation-narrow");
window.Width=1320;vm.ShowPreparation();Capture("preparation-wide");
File.WriteAllText(Path.Combine(output,"summary.json"),JsonSerializer.Serialize(new{passed=true,checks,scope="Headless native Avalonia UI; no GPU inference or video benchmark"},new JsonSerializerOptions{WriteIndented=true}));window.Close();
