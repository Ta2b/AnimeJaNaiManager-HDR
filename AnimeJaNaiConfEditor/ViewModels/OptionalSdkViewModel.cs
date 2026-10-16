using AnimeJaNaiConfEditor.Services;
using ReactiveUI;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
namespace AnimeJaNaiConfEditor.ViewModels;
public sealed class OptionalSdkViewModel : ReactiveObject
{
    readonly FrucSdkStore store;
    readonly Action changed;
    readonly string onboarding;
    readonly string managedRoot;
    CancellationTokenSource? cancel;
    InstalledFruc? installed;
    string status="";
    bool busy;
    public OptionalSdkViewModel(string dataDirectory,Action onChange) {
        store=new(dataDirectory);managedRoot=Path.GetFullPath(Path.Combine(dataDirectory,"optional-sdks","fruc","versions"))+Path.DirectorySeparatorChar;changed=onChange;onboarding=Path.Combine(dataDirectory,"optional-sdks","manager-onboarding-v1");Refresh(false);
    }
    public InstalledFruc? InstalledFruc=>installed;
    public bool IsManagedPath(string path) {
        try{return Path.IsPathFullyQualified(path)&&Path.GetFullPath(path).StartsWith(managedRoot,StringComparison.Ordinal);}
        catch(ArgumentException){return false;}
    }
    public bool NeedsOnboarding=>!File.Exists(onboarding);
    public bool IsBusy {get=>busy;private set {this.RaiseAndSetIfChanged(ref busy,value);this.RaisePropertyChanged(nameof(CanImport));}}
    public bool CanImport=>!IsBusy&&MainWindowViewModel.OnLinux;
    public string Status {get=>status;private set=>this.RaiseAndSetIfChanged(ref status,value);}
    public bool FrucReady=>MainWindowViewModel.OnLinux&&installed!=null&&new[]{"libaji_fruc.so","aji_fruc.fatbin","libaji_pipeline.so","aji_pipeline.fatbin","libaji_trt.so"}.All(n=>File.Exists(Path.Combine(InterpolationStage.InferenceDirectory,n)));
    public string FrucStatus=>!MainWindowViewModel.OnLinux?"FRUC playback is currently available on Linux NVIDIA only.":installed==null?"Not installed — import the NVIDIA Optical Flow SDK ZIP.":!FrucReady?"SDK installed. Install the NVIDIA reader components to enable FRUC.":"Installed — FRUC can be added to a chain. GPU and video format are checked at playback.";
    public void AcknowledgeOnboarding() {
        try{Directory.CreateDirectory(Path.GetDirectoryName(onboarding)!);File.WriteAllText(onboarding,"1\n");}catch(Exception e)when(e is IOException or UnauthorizedAccessException){Status=e.Message;}
    }
    public void Refresh()=>Refresh(true);
    void Refresh(bool notify) {
        try{installed=store.Read();Status=installed==null?"":"SDK files are installed locally. No manual paths are needed.";}
        catch(Exception e)when(e is IOException or InvalidDataException or JsonException or UnauthorizedAccessException){installed=null;Status=e.Message;}
        this.RaisePropertyChanged(nameof(InstalledFruc));this.RaisePropertyChanged(nameof(FrucReady));this.RaisePropertyChanged(nameof(FrucStatus));if(notify)changed();
    }
    public async Task ImportAsync(string path) {
        if(!CanImport)return;IsBusy=true;cancel=new();Status="Checking and importing the SDK ZIP…";
        try{await Task.Run(()=>store.Import(path,cancel.Token));Refresh();Status="FRUC SDK installed. Add NVIDIA FRUC in your processing chain; paths are filled automatically.";}
        catch(OperationCanceledException){Status="Import cancelled. The previous SDK remains selected.";}
        catch(Exception e)when(e is IOException or InvalidDataException or JsonException or UnauthorizedAccessException){Status=e.Message;}
        finally{cancel.Dispose();cancel=null;IsBusy=false;}
    }
    public void Cancel()=>cancel?.Cancel();
    public void OpenFrucDownload()=>Open("https://developer.nvidia.com/opticalflow/download");
    public void OpenTrueHdrDownload()=>Open("https://docs.nvidia.com/maxine/vfx/latest/LinuxVFXSDK/GetStartedonLinux.html");
    void Open(string url){try{Process.Start(new ProcessStartInfo(url){UseShellExecute=true});}catch(Exception e){Status=e.Message;}}
}
