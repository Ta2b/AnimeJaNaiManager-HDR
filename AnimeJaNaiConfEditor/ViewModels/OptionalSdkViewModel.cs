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
    // The engine modules TrueHDR runs in: libaji_trt_rtx.so comes with the player (a link into
    // its image, remade at every launch even if it dangles now); libaji_rtx.so, the bridge to the
    // NVIDIA SDK, is built against the SDK on the computer and no release includes it yet.
    public static bool TrueHdrModulesPresent(string inference) => new[]{"libaji_trt_rtx.so","libaji_rtx.so"}.All(n=>PipelineReaderFeatures.Present(Path.Combine(inference,n)));
    // Same conditions as the player's hdr_backend(): NVIDIA, TensorRT, modules, SDK, PQ output.
    public bool TrueHdrReady => TrueHdrBlocker.Length==0;
    public string TrueHdrBlocker
    {
        get
        {
            var vm=MainWindowViewModel.Instance;
            if(!MainWindowViewModel.OnLinux)return "NVIDIA TrueHDR inside the engine requires the Linux NVIDIA reader; Windows uses RTX Video.";
            if(vm?.ComponentManager?.GpuNvidia==false)return "An NVIDIA GPU is required.";
            if(vm?.AnimeJaNaiConf is {} conf&&!conf.TensorRtSelected)return "The TensorRT engine must be selected (Engines page).";
            if(!HasTrueHdrSdk(TrueHdrRoot))return "Import the NVIDIA Maxine SDK (Core and TrueHDR archives) on the Engines page; it is not installed at "+TrueHdrRoot+".";
            string inference=InterpolationStage.InferenceDirectory;
            if(!PipelineReaderFeatures.Present(Path.Combine(inference,"libaji_trt_rtx.so")))return "The player's TrueHDR engine (libaji_trt_rtx.so) is missing: update the player.";
            if(!PipelineReaderFeatures.Present(Path.Combine(inference,TrueHdrBridgeBuilder.Library)))return BridgeCause;
            if(!PipelineReaderFeatures.HasTrueHdr(InterpolationStage.InferenceDirectory))return "The player's ordered pipeline module has no TrueHDR adapter: update the player.";
            if(vm?.HdrSettings is {} hdr&&!hdr.LinuxPq)return "Choose the HDR PQ output (Display settings): TrueHDR only acts in PQ.";
            return "";
        }
    }
    public string TrueHdrStatus => TrueHdrReady?"NVIDIA TrueHDR is ready: SDK at "+TrueHdrRoot+"; GPU, driver and SDR BT.709 input are checked at playback.":TrueHdrBlocker;
    public bool NeedsOnboarding=>!File.Exists(onboarding);
    public bool IsBusy {get=>busy;private set {this.RaiseAndSetIfChanged(ref busy,value);this.RaisePropertyChanged(nameof(CanImport));this.RaisePropertyChanged(nameof(IsTrueHdrBusy));this.RaisePropertyChanged(nameof(CanBuildBridge));}}
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
        this.RaisePropertyChanged(nameof(InstalledTrueHdr));this.RaisePropertyChanged(nameof(TrueHdrRoot));this.RaisePropertyChanged(nameof(TrueHdrReady));this.RaisePropertyChanged(nameof(TrueHdrBlocker));this.RaisePropertyChanged(nameof(TrueHdrStatus));foreach(var n in new[]{nameof(BridgeInstalled),nameof(CanBuildBridge),nameof(ShowBridge)})this.RaisePropertyChanged(n);if(notify)changed();
    }
    public void Cancel()=>cancel?.Cancel();
    public async Task ImportTrueHdrAsync(string path) {
        if(!CanImport)return;IsBusy=true;cancel=new();TrueHdrImportStatus="Checking and importing the SDK archive… Large Core archives include several GB of dependencies.";
        try{await Task.Run(()=>hdrStore.Import(path,cancel.Token));Refresh();}
        catch(OperationCanceledException){TrueHdrImportStatus="Import cancelled. The previous SDK remains selected.";}
        catch(Exception e)when(e is IOException or InvalidDataException or JsonException or UnauthorizedAccessException){TrueHdrImportStatus=e.Message;}
        finally{cancel.Dispose();cancel=null;IsBusy=false;}
        // A complete SDK: build the bridge at once when nothing else is missing for it.
        if(installedHdr?.Root!=null&&!BridgeInstalled&&CanBuildBridge)await BuildBridgeAsync();
    }
    // The bridge libaji_rtx.so, compiled here against the SDK in use.
    public bool BridgeInstalled => PipelineReaderFeatures.Present(Path.Combine(InterpolationStage.InferenceDirectory,TrueHdrBridgeBuilder.Library));
    static string? BridgeCompiler => TrueHdrBridgeBuilder.Compiler();
    (string Core,string Feature)? BridgeHeaders {
        get {
            try{if(installedHdr?.Root!=null&&TrueHdrRoot==installedHdr.Root)return hdrStore.HeaderDirectories();}
            catch(Exception e)when(e is IOException or InvalidDataException or JsonException or UnauthorizedAccessException){return null;}
            return TrueHdrBridgeBuilder.SystemHeaders(TrueHdrRoot);
        }
    }
    public bool CanBuildBridge => MainWindowViewModel.OnLinux&&!IsBusy&&HasTrueHdrSdk(TrueHdrRoot)&&BridgeCompiler!=null&&BridgeHeaders!=null;
    public bool ShowBridge => MainWindowViewModel.OnLinux&&HasTrueHdrSdk(TrueHdrRoot);
    string BridgeCause => BridgeCompiler==null
        ? "The TrueHDR bridge (libaji_rtx.so) is built on this computer and needs a C++ compiler. Install g++ (Arch: sudo pacman -S gcc; Debian/Ubuntu: sudo apt install g++; Fedora: sudo dnf install gcc-c++), then click \"Build the TrueHDR bridge\" on the Engines page."
        : BridgeHeaders==null
            ? "The TrueHDR bridge (libaji_rtx.so) needs the SDK headers: import the Core and TrueHDR archives again on the Engines page (earlier imports did not keep them); it is then built automatically."
            : "The TrueHDR bridge (libaji_rtx.so) is not built yet: click \"Build the TrueHDR bridge\" on the Engines page (a few seconds).";
    string bridgeStatus="";
    public string BridgeStatus {get=>bridgeStatus;private set=>this.RaiseAndSetIfChanged(ref bridgeStatus,value);}
    public async Task BuildBridgeAsync() {
        if(!CanBuildBridge||BridgeHeaders is not {} headers||BridgeCompiler is not {} compiler)return;
        IsBusy=true;cancel=new();BridgeStatus="Building the TrueHDR bridge with "+Path.GetFileName(compiler)+"…";
        try{string built=await TrueHdrBridgeBuilder.BuildAsync(compiler,headers,TrueHdrRoot,InterpolationStage.InferenceDirectory,cancel.Token);BridgeStatus="TrueHDR bridge built: "+built+". Restart the player to use it.";}
        catch(OperationCanceledException){BridgeStatus="Bridge build cancelled.";}
        catch(Exception e)when(e is IOException or InvalidDataException or UnauthorizedAccessException or System.ComponentModel.Win32Exception){BridgeStatus=e.Message;}
        finally{cancel.Dispose();cancel=null;IsBusy=false;Refresh();}
    }
    public async void BuildBridge()=>await BuildBridgeAsync();
    public void OpenTrueHdrDownload()=>Open("https://docs.nvidia.com/maxine/vfx/latest/LinuxVFXSDK/GetStartedonLinux.html");
    void Open(string url){try{Process.Start(new ProcessStartInfo(url){UseShellExecute=true});}catch(Exception e){Status=e.Message;}}
}
