using AnimeJaNaiConfEditor.ViewModels;
using AnimeJaNaiConfEditor.Views;
using AnimeJaNai.Platform;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ReactiveUI.Avalonia;
using Salaros.Configuration;
using System.Text.Json;
// Historical profile (legacy keys, old built-in default) opened by the contract v2 Manager.
string output=Path.GetFullPath(args[0]);Directory.CreateDirectory(output);if(File.Exists(Path.Combine(output,"summary.json")))throw new Exception("Conclusive receipt exists; no replay.");
string data=Path.Combine(output,"data"),inf=Path.Combine(data,"inference"),opts=Path.Combine(output,"portable_config","script-opts");
foreach(string d in new[]{data,inf,opts,Path.Combine(data,"onnx"),Path.Combine(data,"rife")})Directory.CreateDirectory(d);
Environment.SetEnvironmentVariable("ANIMEJANAI_DATA_DIR",data);Environment.SetEnvironmentVariable("ANIMEJANAI_INFERENCE_DIR",inf);Environment.SetEnvironmentVariable("ANIMEJANAI_ROOT",output);
File.WriteAllText(Path.Combine(data,"onnx","SPAN.onnx"),"UI weights fixture");File.WriteAllText(Path.Combine(data,"rife","rife_v4.26.onnx"),"UI weights fixture");
string hdrOriginal="hdr_conversion=yes\nhdr_backend=mpv\nrtx_contrast=120\nrtx_saturation=90\nrtx_middle_gray=55\nmpv_truehdr_look=no\npaper_white=210\nhighlights=0.45\nvibrance=0.12\n";
File.WriteAllText(Path.Combine(opts,"animejanai_hdr_auto.conf"),hdrOriginal);
string original="# preserve comment\n[global]\nbackend=TensorRT\nrife_engine=quality\ndefault_slot=1002\nquality_preset=sharp\nfuture_global=preserve\n[slot_1]\nprofile_name=My custom profile\nchain_1_rife=yes\nchain_1_rife_model=426\nchain_1_rife_factor_numerator=5\nchain_1_rife_factor_denominator=2\nchain_1_rife_scene_detect_threshold=0.125\nchain_1_model_1_name=SPAN\nchain_1_model_1_resize_height_before_upscale=720\nchain_1_model_1_resize_factor_before_upscale=100\n";
File.WriteAllText(Path.Combine(data,"animejanai.conf"),original);
AppBuilder.Configure<AnimeJaNaiConfEditor.App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions{UseHeadlessDrawing=false}).WithInterFont().UseReactiveUI(_=>{}).SetupWithoutStarting();
var checks=new List<string>();void Check(string name,bool ok){if(!ok)throw new Exception(name);checks.Add(name);Console.WriteLine("PASS "+name);}
var vm=new MainWindowViewModel();vm.HandleShowCustomProfile("1");var chain=vm.CurrentSlot.Chains.Single();
Check("removed built-in default becomes Off with explanation and custom profile intact",vm.AnimeJaNaiConf.DefaultSlot==0&&vm.HasProfileMigrationNotice&&vm.CurrentSlot.ProfileName=="My custom profile");
Check("legacy resize is a separate exact-size step preserving the interpolation order and arguments",chain.Stages.Select(s=>s.Kind).SequenceEqual(new[]{"resize","interpolate","upscale"})&&chain.Stages[0].Method=="spline36"&&chain.Stages[0].IsLegacySizing&&chain.Stages[0].TargetHeight=="720"&&chain.Stages[1].Numerator=="5"&&chain.Stages[1].Denominator=="2"&&!chain.Stages[2].OptionsText.Contains("resize_"));
Check("HDR stays a global setting: the renderer conversion and its look are untouched",vm.HdrSettings.HdrConversion&&vm.HdrSettings.ModeMpv&&!chain.Stages.Any(s=>s.Kind=="hdr"));
Check("opening does not overwrite the source configuration",File.ReadAllText(Path.Combine(data,"animejanai.conf"))==original&&File.ReadAllText(Path.Combine(opts,"animejanai_hdr_auto.conf"))==hdrOriginal);
var view=new InterpolationStages{DataContext=chain};var window=new Window{Content=view,Width=1100,Height=1450};window.Show();Dispatcher.UIThread.RunJobs();
Check("one category menu and no old editor toggle",view.FindControl<Button>("AddProcessingStep")!=null&&!view.GetVisualDescendants().OfType<CheckBox>().Any(c=>c.Name=="OrderedStagesToggle"));
using(var frame=window.CaptureRenderedFrame()){if(frame==null)throw new Exception("No UI frame");frame.Save(Path.Combine(output,"imported-steps.png"));}
string saved=Path.Combine(output,"saved.conf");vm.WriteAnimeJaNaiConf(saved);var text=File.ReadAllText(saved);var l=vm.ReadAnimeJaNaiConf(saved).UpscaleSlots[0].Chains[0];
Check("canonical save keeps stage ids and regenerates the legacy keys the Windows engine reads",l.Stages.Select(s=>s.Id).SequenceEqual(chain.Stages.Select(s=>s.Id))&&!text.Contains("quality_preset")&&text.Contains("config_version=4")&&text.Contains("chain_1_model_1_resize_height_before_upscale=720")&&text.Contains("chain_1_rife_factor_numerator=5")&&text.Contains("chain_1_rife=yes")&&!text.Contains("kind=hdr"));
var future=vm.ReadAnimeJaNaiConf(new ConfigParser("[global]\nbackend=TensorRT\n[slot_1]\nprofile_name=Future\nchain_1_pipeline_version=99\nchain_1_stage_count=1\nchain_1_stage_1_future_option=keep\n"));
Check("future schema remains read-only and its raw stage data is preserved",!future.UpscaleSlots[0].Chains[0].PipelineEditable&&MainWindowViewModel.ParsedAnimeJaNaiConf(future)!.ToString().Contains("chain_1_stage_1_future_option=keep"));
var parsed=new ConfigParser(saved);var expected=InterpolationPipeline.InferencePlan(InterpolationPipeline.Read(parsed.Sections.First(s=>s.SectionName=="slot_1").Keys.ToDictionary(k=>k.Name,k=>parsed.GetValue("slot_1",k.Name)),1,"tensorrt","quality"));
Check("single execution bridge retains native Spline36 source calculation",expected.Stages.Count==2&&expected.Stages[1].Options["resize_height_before_upscale"]=="720"&&expected.Stages[0].Factor==new InterpolationPipeline.Rational(5,2));
var main=new MainWindow{DataContext=vm,Width=1400,Height=1050};main.Show();vm.ReturnToProfiles();vm.HandleShowCustomProfile("1");Dispatcher.UIThread.RunJobs();
Check("main profile view contains no built-in profile list or legacy model editor",!main.GetVisualDescendants().OfType<TextBlock>().Any(t=>t.Text?.Contains("Default Profiles (Read-only)")==true)&&!main.GetVisualDescendants().OfType<CheckBox>().Any(c=>c.Content?.ToString()=="Enable RIFE Interpolation"));
using(var frame=main.CaptureRenderedFrame()){if(frame==null)throw new Exception("No main UI frame");frame.Save(Path.Combine(output,"manager-profiles.png"));}
vm.HandleShowGlobalSettings();Dispatcher.UIThread.RunJobs();
var modes=main.GetVisualDescendants().OfType<ToggleButton>().Where(b=>b.Name is "HdrModeOff" or "HdrModeMpv" or "HdrModeRtx").ToArray();
Check("Settings holds the one HDR conversion choice: Off, Renderer, NVIDIA TrueHDR (greyed with its cause when unavailable)",modes.Length==3&&modes.Single(b=>b.Name=="HdrModeMpv").IsChecked==true&&modes.Single(b=>b.Name=="HdrModeRtx").IsEnabled==vm.TrueHdrSelectable&&main.FindControl<TextBlock>("TrueHdrAvailability")!.Text==vm.OptionalSdks.TrueHdrStatus);
modes.Single(b=>b.Name=="HdrModeOff").IsChecked=true;Dispatcher.UIThread.RunJobs();vm.HdrSettings.Save();
Check("turning the conversion off is saved globally, the look values stay",File.ReadAllText(Path.Combine(opts,"animejanai_hdr_auto.conf")).Contains("hdr_conversion=no")&&File.ReadAllText(Path.Combine(opts,"animejanai_hdr_auto.conf")).Contains("rtx_contrast=120"));
using(var frame=main.CaptureRenderedFrame()){if(frame==null)throw new Exception("No settings frame");frame.Save(Path.Combine(output,"settings-hdr.png"));}
File.WriteAllText(Path.Combine(output,"summary.json"),JsonSerializer.Serialize(new{passed=true,checks,scope="Contract v2 opening of a historical profile, global HDR choice; no engine or video test"},new JsonSerializerOptions{WriteIndented=true}));main.Close();window.Close();
