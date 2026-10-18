using AnimeJaNaiConfEditor.ViewModels;
using Avalonia;
using Avalonia.Headless;
using ReactiveUI.Avalonia;
using System.Text.Json;
// Contract v2: a file from the "conversion in profiles" era (HDR step inside a rule, global
// conversion retired) goes back to one global HDR setting. No GPU, video or UI test.
string root=Path.GetFullPath(args[0]);Directory.CreateDirectory(root);if(File.Exists(Path.Combine(root,"summary.json")))throw new Exception("Conclusive receipt exists; no replay.");
string data=Path.Combine(root,"data"),opts=Path.Combine(root,"portable_config","script-opts");Directory.CreateDirectory(data);Directory.CreateDirectory(opts);
Environment.SetEnvironmentVariable("ANIMEJANAI_DATA_DIR",data);Environment.SetEnvironmentVariable("ANIMEJANAI_ROOT",root);Environment.SetEnvironmentVariable("ANIMEJANAI_INFERENCE_DIR",Path.Combine(data,"inference"));
string conf=Path.Combine(data,"animejanai.conf"),hdr=Path.Combine(opts,"animejanai_hdr_auto.conf");
string bank="[global]\nconfig_version=3\nbackend=TensorRT\ndefault_slot=1\n[slot_1]\nprofile_name=Mine\nhdr_stage_controlled=yes\nchain_1_pipeline_version=1\nchain_1_stage_count=2\n"+
    "chain_1_stage_1_id=hdr\nchain_1_stage_1_kind=hdr\nchain_1_stage_1_method=mpv\nchain_1_stage_1_runtime=gpu-next\nchain_1_stage_1_enabled=yes\nchain_1_stage_1_contrast=130\nchain_1_stage_1_saturation=95\n"+
    "chain_1_stage_2_id=up\nchain_1_stage_2_kind=upscale\nchain_1_stage_2_method=onnx\nchain_1_stage_2_runtime=tensorrt\nchain_1_stage_2_enabled=yes\nchain_1_stage_2_model=SPAN\n"+
    "[slot_2]\nprofile_name=Second\nchain_1_pipeline_version=1\nchain_1_stage_count=1\nchain_1_stage_1_id=hdr2\nchain_1_stage_1_kind=hdr\nchain_1_stage_1_method=nvidia-truehdr\nchain_1_stage_1_runtime=cuda\nchain_1_stage_1_enabled=no\n";
File.WriteAllText(conf,bank);
string original="# user note\nfuture_custom=keep\ndisplay_peak=1200\nconversion_in_profiles=yes\nrtx_contrast=100\nrtx_saturation=100\nrtx_middle_gray=50\npaper_white=210\n";File.WriteAllText(hdr,original);
AppBuilder.Configure<AnimeJaNaiConfEditor.App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions()).WithInterFont().UseReactiveUI(_=>{}).SetupWithoutStarting();
var vm=new MainWindowViewModel();var checks=new List<string>();void Check(string name,bool ok){if(!ok)throw new Exception(name);checks.Add(name);Console.WriteLine("PASS "+name);}
var mine=vm.AnimeJaNaiConf.UpscaleSlots.Single(s=>s.SlotNumber=="1").Chains.Single();var second=vm.AnimeJaNaiConf.UpscaleSlots.Single(s=>s.SlotNumber=="2").Chains.Single();
Check("HDR steps leave the rules, active or not",mine.Stages.Select(s=>s.Kind).SequenceEqual(new[]{"upscale"})&&second.Stages.Count==0);
Check("the first active HDR step becomes the global choice (renderer conversion)",vm.HdrSettings.HdrConversion&&vm.HdrSettings.ModeMpv);
Check("the user is told once, including that its own look was not copied",vm.HasProfileMigrationNotice&&vm.ProfileMigrationNotice.Contains("Settings → HDR conversion")&&vm.ProfileMigrationNotice.Contains("contrast 130"));
Check("opening alone writes nothing",File.ReadAllText(conf)==bank&&File.ReadAllText(hdr)==original);
vm.WriteAnimeJaNaiConf();vm.HdrSettings.Save();string saved=File.ReadAllText(conf),settings=File.ReadAllText(hdr);
Check("the saved profiles hold no HDR step, marker or retired key",!saved.Contains("kind=hdr")&&!saved.Contains("hdr_stage_controlled")&&saved.Contains("config_version=4"));
Check("the global settings carry the conversion and drop the retired marker",settings.Contains("hdr_conversion=yes")&&settings.Contains("hdr_backend=mpv")&&!settings.Contains("conversion_in_profiles"));
Check("screen values, comments and unknown settings survive",settings.Contains("display_peak=1200")&&settings.Contains("# user note")&&settings.Contains("future_custom=keep")&&settings.Contains("paper_white=210"));
var reopened=new MainWindowViewModel();
Check("reopening finds nothing to migrate and says nothing",!reopened.HasProfileMigrationNotice&&reopened.HdrSettings.HdrConversion&&reopened.AnimeJaNaiConf.UpscaleSlots.SelectMany(s=>s.Chains).SelectMany(c=>c.Stages).All(s=>s.Kind!="hdr"));
File.WriteAllText(Path.Combine(root,"summary.json"),JsonSerializer.Serialize(new{passed=true,checks,scope="Contract v2 return of in-profile HDR to the global setting; files only"},new JsonSerializerOptions{WriteIndented=true}));
