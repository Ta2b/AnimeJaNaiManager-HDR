using ReactiveUI;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace AnimeJaNaiConfEditor.ViewModels;

// External inference runs in one owned process. The UI never loads CUDA,
// PyTorch, weights or an SDK and never advertises a native GMFSS provider.
public sealed class PreparationViewModel : ViewModelBase
{
    readonly Func<string> root,data;
    readonly Dictionary<string,JsonElement> extra=new();
    bool loading=true,settingsCompatible=true,busy,cancelRequested;
    Task activeTask=Task.CompletedTask;
    Process? process;
    string? cancelFile,jobId,stagingPath,destinationForJob;
    CancellationTokenSource? forceCancel;
    string source="",destination="",modelSource="",weights="",python=OperatingSystem.IsWindows()?"python.exe":"python3",ffmpeg="ffmpeg",ffprobe="ffprobe",factor="2",codec="lossless-rgb",audio="copy";
    string status="Prepare a video with original GMFSS Fortuna. This is offline CUDA inference; no real-time GMFSS reader is installed.",completed="";
    double progress;
    public PreparationViewModel(Func<string> root,Func<string> data)
    {
        this.root=root;this.data=data;
        try
        {
            string path=Path.Combine(data(),"preparation-settings.json");
            if(File.Exists(path))
            {
                var saved=JsonSerializer.Deserialize<Dictionary<string,JsonElement>>(File.ReadAllText(path))??new();
                if(saved.TryGetValue("version",out var version)&&version.GetInt32()!=1)throw new InvalidDataException("Newer preparation settings preserved. Use the matching Manager version.");
                string Read(string key,string fallback)=>saved.TryGetValue(key,out var value)?value.GetString()??fallback:fallback;
                source=Read("source",source);destination=Read("destination",destination);modelSource=Read("model_source",modelSource);weights=Read("weights",weights);
                python=Read("python",python);ffmpeg=Read("ffmpeg",ffmpeg);ffprobe=Read("ffprobe",ffprobe);factor=Read("factor",factor);codec=Read("output_codec",codec);audio=Read("audio",audio);
                foreach(var pair in saved)extra[pair.Key]=pair.Value.Clone();
            }
        }
        catch(Exception e) when(e is IOException or InvalidDataException or JsonException or InvalidOperationException)
        { settingsCompatible=false;status="Preparation settings preserved: "+e.Message; }
        finally{loading=false;}
    }
    public string Source {get=>source;set=>Set(ref source,value);}
    public string Destination {get=>destination;set=>Set(ref destination,value);}
    public string ModelSource {get=>modelSource;set=>Set(ref modelSource,value);}
    public string Weights {get=>weights;set=>Set(ref weights,value);}
    public string Python {get=>python;set=>Set(ref python,value);}
    public string Ffmpeg {get=>ffmpeg;set=>Set(ref ffmpeg,value);}
    public string Ffprobe {get=>ffprobe;set=>Set(ref ffprobe,value);}
    public string Factor {get=>factor;set=>Set(ref factor,value);}
    public string OutputCodec {get=>codec;set=>Set(ref codec,value);}
    public string Audio {get=>audio;set=>Set(ref audio,value);}
    public string[] OutputCodecs=>["lossless-rgb","hevc44410-sdr"];
    public string[] AudioChoices=>["copy","drop"];
    public bool IsBusy {get=>busy;private set {this.RaiseAndSetIfChanged(ref busy,value);Notify();}}
    public bool NotBusy=>!IsBusy;
    public bool CanStart=>settingsCompatible&&NotBusy&&!MainWindowViewModel.OnMac&&new[]{Source,Destination,ModelSource,Weights,Python,Ffmpeg,Ffprobe}.All(x=>!string.IsNullOrWhiteSpace(x));
    public bool CanCancel=>IsBusy&&!cancelRequested&&completed.Length==0;
    public bool CanOpen=>NotBusy&&completed.Length>0&&File.Exists(completed);
    public string Status {get=>status;private set=>this.RaiseAndSetIfChanged(ref status,value);}
    public double Progress {get=>progress;private set=>this.RaiseAndSetIfChanged(ref progress,value);}
    public string MethodNotice=>MainWindowViewModel.OnMac?"GMFSS Fortuna requires NVIDIA CUDA. This local provider is unavailable on macOS; settings can be kept for a CUDA machine.":"Original GMFSS Fortuna, FP32/scale1, CUDA CuPy softsplat. Install its pinned source, official weights and a compatible Python/PyTorch/CuPy environment. No weights or training environment are bundled.";
    public bool EngineConfigured=>new[]{ModelSource,Weights,Python,Ffmpeg,Ffprobe}.All(x=>!string.IsNullOrWhiteSpace(x));
    public string EngineStatus=>EngineConfigured?"GMFSS paths configured · check files before the first export":"Set up GMFSS in Engines & components to prepare a video.";
    public void ConfigureEngine()=>MainWindowViewModel.Instance?.ShowPreparationEngineSettings();
    public string OutputNotice=>OutputCodec=="hevc44410-sdr"?"HEVC44410: lossless encoding after an explicit RGB8→YUV44410 SDR conversion. Requires FFmpeg libx265 and compatible NVIDIA HEVC4:4:4 decoding for later native interpolation. This conversion has quantization; it is not RGB pixel-exact.":"Lossless RGB8 FFV1 in Matroska. Keeps the official GMFSS presentation pixels and SDR metadata. Software decoding may be required; native CUDA chains need a compatible hardware-decoded input. The output container can quantize timestamps.";
    string Worker=>Path.Combine(root(),"animejanai","preparation","worker.py");
    void Set(ref string field,string value,[CallerMemberName]string? name=null)
    {
        value??="";if(field==value)return;this.RaiseAndSetIfChanged(ref field,value,name);Notify();
        if(name==nameof(OutputCodec))this.RaisePropertyChanged(nameof(OutputNotice));
        if(!loading&&settingsCompatible)SaveSettings();
    }
    void Notify(){foreach(var name in new[]{nameof(NotBusy),nameof(CanStart),nameof(CanCancel),nameof(CanOpen),nameof(EngineConfigured),nameof(EngineStatus)})this.RaisePropertyChanged(name);}
    void SaveSettings()
    {
        try
        {
            var saved=new Dictionary<string,object>();foreach(var pair in extra)saved[pair.Key]=pair.Value;
            saved["version"]=1;
            foreach(var pair in new Dictionary<string,string>{["source"]=Source,["destination"]=Destination,["model_source"]=ModelSource,["weights"]=Weights,["python"]=Python,["ffmpeg"]=Ffmpeg,["ffprobe"]=Ffprobe,["factor"]=Factor,["output_codec"]=OutputCodec,["audio"]=Audio})saved[pair.Key]=pair.Value;
            Directory.CreateDirectory(data());string path=Path.Combine(data(),"preparation-settings.json"),temp=path+".tmp-"+Guid.NewGuid().ToString("N");
            try{File.WriteAllText(temp,JsonSerializer.Serialize(saved,new JsonSerializerOptions{WriteIndented=true}));File.Move(temp,path,true);}finally{if(File.Exists(temp))File.Delete(temp);}
        }
        catch(Exception e) when(e is IOException or UnauthorizedAccessException){Status="Cannot save preparation paths: "+e.Message;}
    }
    public Task InspectAsync()=>BeginAsync(true);
    public Task PrepareAsync()=>BeginAsync(false);
    Task BeginAsync(bool inspect) { if(!CanStart)return Task.CompletedTask;activeTask=StartAsync(inspect);return activeTask; }
    public async Task StopAsync() { if(IsBusy) { Cancel();await activeTask; } }
    async Task StartAsync(bool inspect)
    {
        if(!CanStart)return;
        cancelRequested=false;completed="";IsBusy=true;Progress=0;stagingPath=null;bool started=false;
        try
        {
            if(!File.Exists(Worker))throw new FileNotFoundException("Preparation worker missing. Update the player package.",Worker);
            if(!int.TryParse(Factor,out int n)||n<2||n>8)throw new InvalidDataException("GMFSS factor must be an integer2..8.");
            if(!OutputCodecs.Contains(OutputCodec)||!AudioChoices.Contains(Audio))throw new InvalidDataException("Choose a supported output/audio policy.");
            jobId=Guid.NewGuid().ToString("N");string job=Path.Combine(data(),"preparation-jobs",jobId);Directory.CreateDirectory(job);
            cancelFile=Path.Combine(job,"cancel");destinationForJob=Path.GetFullPath(Destination);
            var spec=new Dictionary<string,object>{["version"]=1,["method"]="gmfss-fortuna",["source"]=Source,["destination"]=Destination,["model_source"]=ModelSource,["weights"]=Weights,["factor"]=n,["ffmpeg"]=Ffmpeg,["ffprobe"]=Ffprobe,["audio"]=Audio,["output_codec"]=OutputCodec};
            string specFile=Path.Combine(job,"job.json");File.WriteAllText(specFile,JsonSerializer.Serialize(spec,new JsonSerializerOptions{WriteIndented=true}));
            var start=new ProcessStartInfo(Python){UseShellExecute=false,RedirectStandardOutput=true,RedirectStandardError=true,CreateNoWindow=true};
            start.ArgumentList.Add(Worker);start.ArgumentList.Add(specFile);
            if(inspect)start.ArgumentList.Add("--inspect");
            start.ArgumentList.Add("--cancel-file");start.ArgumentList.Add(cancelFile);start.ArgumentList.Add("--job-id");start.ArgumentList.Add(jobId);
            process=new Process{StartInfo=start};Status=inspect?"Checking pinned files and source timing (no GPU inference)…":"Starting GMFSS preparation…";
            if(!process.Start())throw new IOException("Cannot start preparation Python.");
            started=true;
            var errors=new StringBuilder();
            async Task ErrorLog()
            {
                using var log=new StreamWriter(Path.Combine(job,"stderr.log"));string? line;
                while((line=await process.StandardError.ReadLineAsync())!=null){await log.WriteLineAsync(line);errors.AppendLine(line);if(errors.Length>8192)errors.Remove(0,errors.Length-8192);}
            }
            Task errorTask=ErrorLog();bool terminal=false;
            using(var log=new StreamWriter(Path.Combine(job,"progress.jsonl")))
            {
                string? line;
                while((line=await process.StandardOutput.ReadLineAsync())!=null)
                {
                    await log.WriteLineAsync(line);
                    if(!line.StartsWith('{'))continue;
                    using var json=JsonDocument.Parse(line);var message=json.RootElement;
                    if(!message.TryGetProperty("protocol",out var protocol)||protocol.GetInt32()!=1)continue;
                    string state=message.GetProperty("state").GetString()??"";
                    if(message.TryGetProperty("staging",out var dir))stagingPath=dir.GetString();
                    if(state=="running")
                    {
                        int count=message.GetProperty("source_completed").GetInt32(),total=message.GetProperty("source_total").GetInt32();
                        Progress=total>0?100.0*count/total:0;Status=$"GMFSS: {count}/{total} source frames, {message.GetProperty("output_frames").GetInt32()} output frames.";
                    }
                    else if(state=="complete")
                    {
                        string output=Path.GetFullPath(message.GetProperty("destination").GetString()!);
                        if(output!=destinationForJob||!File.Exists(output))throw new IOException("Worker reported an unexpected/missing output.");
                        completed=output;Notify();Progress=100;terminal=true;Status="Prepared video: "+output+"\n"+OutputNotice;
                    }
                    else if(state=="inspected"){terminal=true;Status="Pinned files and source timing checked. GPU/runtime availability is checked when preparing.";}
                    else if(state is "failed" or "cancelled"){terminal=true;Status=message.GetProperty("message").GetString()??state;}
                    else if(message.TryGetProperty("message",out var detail))Status=detail.GetString()??state;
                }
            }
            await process.WaitForExitAsync();await errorTask;
            if(cancelRequested&&completed.Length==0){Status="Preparation cancelled. Original source and existing destination preserved.";}
            else if(process.ExitCode!=0){completed="";if(!terminal)Status="Preparation failed: "+errors;}
            else if(!terminal)throw new IOException("Worker exited without a terminal result; no usable preparation was reported.");
        }
        catch(Exception e){completed="";Status="Preparation failed: "+e.Message;}
        finally
        {
            if(started&&process is {HasExited:false}){try{process.Kill(true);await process.WaitForExitAsync();}catch(InvalidOperationException){}}
            forceCancel?.Cancel();forceCancel?.Dispose();forceCancel=null;process?.Dispose();process=null;
            CleanupStaging();IsBusy=false;cancelFile=null;jobId=null;destinationForJob=null;
        }
    }
    public void Cancel()
    {
        if(!CanCancel)return;cancelRequested=true;Notify();Status="Cancelling owned preparation…";
        try { if(cancelFile!=null)File.WriteAllText(cancelFile,"cancel\n"); }
        catch(IOException e) { Status="Cancellation signal failed; stopping owned process: "+e.Message; }
        forceCancel=new CancellationTokenSource();var token=forceCancel.Token;var owned=process;
        _=Task.Run(async()=>{try{await Task.Delay(5000,token);if(owned is {HasExited:false})owned.Kill(true);}catch(OperationCanceledException){}catch(InvalidOperationException){}});
    }
    void CleanupStaging()
    {
        // Only the worker's marked same-parent temporary folder is ours.
        if(stagingPath==null||jobId==null||destinationForJob==null)return;
        try
        {
            string dir=Path.GetFullPath(stagingPath),parent=Path.GetDirectoryName(destinationForJob)!;
            if(Path.GetDirectoryName(dir)!=parent||!Path.GetFileName(dir).StartsWith(".animejanai-prepare-"+jobId+"-",StringComparison.Ordinal))return;
            string marker=Path.Combine(dir,"job-owner");if(!File.Exists(marker)||File.ReadAllText(marker)!=jobId)return;
            foreach(string name in new[]{"decode.log","encode.log","video"+Path.GetExtension(destinationForJob),"job-owner"}){string file=Path.Combine(dir,name);if(File.Exists(file))File.Delete(file);}
            if(!Directory.EnumerateFileSystemEntries(dir).Any())Directory.Delete(dir);
        }
        catch(IOException){}catch(UnauthorizedAccessException){}
    }
    public void OpenPrepared()
    {
        if(!CanOpen)return;
        try
        {
            // The platform's file association opens the prepared media. Its
            // subsequent player profile remains an explicit user choice.
            Process.Start(new ProcessStartInfo(completed){UseShellExecute=true});
        }
        catch(Exception e){Status="Cannot open prepared video: "+e.Message;}
    }
    public void PickerError(string message)=>Status=message;
}
