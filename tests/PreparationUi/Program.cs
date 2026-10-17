using AnimeJaNaiConfEditor.ViewModels;
using AnimeJaNaiConfEditor.Views;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ReactiveUI.Avalonia;
using System.Diagnostics;
using System.Text.Json;

string output=Path.GetFullPath(args[0]);Directory.CreateDirectory(output);
if(File.Exists(Path.Combine(output,"summary.json")))throw new Exception("Conclusive receipt exists; no replay without a changed concern.");
string data=Path.Combine(output,"data"),workerDir=Path.Combine(output,"animejanai","preparation");
foreach(var d in new[]{data,workerDir,Path.Combine(output,"portable_config","script-opts"),Path.Combine(output,"models"),Path.Combine(output,"weights")})Directory.CreateDirectory(d);
Environment.SetEnvironmentVariable("ANIMEJANAI_ROOT",output);Environment.SetEnvironmentVariable("ANIMEJANAI_DATA_DIR",data);Environment.SetEnvironmentVariable("ANIMEJANAI_INFERENCE_DIR",Path.Combine(data,"inference"));
File.WriteAllText(Path.Combine(data,"animejanai.conf"),"[global]\nbackend=TensorRT\ndefault_slot=0\n");
File.WriteAllText(Path.Combine(data,"preparation-settings.json"),"{\"version\":1,\"future_option\":{\"preserve\":[1,\"original\"]},\"python\":\"/usr/bin/python3\"}");
File.WriteAllText(Path.Combine(output,"source.mp4"),"original source fixture");
File.WriteAllText(Path.Combine(workerDir,"worker.py"),"""
import json, os, pathlib, subprocess, sys, time
spec=pathlib.Path(sys.argv[1]);job=json.loads(spec.read_text());dest=pathlib.Path(job['destination']);jobid=sys.argv[sys.argv.index('--job-id')+1]
def emit(state, **kw):print(json.dumps(dict(protocol=1,state=state,**kw)),flush=True)
if dest.exists():emit('failed',message='Destination exists; choose a new output');sys.exit(1)
if '--inspect' in sys.argv:emit('inspected');sys.exit(0)
if 'cancel' in dest.name:
    stage=dest.parent/('.animejanai-prepare-'+jobid+'-fixture');stage.mkdir();(stage/'job-owner').write_text(jobid);(stage/('video'+dest.suffix)).write_bytes(b'partial fixture')
    child=subprocess.Popen([sys.executable,'-c','import time; time.sleep(60)'])
    (spec.parent/'owned-processes.json').write_text(json.dumps(dict(worker=os.getpid(),child=child.pid,staging=str(stage))))
    emit('staging',staging=str(stage));emit('running',source_completed=1,source_total=4,output_frames=2)
    # Deliberately ignores cooperative cancellation to exercise the Manager's
    # bounded force-stop of its owned tree; no external process is touched.
    while True:time.sleep(.1)
emit('running',source_completed=2,source_total=2,output_frames=4)
dest.write_bytes(b'completed fake export');emit('complete',destination=str(dest.resolve()))
""");
AppBuilder.Configure<AnimeJaNaiConfEditor.App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions{UseHeadlessDrawing=false}).WithInterFont().UseReactiveUI(_=>{}).SetupWithoutStarting();
var checks=new List<string>();void Check(string name,bool ok){if(!ok)throw new Exception(name);checks.Add(name);Console.WriteLine("PASS "+name);}
void PumpUntil(Func<bool> done,int milliseconds=8000){var timer=Stopwatch.StartNew();while(!done()){Dispatcher.UIThread.RunJobs();if(timer.ElapsedMilliseconds>milliseconds)throw new TimeoutException("Headless operation did not finish");Thread.Sleep(2);}Dispatcher.UIThread.RunJobs();}
void Finish(Task task){PumpUntil(()=>task.IsCompleted);task.GetAwaiter().GetResult();}
var vm=new MainWindowViewModel();var prep=vm.Preparation;prep.Source=Path.Combine(output,"source.mp4");prep.Destination=Path.Combine(output,"prepared.mkv");prep.ModelSource=Path.Combine(output,"models");prep.Weights=Path.Combine(output,"weights");
Check("unknown preparation settings survive edits",JsonDocument.Parse(File.ReadAllText(Path.Combine(data,"preparation-settings.json"))).RootElement.GetProperty("future_option").GetProperty("preserve")[1].GetString()=="original");
var main=new MainWindow{DataContext=vm,Width=1250,Height=1250};main.Show();vm.ShowPreparation();Dispatcher.UIThread.RunJobs();
Check("preparation exposes output choices and keeps engine paths in Engines",main.GetVisualDescendants().OfType<TextBox>().Any(c=>c.Name=="PreparationSource"&&c.IsEffectivelyVisible)&&!main.GetVisualDescendants().OfType<TextBox>().Any(c=>c.Name=="PreparationPython"&&c.IsEffectivelyVisible));
Finish(prep.InspectAsync());Check("inspection returns idle without advertising a completed export",prep.NotBusy&&!prep.CanOpen&&prep.CanStart&&prep.Status.StartsWith("Pinned files and source timing checked"));
Finish(prep.PrepareAsync());Check("first job completes and exposes its output",prep.NotBusy&&prep.CanOpen&&prep.Progress==100&&File.ReadAllText(prep.Destination)=="completed fake export");
using(var frame=main.CaptureRenderedFrame()){if(frame==null)throw new Exception("No preparation frame");frame.Save(Path.Combine(output,"preparation-complete.png"));}
vm.ShowEngines();Dispatcher.UIThread.RunJobs();
Check("GMFSS setup lives in Engines and there are no FRUC controls",main.GetVisualDescendants().OfType<TextBox>().Any(c=>c.Name=="PreparationPython"&&c.IsEffectivelyVisible)&&!main.GetVisualDescendants().OfType<Control>().Any(c=>c.Name?.Contains("Fruc",StringComparison.OrdinalIgnoreCase)==true));
using(var frame=main.CaptureRenderedFrame()){if(frame==null)throw new Exception("No engines frame");frame.Save(Path.Combine(output,"engines-preparation-setup.png"));}
// The same model and views must keep codec/audio through navigation.
vm.ShowPreparation();Dispatcher.UIThread.RunJobs();Check("view navigation preserves output codec and audio policy",prep.OutputCodec=="lossless-rgb"&&prep.Audio=="copy");
prep.Destination=Path.Combine(output,"cancelled.mkv");var second=prep.PrepareAsync();
PumpUntil(()=>prep.Status.StartsWith("GMFSS: 1/4"));
Check("second job resets completion and enables cancellation",prep.IsBusy&&prep.CanCancel&&!prep.CanOpen&&main.GetVisualDescendants().OfType<Button>().Any(b=>b.Name=="PreparationCancel"&&b.IsEffectivelyVisible&&b.IsEnabled));
var ownerFile=Directory.GetFiles(Path.Combine(data,"preparation-jobs"),"owned-processes.json",SearchOption.AllDirectories).Single();var owner=JsonDocument.Parse(File.ReadAllText(ownerFile)).RootElement;int pid=owner.GetProperty("worker").GetInt32(),childPid=owner.GetProperty("child").GetInt32();string staging=owner.GetProperty("staging").GetString()!;
main.Close();PumpUntil(()=>second.IsCompleted&&!main.IsVisible);second.GetAwaiter().GetResult();
bool Alive(int id){try{using var process=Process.GetProcessById(id);return !process.HasExited;}catch(ArgumentException){return false;}}
Check("closing waits for cancellation of owned worker and child",!prep.IsBusy&&!Alive(pid)&&!Alive(childPid)&&prep.Status.StartsWith("Preparation cancelled"));
Check("cancel removes only owned staging and never publishes a partial output",!Directory.Exists(staging)&&!File.Exists(prep.Destination)&&File.ReadAllText(prep.Source)=="original source fixture");
prep.Destination=Path.Combine(output,"existing.mkv");File.WriteAllText(prep.Destination,"existing user output");Finish(prep.PrepareAsync());
Check("worker refusal for an existing destination is surfaced without overwrite",File.ReadAllText(prep.Destination)=="existing user output"&&prep.NotBusy&&!prep.CanOpen&&prep.Status.Contains("Destination exists"));
var newer=Path.Combine(output,"newer-settings");Directory.CreateDirectory(newer);var original="{\"version\":2,\"future\":\"keep\"}";File.WriteAllText(Path.Combine(newer,"preparation-settings.json"),original);var future=new PreparationViewModel(()=>output,()=>newer);future.Source="different";
Check("future settings schema stays untouched and cannot start",!future.CanStart&&File.ReadAllText(Path.Combine(newer,"preparation-settings.json"))==original);
File.WriteAllText(Path.Combine(output,"summary.json"),JsonSerializer.Serialize(new{passed=true,checks,scope="Headless UI plus fake owned Python worker; no video, no inference, no GPU claims"},new JsonSerializerOptions{WriteIndented=true}));
