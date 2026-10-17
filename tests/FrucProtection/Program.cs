using AnimeJaNaiConfEditor.ViewModels;
using AnimeJaNaiConfEditor.Views;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ReactiveUI.Avalonia;
using System.Text.Json;
string output=Path.GetFullPath(args[0]);Directory.CreateDirectory(output);if(File.Exists(Path.Combine(output,"summary.json")))throw new Exception("Conclusive receipt exists; no replay.");
string data=Path.Combine(output,"data"),inf=Path.Combine(data,"inference");foreach(var dir in new[]{data,inf,Path.Combine(data,"rife"),Path.Combine(data,"onnx")})Directory.CreateDirectory(dir);
Environment.SetEnvironmentVariable("ANIMEJANAI_DATA_DIR",data);Environment.SetEnvironmentVariable("ANIMEJANAI_INFERENCE_DIR",inf);Environment.SetEnvironmentVariable("ANIMEJANAI_ROOT",output);
File.Copy(Path.GetFullPath(args[1]),Path.Combine(inf,"libaji_fruc.so"),true);File.Copy(Path.GetFullPath(args[2]),Path.Combine(inf,"libaji_pipeline.so"),true);File.WriteAllText(Path.Combine(inf,"aji_fruc.fatbin"),"UI fixture only");foreach(var name in new[]{"libaji_trt.so","aji_pipeline.fatbin"})File.WriteAllText(Path.Combine(inf,name),"UI fixture only");
string config="[global]\nconfig_version=3\nbackend=TensorRT\ndefault_slot=1\n[slot_1]\nprofile_name=FRUC\nchain_1_pipeline_version=1\nchain_1_stage_count=1\nchain_1_stage_1_id=fruc\nchain_1_stage_1_kind=interpolate\nchain_1_stage_1_method=nvidia-fruc\nchain_1_stage_1_runtime=cuda\nchain_1_stage_1_policy=fast\nchain_1_stage_1_enabled=no\nchain_1_stage_1_factor_numerator=2\nchain_1_stage_1_sdk_library="+Path.GetFullPath(args[3])+"\nchain_1_stage_1_sdk_runtime="+Path.GetFullPath(args[4])+"\nchain_1_stage_1_kernel_fatbin="+Path.Combine(inf,"aji_fruc.fatbin")+"\n";File.WriteAllText(Path.Combine(data,"animejanai.conf"),config);
AppBuilder.Configure<AnimeJaNaiConfEditor.App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions{UseHeadlessDrawing=false}).WithInterFont().UseReactiveUI(_=>{}).SetupWithoutStarting();var checks=new List<string>();void Check(string name,bool ok){if(!ok)throw new Exception(name);checks.Add(name);Console.WriteLine("PASS "+name);}
var vm=new MainWindowViewModel();vm.HandleShowCustomProfile("1");vm.AnimeJaNaiConf.SetTensorRtSelected();var chain=vm.CurrentSlot.Chains[0];var stage=chain.Stages.Single();var view=new InterpolationStages{DataContext=chain};var window=new Window{Content=view,Width=1100,Height=850};window.Show();Dispatcher.UIThread.RunJobs();var control=view.GetVisualDescendants().OfType<CheckBox>().Single(c=>c.Name=="FrucArtifactProtection");
Console.WriteLine(JsonSerializer.Serialize(new{control.IsEnabled,control.IsChecked,stage.FrucArtifactProtection,stage.FrucProtectionAvailable,stage.CanEnable,stage.Availability,stage.Kind,stage.Method,stage.Runtime}));
Check("new capable reader offers one protection control enabled by default",control.IsEnabled&&control.IsChecked==true&&stage.FrucArtifactProtection&&stage.CanEnable);
Check("protection states its original-repeat and fluidity compromise",stage.FrucProtectionNotice.Contains("previous image")&&stage.FrucProtectionNotice.Contains("fluidity"));
control.IsChecked=false;stage.Enabled=true;Dispatcher.UIThread.RunJobs();string saved=Path.Combine(output,"saved.conf");vm.WriteAnimeJaNaiConf(saved);var loaded=vm.ReadAnimeJaNaiConf(saved);Check("explicit off preserves standard FRUC and survives save reload",!loaded.UpscaleSlots[0].Chains[0].Stages.Single().FrucArtifactProtection);
control.IsChecked=true;vm.WriteAnimeJaNaiConf(saved);loaded=vm.ReadAnimeJaNaiConf(saved);Check("protection on remains FRUC Fast x2 with same stage identity",loaded.UpscaleSlots[0].Chains[0].Stages.Single() is {} s&&s.FrucArtifactProtection&&s.Id==stage.Id&&s.Policy=="fast"&&s.Numerator=="2");
using(var frame=window.CaptureRenderedFrame()){if(frame==null)throw new Exception("No frame");frame.Save(Path.Combine(output,"fruc-protection.png"));}
File.Copy(Path.GetFullPath(args[5]),Path.Combine(inf,"libaji_fruc.so"),true);stage.Enabled=false;chain.RefreshPipeline();Check("older provider cannot silently enable unsupported protection",!stage.FrucProtectionAvailable&&!stage.CanEnable&&stage.Availability.Contains("Update the player")&&control.IsEnabled);control.IsChecked=false;Check("standard FRUC is still explicitly available on the older provider",stage.CanEnable);
File.WriteAllText(Path.Combine(output,"summary.json"),JsonSerializer.Serialize(new{passed=true,checks,scope="New FRUC guard option/capability/persistence only; no old UI suite or GPU pixels replayed"},new JsonSerializerOptions{WriteIndented=true}));window.Close();
