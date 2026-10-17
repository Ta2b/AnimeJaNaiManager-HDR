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
    readonly TrueHdrSdkStore hdrStore;
    readonly Action changed;
    readonly string onboarding;
    CancellationTokenSource? cancel;
    InstalledTrueHdr? installedHdr;
    string hdrImportStatus="";
    string status="";
    bool busy;
    public OptionalSdkViewModel(string dataDirectory,Action onChange) {
        hdrStore=new(dataDirectory);changed=onChange;onboarding=Path.Combine(dataDirectory,"optional-sdks","manager-onboarding-v1");Refresh(false);
    }
    public string TrueHdrRoot => Environment.GetEnvironmentVariable("ANIMEJANAI_VFX_SDK") is {Length:>0} root?root:installedHdr?.Root??"/usr/local/VideoFX";
    public InstalledTrueHdr? InstalledTrueHdr=>installedHdr;
    public string TrueHdrImportStatus {get=>hdrImportStatus;private set=>this.RaiseAndSetIfChanged(ref hdrImportStatus,value);}
    public static bool HasTrueHdrSdk(string root) => Path.IsPathFullyQualified(root)&&new[]{"lib/libVideoFX.so","lib/libNVCVImage.so","features/nvvfxtruehdr/lib/libnvVFXTrueHDR.so"}.All(n=>File.Exists(Path.Combine(root,n)));
    public bool TrueHdrReady => MainWindowViewModel.OnLinux&&HasTrueHdrSdk(TrueHdrRoot)&&PipelineReaderFeatures.HasTrueHdr(InterpolationStage.InferenceDirectory);
    public string TrueHdrStatus => !MainWindowViewModel.OnLinux?"Ordered TrueHDR playback currently requires the Linux NVIDIA reader.":!HasTrueHdrSdk(TrueHdrRoot)?"Install the Maxine VideoFX Core and TrueHDR feature from the NVIDIA guide.":!PipelineReaderFeatures.HasTrueHdr(InterpolationStage.InferenceDirectory)?"SDK installed. Update the player components to enable HDR stages.":"Installed — NVIDIA TrueHDR can be added once per profile. GPU, SDR BT.709 input and driver are checked at playback.";
    public bool NeedsOnboarding=>!File.Exists(onboarding);
    public bool IsBusy {get=>busy;private set {this.RaiseAndSetIfChanged(ref busy,value);this.RaisePropertyChanged(nameof(CanImport));this.RaisePropertyChanged(nameof(IsTrueHdrBusy));}}
    public bool IsTrueHdrBusy=>IsBusy;
    public bool CanImport=>!IsBusy&&MainWindowViewModel.OnLinux;
    public string Status {get=>status;private set=>this.RaiseAndSetIfChanged(ref status,value);}
    public void AcknowledgeOnboarding() {
        try{Directory.CreateDirectory(Path.GetDirectoryName(onboarding)!);File.WriteAllText(onboarding,"1\n");}catch(Exception e)when(e is IOException or UnauthorizedAccessException){Status=e.Message;}
    }
    public void Refresh()=>Refresh(true);
    void Refresh(bool notify) {
        try{installedHdr=hdrStore.Read();TrueHdrImportStatus=installedHdr.Root!=null?"Core and TrueHDR feature installed. No manual paths are needed. Reopen the player to use the new SDK dependency paths.":installedHdr.Core?"Core installed. Import the Linux TrueHDR feature archive to complete this managed SDK.":installedHdr.Feature?"TrueHDR feature installed. Import the Linux Maxine Core archive to complete this managed SDK.":"";}
        catch(Exception e)when(e is IOException or InvalidDataException or JsonException or UnauthorizedAccessException){installedHdr=null;TrueHdrImportStatus=e.Message;}
        this.RaisePropertyChanged(nameof(InstalledTrueHdr));this.RaisePropertyChanged(nameof(TrueHdrRoot));this.RaisePropertyChanged(nameof(TrueHdrReady));this.RaisePropertyChanged(nameof(TrueHdrStatus));if(notify)changed();
    }
    public void Cancel()=>cancel?.Cancel();
    public async Task ImportTrueHdrAsync(string path) {
        if(!CanImport)return;IsBusy=true;cancel=new();TrueHdrImportStatus="Checking and importing the SDK archive… Large Core archives include several GB of dependencies.";
        try{await Task.Run(()=>hdrStore.Import(path,cancel.Token));Refresh();}
        catch(OperationCanceledException){TrueHdrImportStatus="Import cancelled. The previous SDK remains selected.";}
        catch(Exception e)when(e is IOException or InvalidDataException or JsonException or UnauthorizedAccessException){TrueHdrImportStatus=e.Message;}
        finally{cancel.Dispose();cancel=null;IsBusy=false;}
    }
    public void OpenTrueHdrDownload()=>Open("https://docs.nvidia.com/maxine/vfx/latest/LinuxVFXSDK/GetStartedonLinux.html");
    void Open(string url){try{Process.Start(new ProcessStartInfo(url){UseShellExecute=true});}catch(Exception e){Status=e.Message;}}
}
