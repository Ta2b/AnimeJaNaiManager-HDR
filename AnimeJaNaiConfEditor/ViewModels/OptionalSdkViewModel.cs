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
    readonly TrueHdrSdkStore hdrStore;
    readonly Action changed;
    readonly string onboarding;
    readonly string managedRoot;
    CancellationTokenSource? cancel;
    InstalledFruc? installed;
    InstalledTrueHdr? installedHdr;
    string hdrImportStatus="";
    string status="";
    bool busy;
    string activeImport="";
    public OptionalSdkViewModel(string dataDirectory,Action onChange) {
        store=new(dataDirectory);hdrStore=new(dataDirectory);managedRoot=Path.GetFullPath(Path.Combine(dataDirectory,"optional-sdks","fruc","versions"))+Path.DirectorySeparatorChar;changed=onChange;onboarding=Path.Combine(dataDirectory,"optional-sdks","manager-onboarding-v1");Refresh(false);
    }
    public InstalledFruc? InstalledFruc=>installed;
    public bool IsManagedPath(string path) {
        try{return Path.IsPathFullyQualified(path)&&Path.GetFullPath(path).StartsWith(managedRoot,StringComparison.Ordinal);}
        catch(ArgumentException){return false;}
    }
    public string TrueHdrRoot => Environment.GetEnvironmentVariable("ANIMEJANAI_VFX_SDK") is {Length:>0} root?root:installedHdr?.Root??"/usr/local/VideoFX";
    public InstalledTrueHdr? InstalledTrueHdr=>installedHdr;
    public string TrueHdrImportStatus {get=>hdrImportStatus;private set=>this.RaiseAndSetIfChanged(ref hdrImportStatus,value);}
    public static bool HasTrueHdrSdk(string root) => Path.IsPathFullyQualified(root)&&new[]{"lib/libVideoFX.so","lib/libNVCVImage.so","features/nvvfxtruehdr/lib/libnvVFXTrueHDR.so"}.All(n=>File.Exists(Path.Combine(root,n)));
    public bool TrueHdrReady => MainWindowViewModel.OnLinux&&HasTrueHdrSdk(TrueHdrRoot)&&PipelineReaderFeatures.HasTrueHdr(InterpolationStage.InferenceDirectory);
    public string TrueHdrStatus => !MainWindowViewModel.OnLinux?"Ordered TrueHDR playback currently requires the Linux NVIDIA reader.":!HasTrueHdrSdk(TrueHdrRoot)?"Install the Maxine VideoFX Core and TrueHDR feature from the NVIDIA guide.":!PipelineReaderFeatures.HasTrueHdr(InterpolationStage.InferenceDirectory)?"SDK installed. Update the player components to enable HDR stages.":"Installed — NVIDIA TrueHDR can be added once per profile. GPU, SDR BT.709 input and driver are checked at playback.";
    public bool NeedsOnboarding=>!File.Exists(onboarding);
    public bool IsBusy {get=>busy;private set {this.RaiseAndSetIfChanged(ref busy,value);this.RaisePropertyChanged(nameof(CanImport));this.RaisePropertyChanged(nameof(IsFrucBusy));this.RaisePropertyChanged(nameof(IsTrueHdrBusy));}}
    public bool IsFrucBusy=>IsBusy&&activeImport=="fruc";
    public bool IsTrueHdrBusy=>IsBusy&&activeImport=="hdr";
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
        try{installedHdr=hdrStore.Read();TrueHdrImportStatus=installedHdr.Root!=null?"Core and TrueHDR feature installed. No manual paths are needed. Reopen the player to use the new SDK dependency paths.":installedHdr.Core?"Core installed. Import the Linux TrueHDR feature archive to complete this managed SDK.":installedHdr.Feature?"TrueHDR feature installed. Import the Linux Maxine Core archive to complete this managed SDK.":"";}
        catch(Exception e)when(e is IOException or InvalidDataException or JsonException or UnauthorizedAccessException){installedHdr=null;TrueHdrImportStatus=e.Message;}
        this.RaisePropertyChanged(nameof(InstalledFruc));this.RaisePropertyChanged(nameof(InstalledTrueHdr));this.RaisePropertyChanged(nameof(FrucReady));this.RaisePropertyChanged(nameof(FrucStatus));this.RaisePropertyChanged(nameof(TrueHdrRoot));this.RaisePropertyChanged(nameof(TrueHdrReady));this.RaisePropertyChanged(nameof(TrueHdrStatus));if(notify)changed();
    }
    public async Task ImportAsync(string path) {
        if(!CanImport)return;activeImport="fruc";IsBusy=true;cancel=new();Status="Checking and importing the SDK ZIP…";
        try{await Task.Run(()=>store.Import(path,cancel.Token));Refresh();Status="FRUC SDK installed. Add NVIDIA FRUC in your processing chain; paths are filled automatically.";}
        catch(OperationCanceledException){Status="Import cancelled. The previous SDK remains selected.";}
        catch(Exception e)when(e is IOException or InvalidDataException or JsonException or UnauthorizedAccessException){Status=e.Message;}
        finally{cancel.Dispose();cancel=null;IsBusy=false;}
    }
    public void Cancel()=>cancel?.Cancel();
    public async Task ImportTrueHdrAsync(string path) {
        if(!CanImport)return;activeImport="hdr";IsBusy=true;cancel=new();TrueHdrImportStatus="Checking and importing the SDK archive… Large Core archives include several GB of dependencies.";
        try{await Task.Run(()=>hdrStore.Import(path,cancel.Token));Refresh();}
        catch(OperationCanceledException){TrueHdrImportStatus="Import cancelled. The previous SDK remains selected.";}
        catch(Exception e)when(e is IOException or InvalidDataException or JsonException or UnauthorizedAccessException){TrueHdrImportStatus=e.Message;}
        finally{cancel.Dispose();cancel=null;IsBusy=false;}
    }
    public void OpenFrucDownload()=>Open("https://developer.nvidia.com/opticalflow/download");
    public void OpenTrueHdrDownload()=>Open("https://docs.nvidia.com/maxine/vfx/latest/LinuxVFXSDK/GetStartedonLinux.html");
    void Open(string url){try{Process.Start(new ProcessStartInfo(url){UseShellExecute=true});}catch(Exception e){Status=e.Message;}}
}
