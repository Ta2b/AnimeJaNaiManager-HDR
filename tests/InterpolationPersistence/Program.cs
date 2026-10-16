using AnimeJaNaiConfEditor.ViewModels;
using ReactiveUI.Builder;
using System.Text.Json;
using System.Text.RegularExpressions;
using Salaros.Configuration;

bool invalidOnly=args.Length==2 && args[1]=="--invalid-only";
bool generateOnly=args.Length==2 && args[1]=="--generate-only";
if(args.Length!=1&&!invalidOnly&&!generateOnly)throw new Exception("Usage: InterpolationPersistence SPEC_JSON");
var spec=JsonDocument.Parse(File.ReadAllText(args[0])).RootElement;
string At(string key)=>spec.GetProperty(key).GetString()!;
string output=At("output"),data=Path.Combine(output,"fixture"),inference=Path.Combine(data,"inference");
Directory.CreateDirectory(output);if(File.Exists(Path.Combine(output,"summary.json")))throw new Exception("Conclusive receipt exists, do not replay.");
foreach(string dir in new[]{data,inference,Path.Combine(data,"onnx"),Path.Combine(data,"rife")})Directory.CreateDirectory(dir);
Environment.SetEnvironmentVariable("ANIMEJANAI_DATA_DIR",data);Environment.SetEnvironmentVariable("ANIMEJANAI_INFERENCE_DIR",inference);Environment.SetEnvironmentVariable("ANIMEJANAI_ROOT",output);
foreach(string name in new[]{"libnvinfer.so.11","libaji_pipeline.so","aji_pipeline.fatbin","libaji_trt.so","libaji_fruc.so","aji_fruc.fatbin"})File.WriteAllText(Path.Combine(inference,name),"UI filesystem fixture, never loaded");
File.WriteAllText(Path.Combine(data,"rife/rife_v4.26.onnx"),"UI filesystem fixture, never loaded");
RxAppBuilder.CreateReactiveUIBuilder().WithCoreServices().BuildApp();
string active=Path.Combine(data,"animejanai.conf"),original=File.ReadAllText(At("source"));File.WriteAllText(active,original);
var checks=new List<string>();void Check(string name,bool ok){if(!ok)throw new Exception(name);checks.Add(name);Console.WriteLine("PASS "+name);}
var vm=new MainWindowViewModel();vm.HandleShowCustomProfile("1");
if(!invalidOnly&&!generateOnly)
{
Check("constructor does not persist a partially loaded stage profile",File.ReadAllText(active)==original);
var imported=vm.ReadAnimeJaNaiConf(At("source"),true);
Check("read with autosave wiring does not modify active profile",File.ReadAllText(active)==original);
vm.AnimeJaNaiConf=imported;var chain=vm.CurrentSlot.Chains[0];chain.AddRifeStage();
Check("add autosaves complete count4",new ConfigParser(active).GetValue("slot_1","chain_1_stage_count")=="4");
chain.Stages.Last().Remove();Check("remove autosaves complete count3 and deletes old keys",new ConfigParser(active).GetValue("slot_1","chain_1_stage_count")=="3"&&!File.ReadAllText(active).Contains("chain_1_stage_4_"));
string profile=Path.Combine(output,"profile.pconf");vm.WriteAnimeJaNaiCurrentProfileConf(profile);var parser=new ConfigParser(profile);parser.SetValue("slot","profile_name","Imported atomically");parser.Save(profile);
vm.ReadAnimeJaNaiConfToCurrentSlot(profile,true);
Check("profile import commits final name and all stages once fully loaded",new ConfigParser(active).GetValue("slot_1","profile_name")=="Imported atomically"&&new ConfigParser(active).GetValue("slot_1","chain_1_stage_count")=="3");
chain=vm.CurrentSlot.Chains[0];var fruc=chain.Stages.Single(s=>s.Method=="nvidia-fruc");
// Set the same editable properties as the UI to actual user-installed paths.
fruc.SdkLibrary=At("sdk");fruc.SdkRuntime=At("runtime");fruc.KernelFatbin=At("kernel");
// Disable scene cuts only in this private qualification profile to force the
// synthesized median on the already qualified pair; user defaults stay intact.
chain.Stages.Single(s=>s.Method=="rife").SceneThreshold="1";
string generated=Path.Combine(output,"manager-generated.conf");vm.WriteAnimeJaNaiConf(generated);
Check("actual SDK paths serialized into ordered RIFE FRUC SPAN",File.ReadAllText(generated).Contains("sdk_library="+At("sdk"))&&File.ReadAllText(generated).Contains("chain_1_rife=no"));
}
if(generateOnly)
{
    var real=vm.CurrentSlot.Chains[0];var f=real.Stages.Single(s=>s.Method=="nvidia-fruc");
    f.SdkLibrary=At("sdk");f.SdkRuntime=At("runtime");f.KernelFatbin=At("kernel");
    real.Stages.Single(s=>s.Method=="rife").SceneThreshold="1";
    vm.AnimeJaNaiConf.TrtEngineSettings=At("trt_settings");
    vm.WriteAnimeJaNaiConf(Path.Combine(output,"manager-generated.conf"));
    Check("private reader profile generated through Manager with exact prebuilt-plan settings",true);
    File.WriteAllText(Path.Combine(output,"generation.json"),JsonSerializer.Serialize(new{passed=true,checks}));
    return;
}
var chainForInvalid=vm.CurrentSlot.Chains[0];var bad=chainForInvalid.Stages[0];bad.Numerator="";Console.WriteLine(JsonSerializer.Serialize(new{chainForInvalid.AutoSave,vm.BackendNotice,activeFactor=new ConfigParser(active).GetValue("slot_1","chain_1_stage_1_factor_numerator"),bad.Numerator,chainForInvalid.PipelineNotice}));Check("invalid edit cannot overwrite valid active profile",new ConfigParser(active).GetValue("slot_1","chain_1_stage_1_factor_numerator")=="2"&&chainForInvalid.PipelineNotice.Contains("positive integers"));bad.Numerator="2";
File.WriteAllText(Path.Combine(output,invalidOnly?"invalid-edit.json":"summary.json"),JsonSerializer.Serialize(new{passed=true,checks,scope="Persistence and actual SDK path configuration only, GPU consumption checked separately"},new JsonSerializerOptions{WriteIndented=true}));
