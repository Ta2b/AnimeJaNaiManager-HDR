using AnimeJaNaiConfEditor.Services;
using Avalonia.Collections;
using ReactiveUI;
using Salaros.Configuration;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reactive.Linq;
using System.Runtime.Serialization;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace AnimeJaNaiConfEditor.ViewModels
{
    [DataContract]
    public partial class MainWindowViewModel : ViewModelBase
    {
        private static readonly CultureInfo ENGLISH_CULTURE = CultureInfo.GetCultureInfo("en-US");

        public static MainWindowViewModel? Instance { get; private set; }
        public ReferenceExportViewModel Reference { get; } = new(() => RootDir);
        public PreparationViewModel Preparation { get; } = new(() => RootDir, () => DataDir);
        public string SpatialQualityNotice => AnimeJaNai.Platform.QualitySemantics.Spatial;
        public string TemporalQualityNotice => AnimeJaNai.Platform.QualitySemantics.Temporal;
        public string GlobalFidelityNotice => AnimeJaNai.Platform.QualitySemantics.GlobalLock;
        public string RetimingNotice => AnimeJaNai.Platform.QualitySemantics.Retiming;
        public string EnsemblePrecisionNotice => AnimeJaNai.Platform.QualitySemantics.EnsemblePrecision;

        public MainWindowViewModel()
        {
            Instance = this;
            _expectedHash = AnimeJaNai.Platform.PlatformServices.FileHash(AnimeJaNaiConfPath);
            _expectedBank = AnimeJaNai.Platform.PlatformServices.CurrentBank(RootDir);
            AnimeJaNaiConf = ReadAnimeJaNaiConf(AnimeJaNaiConfPath, true);
            HdrSettings = new HdrSettings(HdrConfPath,
                _expectedBank.Contains("nvidia") ? "nvidia" : _expectedBank.Contains("amd") ? "amd" : null);
            OptionalSdks = new OptionalSdkViewModel(DataDir, RefreshOptionalSdkStages);
            foreach(var profile in AnimeJaNaiConf.UpscaleSlots)MigrateLegacyHdr(profile);
            Engines = new EngineStore(DataDir);
            Engines.Refresh();
            for (var i = 0; i < AnimeJaNaiConf.UpscaleSlots.Count; i++)
            {
                AnimeJaNaiConf.UpscaleSlots[i].MpvProfileName = $"upscale-on-{AnimeJaNaiConf.UpscaleSlots[i].SlotNumber}";
            }

            CheckAndDoBackup();

            SelectedMpvProfile = ReadCurrentProfileFromMpvConf();

            InitializeSelectedSlot();
            InitializeNavigation();

            RefreshComponentAwareness();
            ComponentManager.Refreshed += RefreshComponentAwareness;
            // macOS has no downloadable components: everything is in the application.
            if (!OnMac) _ = InitializeComponentManagerAsync();
        }

        public OptionalSdkViewModel OptionalSdks { get; }
        public void ShowOptionalEngines() => ShowEngines();
        public void ReturnToProfiles() => NavigateProfiles();
        private void RefreshOptionalSdkStages()
        {
            foreach (var slot in AnimeJaNaiConf.UpscaleSlots)
                foreach (var chain in slot.Chains) chain.RefreshPipeline();
            WriteAnimeJaNaiConf();
        }

        // ---- component awareness for the Profiles tab -------------------------------------
        // Install state comes from the disk (works offline); GPU identity comes from the
        // component engine once its refresh completes.

        // The platform the settings are for. The tests describe another one than they run on.
        public enum Os { Windows, Linux, MacOS }
        public static Os Platform { get; set; } =
            OperatingSystem.IsWindows() ? Os.Windows : OperatingSystem.IsMacOS() ? Os.MacOS : Os.Linux;
        public static bool OnWindows => Platform == Os.Windows;
        public static bool OnLinux => Platform == Os.Linux;
        public static bool OnMac => Platform == Os.MacOS;
        public bool WindowsFeatures => OnWindows;
        public bool LinuxFeatures => OnLinux;
        public bool MacFeatures => OnMac;
        public bool NotMacFeatures => !OnMac;
        // HDR output request (linux_hdr_output): Wayland on Linux, EDR on macOS.
        public bool HdrOutputChoice => OnLinux || OnMac;
        public string HdrOutputLabel => OnMac ? "HDR output" : "Wayland output";
        public string HdrOutputAutoLabel => OnMac ? "Follow video" : "Follow system";
        public string BenchmarkDescription => OnMac
            ? "Measure the three engines on each processor of this Mac (GPU, Neural Engine, both) and rank them. It runs offscreen; close the player and leave the Mac idle meanwhile."
            : "Measure your real playback fps (the speed you will actually get) at several resolutions. Close the player and leave the computer idle while it runs. The first run builds a TensorRT engine per resolution.";
        public string HdrOutputHelp => OnMac
            ? "Follow video keeps SDR video in SDR and HDR video in HDR. HDR PQ requests HDR output for an XDR or other HDR display. Add an HDR stage to convert SDR. Ctrl+H in the player switches the output, Ctrl+I shows the output diagnosis."
            : "Auto follows the compositor. Enable HDR in the desktop settings (GNOME, KDE) on a compatible display; grading requires negotiated PQ output. Ctrl+Alt+H shows the output diagnosis.";
        public string RifeMissingNote => OnMac
            ? "The RIFE models are missing from this installation."
            : "RIFE models are not installed. Install them from the Components tab to use interpolation.";
        private string _expectedHash = "";
        private string _expectedBank = "";
        private readonly object _profileSaveLock = new();
        private int _profileReadDepth;
        public static bool TrtOnDisk() => File.Exists(Path.Combine(
            Environment.GetEnvironmentVariable("ANIMEJANAI_INFERENCE_DIR") ?? Path.Combine(DataDir, "inference"),
            OnLinux ? "libnvinfer.so.11" : "nvinfer_11.dll"));

        public static bool VulkanOnDisk() => OnLinux && File.Exists(Path.Combine(
            Environment.GetEnvironmentVariable("ANIMEJANAI_INFERENCE_DIR") ?? Path.Combine(DataDir, "inference"), "libaji_vulkan.so"));
        // The ONNX Runtime engine; the vendor provider itself is only known at playback.
        public static bool OnnxRuntimeOnDisk() => OnLinux && File.Exists(Path.Combine(
            Environment.GetEnvironmentVariable("ANIMEJANAI_INFERENCE_DIR") ?? Path.Combine(DataDir, "inference"), "libaji_ort.so"));
        public static bool NativeMiGraphXOnDisk()
        {
            if (OnMac) return false;
            var folder = Environment.GetEnvironmentVariable("ANIMEJANAI_INFERENCE_DIR") ?? Path.Combine(DataDir, "inference");
            if (!File.Exists(Path.Combine(folder, OnLinux ? "libaji_migraphx_rgb.so" : "aji_migraphx.dll"))) return false;
            // The small module updates with the player; the optional Windows
            // runtime must be installed before enabling its backend choice.
            return OnLinux || File.Exists(Path.Combine(folder, "amd", "bin", "migraphx_c.dll")) ||
                File.Exists(Path.Combine(folder, "migraphx_c.dll"));
        }
        private bool _onnxRuntimeSelectable;
        public bool OnnxRuntimeSelectable { get => _onnxRuntimeSelectable; set => this.RaiseAndSetIfChanged(ref _onnxRuntimeSelectable, value); }
        public static bool VulkanRifeOnDisk() => File.Exists(Path.Combine(DataDir, "rife-vulkan", "rife-v4.6", "flownet.param")) &&
            File.Exists(Path.Combine(DataDir, "rife-vulkan", "rife-v4.6", "flownet.bin"));
        private bool _vulkanSelectable;
        public bool VulkanSelectable { get => _vulkanSelectable; set => this.RaiseAndSetIfChanged(ref _vulkanSelectable, value); }

        public static bool RifeOnDisk() =>
            Directory.Exists(Path.Combine(DataDir, "rife")) &&
            Directory.EnumerateFiles(Path.Combine(DataDir, "rife"), "*.onnx").Any();

        private bool _trtSelectable = true;
        public bool TrtSelectable
        {
            get => _trtSelectable;
            set => this.RaiseAndSetIfChanged(ref _trtSelectable, value);
        }

        private string _backendNotice = "";
        public string BackendNotice
        {
            get => _backendNotice;
            set
            {
                this.RaiseAndSetIfChanged(ref _backendNotice, value);
                this.RaisePropertyChanged(nameof(HasBackendNotice));
            }
        }

        public bool HasBackendNotice => !string.IsNullOrEmpty(BackendNotice);
        public string VulkanPresentationNotice => AnimeJaNai.Platform.BackendCapabilities.VulkanPresentation;

        private bool _rifeMissing;
        public bool RifeMissing
        {
            get => _rifeMissing;
            set => this.RaiseAndSetIfChanged(ref _rifeMissing, value);
        }

        // ---- display upscaling and HDR (portable_config/script-opts/animejanai_hdr_auto.conf) ----
        public HdrSettings HdrSettings { get; }
        public EngineStore Engines { get; }

        public string[] HdrTargetResolutions { get; } =
            ["auto", "1920x1080", "2560x1440", "3440x1440", "3840x1600", "3840x2160", "5120x2160", "5120x2880", "7680x4320"];

        public string HdrConfPath => Path.Combine(RootDir, "portable_config", "script-opts", "animejanai_hdr_auto.conf");

        // Options tied to one GPU maker are greyed when the machine has no GPU of that maker.
        // null = not known (Windows detection pending, or no way to tell AMD from Intel there):
        // the option stays selectable. A hybrid machine keeps the options of each of its GPUs.
        public readonly record struct GpuOptions(bool RtxHdr, bool RtxVsr, bool Amf, bool MiGraphX, bool OpenVino, bool SmoothMotion)
        {
            public static GpuOptions For(bool windows, bool? nvidia, bool? amd, bool? intel) => new(
                RtxHdr: nvidia != false,            // Windows: RTX Video SDK; Linux: Maxine TrueHDR stage
                RtxVsr: windows && nvidia != false,
                Amf: windows && amd != false,       // AMD VideoSR and AMF frame generation
                MiGraphX: amd != false,
                OpenVino: !windows && intel != false,
                // Linux only: on Windows Smooth Motion is a per-program driver setting (NVIDIA app).
                SmoothMotion: !windows && nvidia == true);
        }

        // RTX Video HDR needs an NVIDIA GPU; unknown (detection pending) keeps it selectable.
        private bool _rtxHdrSelectable = true;
        public bool RtxHdrSelectable
        {
            get => _rtxHdrSelectable;
            set => this.RaiseAndSetIfChanged(ref _rtxHdrSelectable, value);
        }

        // The NVIDIA driver announces Smooth Motion in its Vulkan layer manifest (RTX 40 and newer
        // drivers); without that line the variable set by the launcher would do nothing.
        public static bool SmoothMotionLayerInstalled(params string[] folders)
        {
            if (folders.Length == 0)
                folders = ["/usr/share/vulkan/implicit_layer.d", "/etc/vulkan/implicit_layer.d",
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local/share/vulkan/implicit_layer.d")];
            foreach (string folder in folders)
            {
                if (!Directory.Exists(folder)) continue;
                foreach (string file in Directory.EnumerateFiles(folder, "*.json"))
                    try { if (File.ReadAllText(file).Contains("NVPRESENT_ENABLE_SMOOTH_MOTION")) return true; }
                    catch (IOException) { }
                    catch (UnauthorizedAccessException) { }
            }
            return false;
        }
        private bool _smoothMotionSelectable;
        public bool SmoothMotionSelectable { get => _smoothMotionSelectable; set => this.RaiseAndSetIfChanged(ref _smoothMotionSelectable, value); }

        private bool _rtxVsrSelectable = true;
        public bool RtxVsrSelectable { get => _rtxVsrSelectable; set => this.RaiseAndSetIfChanged(ref _rtxVsrSelectable, value); }
        private bool _miGraphXSelectable;
        public bool MiGraphXSelectable { get => _miGraphXSelectable; set => this.RaiseAndSetIfChanged(ref _miGraphXSelectable, value); }
        private bool _openVinoSelectable;
        public bool OpenVinoSelectable { get => _openVinoSelectable; set => this.RaiseAndSetIfChanged(ref _openVinoSelectable, value); }

        // AMD AMF frame generation runs in the player on AMD only; unknown keeps it selectable.
        private bool _amfSelectable = true;
        public bool AmfSelectable
        {
            get => _amfSelectable;
            set => this.RaiseAndSetIfChanged(ref _amfSelectable, value);
        }

        // macOS: which engines have their model files (the application ships them next to the
        // ONNX files: NAME.mlpackage + NAME.mlpatch for Core ML, NAME.ajm for Metal).
        private bool _appleCoreMlSelectable = true;
        public bool AppleCoreMlSelectable { get => _appleCoreMlSelectable; set => this.RaiseAndSetIfChanged(ref _appleCoreMlSelectable, value); }
        private bool _appleMetalSelectable = true;
        public bool AppleMetalSelectable { get => _appleMetalSelectable; set => this.RaiseAndSetIfChanged(ref _appleMetalSelectable, value); }

        private static bool AnyModelFile(string pattern) =>
            Directory.Exists(Path.Combine(DataDir, "onnx")) && Directory.EnumerateFileSystemEntries(Path.Combine(DataDir, "onnx"), pattern).Any();

        public void RefreshComponentAwareness()
        {
            if (OnMac)
            {
                // Apple Silicon: no TensorRT, DirectML, Vulkan, RTX Video, AMF or Smooth Motion,
                // and no component packs. RIFE does not exist in the Apple engines yet.
                TrtSelectable = false; VulkanSelectable = false; OnnxRuntimeSelectable = false;
                MiGraphXSelectable = false; OpenVinoSelectable = false;
                RtxHdrSelectable = false; RtxVsrSelectable = false; AmfSelectable = false; SmoothMotionSelectable = false;
                AppleCoreMlSelectable = AnyModelFile("*.mlpatch");
                AppleMetalSelectable = AnyModelFile("*.ajm");
                AnimeJaNaiConf.BackendAutoFallback = false;
                // Interpolation runs through Core ML with whichever engine upscales.
                RifeMissing = !RifeOnDisk();
                BackendNotice = AnimeJaNaiConf.AppleEngine == "Metal" && !AppleMetalSelectable ? "The Metal model files are missing from this installation."
                    : AnimeJaNaiConf.AppleEngine == "CoreMLNative" && !AppleCoreMlSelectable ? "The Core ML model files are missing from this installation."
                    : "";
                RefreshBackendModelLists();
                return;
            }
            bool trtInstalled = TrtOnDisk();
            bool? nvidia = ComponentManager.GpuNvidia;
            // Windows only tells NVIDIA from the rest; Linux lists every adapter.
            bool? amd = nvidia == true ? false : null, intel = null;
            if (LinuxFeatures)
            {
                var vendors = AnimeJaNai.Platform.PlatformServices.LinuxAdapters().Select(a => a.vendor).ToHashSet();
                nvidia ??= AnimeJaNai.Platform.PlatformServices.SelectAdapter(AnimeJaNai.Platform.PlatformServices.LinuxAdapters()).vendor == "nvidia";
                amd = vendors.Contains("amd");
                intel = vendors.Contains("intel");
            }
            var gpu = GpuOptions.For(WindowsFeatures, nvidia, amd, intel);
            RtxHdrSelectable = gpu.RtxHdr;
            RtxVsrSelectable = gpu.RtxVsr;
            AmfSelectable = gpu.Amf;
            SmoothMotionSelectable = gpu.SmoothMotion && SmoothMotionLayerInstalled();
            if (nvidia is not null) HdrSettings.SelectLegacyDisplay(nvidia == true ? "nvidia" : "amd");
            Engines.GpuName = ComponentManager.GpuName;
            Engines.Refresh();
            bool trtUsable = trtInstalled && nvidia != false;
            TrtSelectable = trtUsable;

            if (LinuxFeatures)
            {
                VulkanSelectable = VulkanOnDisk();
                OnnxRuntimeSelectable = OnnxRuntimeOnDisk() || NativeMiGraphXOnDisk();
                MiGraphXSelectable = OnnxRuntimeSelectable && gpu.MiGraphX;
                OpenVinoSelectable = OnnxRuntimeSelectable && gpu.OpenVino;
                AnimeJaNaiConf.BackendAutoFallback = false;
                if (AnimeJaNaiConf.DirectMlSelected) AnimeJaNaiConf.SetTensorRtSelected();
                BackendNotice = AnimeJaNaiConf.OnnxRuntimeSelected
                    ? (OnnxRuntimeSelectable
                        ? (AnimeJaNaiConf.MiGraphXSelected
                            ? (NativeMiGraphXOnDisk()
                                ? "Native MIGraphX + HIP for AMD: spatial upscaling and RIFE. Requires a compatible ROCm runtime and GPU; restart playback after changing the engine."
                                : "Legacy ONNX Runtime/MIGraphX provider. Install the native AMD engine for the specialized GPU path.")
                            : AnimeJaNai.Platform.BackendCapabilities.OpenVino)
                        : "ONNX Runtime engine not installed: add \"GPU engines\" in Components. Normal playback remains available.")
                    : AnimeJaNaiConf.VulkanSelected
                    ? (VulkanSelectable ? AnimeJaNai.Platform.BackendCapabilities.NcnnVulkan
                        : "Vulkan library missing. Normal playback remains available.")
                    : (trtUsable ? AnimeJaNai.Platform.BackendCapabilities.TensorRt
                        : "TensorRT unavailable. Select Vulkan for AMD or NVIDIA when the Vulkan package is installed.");
                RifeMissing = AnimeJaNaiConf.VulkanSelected ? !VulkanRifeOnDisk() : !RifeOnDisk();
                RefreshBackendModelLists();
                return;
            }

            MiGraphXSelectable = NativeMiGraphXOnDisk() && gpu.MiGraphX;
            if (AnimeJaNaiConf.MiGraphXSelected)
            {
                BackendNotice = MiGraphXSelectable
                    ? "Native MIGraphX + HIP for AMD, with GPU video processing. Requires a compatible AMD driver and runtime."
                    : "The native AMD engine is not installed or this GPU is unavailable. Choose DirectML or install the AMD engine.";
                RifeMissing = !RifeOnDisk();
                RefreshBackendModelLists();
                return;
            }
            string notice = "";
            if (trtUsable && AnimeJaNaiConf != null && AnimeJaNaiConf.DirectMlSelected &&
                AnimeJaNaiConf.BackendAutoFallback)
            {
                // TensorRT is the natural path on NVIDIA: the DirectML selection was
                // only our fallback, so installing TensorRT completes the setup
                AnimeJaNaiConf.BackendAutoFallback = false;
                AnimeJaNaiConf.SetTensorRtSelected();
                notice = "TensorRT is installed — the backend has switched back to TensorRT.";
            }
            else if (!trtUsable)
            {
                // a conf pointing at an unusable backend would only fail at playback;
                // flip it to the engine that ships in every install and say so
                string flipped = "";
                if (AnimeJaNaiConf != null && AnimeJaNaiConf.TensorRtSelected)
                {
                    AnimeJaNaiConf.BackendAutoFallback = true;
                    AnimeJaNaiConf.SetDirectMlSelected();
                    flipped = "Switched to DirectML: ";
                }
                notice = nvidia == false
                    ? flipped + "TensorRT requires an NVIDIA GPU."
                    : ComponentManager.TrtPackAvailable
                        ? flipped + "TensorRT is not installed — install it from the Components tab (recommended for NVIDIA GPUs); the backend switches back automatically once installed."
                        : flipped + "TensorRT is not installed.";
            }
            BackendNotice = notice;
            RifeMissing = !RifeOnDisk();

            // per-chain RIFE toggles derive from RifeMissing; let them re-evaluate
            foreach (var slot in AnimeJaNaiConf?.UpscaleSlots ?? [])
            {
                foreach (var chain in slot.Chains)
                {
                    chain.RefreshPipeline();
                }
            }
        }

        public ComponentManagerViewModel ComponentManager { get; } = new();

        private int _selectedTabIndex;
        public int SelectedTabIndex
        {
            get => _selectedTabIndex;
            set { this.RaiseAndSetIfChanged(ref _selectedTabIndex, value); RefreshNavigation(); }
        }

        // First-run setup: if the hardware's recommended components are missing
        // (fresh slim install, or a GPU change), open on the Components tab and
        // offer to install everything in one click - new users won't read the
        // tab, and 3.3.0 set the bar by shipping preconfigured.
        private async Task InitializeComponentManagerAsync()
        {
            await ComponentManager.RefreshAsync();
            if (!ComponentManager.SetupNeeded)
            {
                return;
            }
            SelectedTabIndex = 2;

            var missing = ComponentManager.MissingPreselected;
            var lines = string.Join("\n",
                missing.Select(p => $"\u2022  {p.Title} \u2014 {p.SizeText}"));
            long totalMb = missing.Sum(p => p.Bytes) / 1048576;
            var dialog = new FluentAvalonia.UI.Controls.FAContentDialog
            {
                Title = "Set up AnimeJaNai",
                Content = $"Components recommended for this PC are not installed:\n\n{lines}\n\nDownload and install them now ({totalMb:N0} MB)?",
                PrimaryButtonText = "Install",
                CloseButtonText = "Not now",
                DefaultButton = FluentAvalonia.UI.Controls.FAContentDialogButton.Primary,
            };
            try
            {
                var result = await dialog.ShowAsync();
                if (result == FluentAvalonia.UI.Controls.FAContentDialogResult.Primary)
                {
                    await ComponentManager.InstallMissingPreselectedAsync();
                }
            }
            catch
            {
                // no dialog host (window closing etc.) - the Components tab with
                // its banner and highlights remains the manual path
            }
        }

        private string[] _commonResolutions = [
            "0x0",
            "640x360",
            "640x480",
            "720x480",
            "768x576",
            "960x540",
            "1024x576",
            "1280x720",
            "1440x1080",
            "1920x1080"];

        public string[] CommonResolutions
        {
            get => _commonResolutions;
            set => this.RaiseAndSetIfChanged(ref _commonResolutions, value);
        }

        public string[] BuilderOptimizationLevels { get; } = ["0", "1", "2", "3", "4", "5"];

        public string ExePath = Path.GetDirectoryName(Process.GetCurrentProcess().MainModule.FileName);

        // Two layouts: at the install root the exe sits next to mpvnet.exe with the data in
        // animejanai/ (3.4.0+); in the legacy layout (and `dotnet run` from the project
        // output, which copies animejanai.conf + onnx/ beside the binary) the exe lives
        // inside the data directory itself.
        public static bool AtInstallRoot { get; } =
            !File.Exists(Path.Combine(AppContext.BaseDirectory, "animejanai.conf")) &&
            File.Exists(Path.Combine(AppContext.BaseDirectory, "animejanai", "animejanai.conf"));

        // animejanai/: conf, onnx models, rife models, benchmarks, backups.
        // ANIMEJANAI_DATA_DIR / ANIMEJANAI_ROOT override the exe-relative layout
        // for the AppImage, where the binary is in a read-only mount but the
        // writable data lives under $XDG_DATA_HOME/AnimeJaNai.
        public static string DataDir { get; } = Path.GetFullPath(
            Environment.GetEnvironmentVariable("ANIMEJANAI_DATA_DIR") is { Length: > 0 } dd ? dd
            : MacUserFolder is { } mac ? Path.Combine(mac, "animejanai")
            : AtInstallRoot ? Path.Combine(AppContext.BaseDirectory, "animejanai")
            : AppContext.BaseDirectory);

        // install root: player, AnimeJaNaiUpdater, portable_config/
        public static string RootDir { get; } = Path.GetFullPath(
            Environment.GetEnvironmentVariable("ANIMEJANAI_ROOT") is { Length: > 0 } rd ? rd
            : MacUserFolder is { } mac ? mac
            : AtInstallRoot ? AppContext.BaseDirectory
            : Path.Combine(AppContext.BaseDirectory, ".."));

        // macOS, Manager opened from its application without the player (which otherwise gives
        // the two folders above in the environment): the folder the player's launcher keeps,
        // ~/Library/Application Support/AnimeJaNai. An application is read-only once installed.
        private static string? MacUserFolder =>
            OperatingSystem.IsMacOS() && AppContext.BaseDirectory.Contains(".app/Contents/MacOS", StringComparison.Ordinal)
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Application Support", "AnimeJaNai")
                : null;

        public string BackupPath => Path.Combine(DataDir, "backups");

        public string MpvConfPath => Path.Combine(RootDir, "portable_config", "mpv.conf");

        public string AnimeJaNaiConfPath => Path.Combine(DataDir, "animejanai.conf");

        public string OnnxPath => Path.Combine(DataDir, "onnx");

        private bool _showGlobalSettings = false; // TODO
        [DataMember]
        public bool ShowGlobalSettings
        {
            get => _showGlobalSettings;
            set => this.RaiseAndSetIfChanged(ref _showGlobalSettings, value);
        }

        private bool _showCustomProfiles = true; // TODO 
        [DataMember]
        public bool ShowCustomProfiles
        {
            get => _showCustomProfiles;
            set
            {
                this.RaiseAndSetIfChanged(ref _showCustomProfiles, value);
                this.RaisePropertyChanged(nameof(CurrentSlot));
            }
        }

        private string _selectedSlotNumber = "1"; // TODO
        [DataMember]
        public string SelectedSlotNumber
        {
            get => _selectedSlotNumber;
            set
            {
                this.RaiseAndSetIfChanged(ref _selectedSlotNumber, value);
                this.RaisePropertyChanged(nameof(CurrentSlot));
            }
        }

        private string? _selectedMpvProfile;
        [DataMember]
        public string? SelectedMpvProfile
        {
            get => _selectedMpvProfile;
            set
            {
                this.RaiseAndSetIfChanged(ref _selectedMpvProfile, value);
                this.RaisePropertyChanged(nameof(MpvConfDetected));
                this.RaisePropertyChanged(nameof(DefaultProfileAction));
            }
        }

        public bool MpvConfDetected => File.Exists(MpvConfPath);


        public void HandleShowGlobalSettings()
        {
            SelectedTabIndex = 3;
        }

        public void HandleShowCustomProfile(string slotNumber)
        {
            ShowGlobalSettings = false;
            ShowCustomProfiles = true;
            SelectedSlotNumber = slotNumber;
            SelectedTabIndex = 0;
            RefreshProfileSelection();
        }

        private AnimeJaNaiConf _animeJaNaiConf;
        public AnimeJaNaiConf AnimeJaNaiConf
        {
            get => _animeJaNaiConf;
            set => this.RaiseAndSetIfChanged(ref _animeJaNaiConf, value);
        }

        public UpscaleSlot CurrentSlot
        {
            get => ShowCustomProfiles ? AnimeJaNaiConf.UpscaleSlots.FirstOrDefault(slot => slot.SlotNumber == SelectedSlotNumber) : null;
        }

        private UpscaleSlot? _selectedProfileToClone;
        public UpscaleSlot? SelectedProfileToClone
        {
            get => _selectedProfileToClone;
            set
            {
                this.RaiseAndSetIfChanged(ref _selectedProfileToClone, value);
                this.RaisePropertyChanged(nameof(CanCloneProfile));
            }
        }

        public bool CanCloneProfile => true; //SelectedProfileToClone != null;

        public void AddChain()
        {
            CurrentSlot.Chains.Add(new UpscaleChain(true) { Vm = this });
            UpdateChainHeaders();
        }

        public void DeleteChain(UpscaleChain chain)
        {
            try
            {
                CurrentSlot.Chains.Remove(chain);
            }
            catch (ArgumentOutOfRangeException)
            {

            }

            UpdateChainHeaders();
        }

        public void UpdateChainHeaders()
        {
            for (var i = 0; i < CurrentSlot.Chains.Count; i++)
            {
                CurrentSlot.Chains[i].ChainNumber = (i + 1).ToString();
            }
        }

        private string _validationText = string.Empty;
        public string ValidationText
        {
            get => _validationText;
            set
            {
                this.RaiseAndSetIfChanged(ref _validationText, value);
                this.RaisePropertyChanged(nameof(LeftStatus));
            }
        }

        public AvaloniaList<UpscaleSlot> AllSlots => new(AnimeJaNaiConf?.UpscaleSlots.Where(x=>x.ShowSlot&&!x.ActiveSlot)??[]);
        public string ProfileMigrationNotice { get; private set; } = "";
        public bool HasProfileMigrationNotice => ProfileMigrationNotice.Length>0;

        private bool _showAdvancedSettings = false;
        [DataMember]
        public bool ShowAdvancedSettings
        {
            get => _showAdvancedSettings;
            set => this.RaiseAndSetIfChanged(ref _showAdvancedSettings, value);
        }

        private bool _showConsole = false;
        public bool ShowConsole
        {
            get => _showConsole;
            set => this.RaiseAndSetIfChanged(ref _showConsole, value);
        }

        private string _inputStatusText = string.Empty;
        public string InputStatusText
        {
            get => _inputStatusText;
            set
            {
                this.RaiseAndSetIfChanged(ref _inputStatusText, value);
                this.RaisePropertyChanged(nameof(LeftStatus));
            }
        }

        public string LeftStatus => !Valid ? ValidationText.Replace("\n", " ") : $"{InputStatusText} selected for upscaling.";

        private bool _valid = false;
        [IgnoreDataMember]
        public bool Valid
        {
            get => _valid;
            set
            {
                this.RaiseAndSetIfChanged(ref _valid, value);
                this.RaisePropertyChanged(nameof(LeftStatus));
            }
        }

        public AvaloniaList<string> GetAllModels()
        {
            // No models folder yet (macOS: the Manager opened before the player ever ran).
            if (!Directory.Exists(OnnxPath)) return new AvaloniaList<string>();
            if (OnMac)
            {
                // NAME.coreml.onnx is a variant of NAME.onnx made for the ONNX Runtime engine, not a
                // model of its own. The two native engines only run the models converted for them.
                string engine = AnimeJaNaiConf?.AppleEngine ?? "CoreML";
                return new AvaloniaList<string>(Directory.GetFiles(OnnxPath, "*.onnx")
                    .Select(filename => Path.GetFileNameWithoutExtension(filename))
                    .Where(name => !name.EndsWith(".coreml", StringComparison.OrdinalIgnoreCase))
                    .Where(name => engine == "Metal" ? File.Exists(Path.Combine(OnnxPath, name + ".ajm"))
                        : engine != "CoreMLNative" || File.Exists(Path.Combine(OnnxPath, name + ".mlpatch")))
                    .Order().ToList());
            }
            return new AvaloniaList<string>(Directory.GetFiles(OnnxPath).Where(filename => Path.GetExtension(filename).Equals(".onnx", StringComparison.CurrentCultureIgnoreCase))
                .Where(filename => AnimeJaNaiConf?.VulkanSelected != true ||
                    (File.Exists(Path.ChangeExtension(filename, ".ncnn.param")) && File.Exists(Path.ChangeExtension(filename, ".ncnn.bin"))))
                .Select(filename => Path.GetFileNameWithoutExtension(filename))
                .Order().ToList());
        }


        public void RefreshBackendModelLists()
        {
            foreach(var profile in AnimeJaNaiConf.UpscaleSlots)
                foreach(var chain in profile.Chains)chain.RefreshPipeline();
        }

        public void Validate()
        {
            Console.WriteLine("OK");
        }

        private static List<string>? _rifeModels = null;

        public static List<string> RifeModels
        {
            get
            {
                if (Instance?.AnimeJaNaiConf?.VulkanSelected == true)
                    return VulkanRifeOnDisk() ? [RifeValueToLabel("46")] : [];
                if (_rifeModels == null)
                {
                    var models = new List<string>();
                    var modelsPath = Path.Combine(DataDir, "rife");

                    if (!Directory.Exists(modelsPath))
                    {
                        return [];
                    }

                    var files = Directory.GetFiles(modelsPath, searchPattern: "*.onnx");

                    foreach (var file in files)
                    {
                        Debug.WriteLine(file);
                        var m = Regex.Match(Path.GetFileName(file), @"rife_v(\d+)\.(\d+)(_lite)?(_ensemble)?.onnx");
                        if (m.Success)
                        {
                            var model = m.Groups[1].Value + m.Groups[2].Value;
                            if (file.Contains("_lite"))
                            {
                                model += "1";
                            }

                            if (!models.Contains(model))
                            {
                                models.Add(model);
                            }
                        }
                    }

                    models.Sort(delegate (string m1, string m2)
                    {
                        var m1i = decimal.Parse(m1[..Math.Min(3, m1.Length)]);
                        var m2i = decimal.Parse(m2[..Math.Min(3, m2.Length)]);

                        if (m1.Length > 3)
                        {
                            m1i += .1m;
                        }

                        if (m2.Length > 3)
                        {
                            m2i += .1m;
                        }

                        return m2i.CompareTo(m1i);
                    });

                    _rifeModels = [.. models.Select(m => RifeValueToLabel(m))];
                }

                return _rifeModels;
            }
        }

        public static string RifeLabelToValue(string rifeLabel)
        {
            if (string.IsNullOrEmpty(rifeLabel))
            {
                return rifeLabel;
            }

            var m = Regex.Match(rifeLabel, @"RIFE (\d+)\.(\d+)( Lite)?");
            if (m.Success)
            {
                var value = $"{m.Groups[1].Value}{m.Groups[2].Value}";
                if (m.Groups[3].Success)
                {
                    value += "1";
                }
                return value;
            }

            throw new ArgumentException(rifeLabel);
        }

        public static string RifeValueToLabel(string rifeValue)
        {
            string dec;

            if (string.IsNullOrEmpty(rifeValue))
            {
                return rifeValue;
            }

            if (rifeValue.Length == 2)
            {
                dec = rifeValue[1].ToString();
            }
            else
            {
                dec = rifeValue.Substring(1, 2);
            }

            var modelName = $"RIFE {rifeValue[0]}.{dec}";

            if (rifeValue.Length >= 4 && rifeValue.EndsWith('1'))
            {
                modelName += " Lite";
            }

            return modelName;
        }

        public AnimeJaNaiConf ReadAnimeJaNaiConf(string fullPath, bool autoSave = false)
        {
            return ReadAnimeJaNaiConf(new ConfigParser(fullPath), autoSave);
        }

        // Keep CONFIG_VERSION, the current default, and the historical-defaults set in sync with
        // aji_conf.cpp in the animejanai-inference repo (the native filter's conf loader).
        private const int CONFIG_VERSION = 3;
        // [global] sub_render_mode value that selects the experimental GPU subtitle path. The
        // settings themselves live in the [subs-gpu] profile of the managed mpv-animejanai.conf,
        // so they can change with the player without a Manager release.
        private const string SUB_RENDER_MODE_GPU = "gpu";
        // TRT 11: strongly-typed is the default (--stronglyTyped is a no-op), and the native filter's
        // sanitizer strips --inputIOFormats/--outputIOFormats/--tacticSources anyway (types come from
        // the network; the cuDNN/cuBLAS tactic sources are gone). Only the builder optimization level,
        // build shape, and skip-inference are worth carrying.
        private const string DEFAULT_TRT_ENGINE_SETTINGS =
            "--builderOptimizationLevel=5 --optShapes=input:%video_resolution% --skipInference";
        private static readonly HashSet<string> LEGACY_DEFAULT_TRT_ENGINE_SETTINGS = new()
        {
            DEFAULT_TRT_ENGINE_SETTINGS,
            // pre-trim default (TRT 8/10-era flags); drop it from old confs so the current default applies
            "--stronglyTyped --optShapes=input:%video_resolution% --inputIOFormats=fp16:chw --outputIOFormats=fp16:chw --builderOptimizationLevel=5 --tacticSources=-CUDNN,-CUBLAS,-CUBLAS_LT --skipInference",
        };

        // Hand-edited confs can leave numeric keys empty; the native filter reads
        // empty as 0, but a non-numeric string must never reach a NumberBox binding
        // (InvalidCastException converting '' to double).
        private static string NumericOr(ConfigParser parser, string section, string key, string fallback = "0")
        {
            var value = parser.GetValue(section, key);
            return string.IsNullOrWhiteSpace(value) ||
                   !double.TryParse(value, System.Globalization.NumberStyles.Float, ENGLISH_CULTURE, out _)
                ? fallback : value;
        }

        public AnimeJaNaiConf ReadAnimeJaNaiConf(ConfigParser parser, bool autoSave = false)
        {
            _profileReadDepth++;
            using var reading = System.Reactive.Disposables.Disposable.Create(() => _profileReadDepth--);
            var animeJaNaiConf = new AnimeJaNaiConf(autoSave) { Vm = this };
            var slots = new Dictionary<string, UpscaleSlot>();

            animeJaNaiConf.EnableLogging = ParseBool(parser.GetValue("global", "logging", "no"));
            animeJaNaiConf.AutoDisplayRefresh = ParseBool(parser.GetValue("global", "display_rate_match", "no"));
            animeJaNaiConf.BackendAutoFallback = ParseBool(parser.GetValue("global", "backend_auto_fallback", "no"));
            if (Enum.TryParse(parser.GetValue("global", "backend", "TensorRT"), out Backend backend))
            {
                switch (backend)
                {
                    case Backend.DirectML:
                    case Backend.NCNN: // retired; the inference shim treats NCNN as DirectML
                        animeJaNaiConf.SetDirectMlSelected();
                        break;
                    case Backend.Vulkan:
                        animeJaNaiConf.SetVulkanSelected();
                        break;
                    case Backend.MIGraphX:
                        animeJaNaiConf.SetOnnxRuntimeSelected(false);
                        break;
                    case Backend.OpenVINO:
                        animeJaNaiConf.SetOnnxRuntimeSelected(true);
                        break;
                    case Backend.TensorRT:
                    default:
                        animeJaNaiConf.SetTensorRtSelected();
                        break;
                }
            }

            // Apple Silicon engine settings. Unknown values fall back to the defaults.
            animeJaNaiConf.SetAppleEngine(parser.GetValue("global", "backend", "CoreML"));
            animeJaNaiConf.SetAppleDevice(parser.GetValue("global", "apple_device", "AUTO"));
            animeJaNaiConf.SetEngineReuse(parser.GetValue("global", "engine_reuse", "frame"));
            animeJaNaiConf.AppleAutoProfile = ParseBool(parser.GetValue("global", "apple_auto_profile", "yes"));
            animeJaNaiConf.SetRifeDevice(parser.GetValue("global", "engine_rife_device", "auto"));
            animeJaNaiConf.EngineBoost = ParseBool(parser.GetValue("global", "engine_boost", "no"));

            int.TryParse(parser.GetValue("global","default_slot","0"),out int defaultSlot);
            if(defaultSlot>=1000)ProfileMigrationNotice="The three old built-in profiles were removed. Startup is Off; your custom profiles are preserved.";
            animeJaNaiConf.DefaultSlot=defaultSlot is >=0 and <=9?defaultSlot:0;

            animeJaNaiConf.SetRifeEngine(parser.GetValue("global", "rife_engine", "fast"));
            animeJaNaiConf.RifeDrawings = !parser.GetValue("global", "rife_drawings", "yes").Trim()
                .Equals("no", StringComparison.OrdinalIgnoreCase);

            // Subtitle rendering mode: "gpu" opts into the experimental GPU subtitle path
            // (the [subs-gpu] profile in mpv-animejanai.conf, applied at startup by
            // scripts/animejanai_backend.lua). Anything else (including the absent key)
            // means the stable defaults baked into the [animejanai] profile.
            animeJaNaiConf.GpuSubtitles = SUB_RENDER_MODE_GPU.Equals(
                parser.GetValue("global", "sub_render_mode", "").Trim(),
                StringComparison.OrdinalIgnoreCase);

            // config_version drives migrations (absent => 1, the pre-versioning schema).
            int.TryParse(parser.GetValue("global", "config_version", "1"), out var configVersion);
            var trtEngineSettings = parser.GetValue("global", "trt_engine_settings", "");
            // v1 -> v2: drop a trt_engine_settings value that is just an old shipped default so the
            // current default applies; a genuinely customized value is kept.
            if (configVersion < 2 && LEGACY_DEFAULT_TRT_ENGINE_SETTINGS.Contains(trtEngineSettings))
            {
                trtEngineSettings = "";
            }
            // v2 -> v3: TRT 11 strongly-typed ONNX dropped --fp16/--bf16 (the backend strips them and
            // the precision selector was removed); rewrite either to --stronglyTyped so a migrated
            // custom value stays valid, collapsing a duplicate if both were present.
            if (configVersion < 3 && Regex.IsMatch(trtEngineSettings, @"--(fp16|bf16)\b"))
            {
                trtEngineSettings = Regex.Replace(trtEngineSettings, @"--(fp16|bf16)\b", "--stronglyTyped");
                trtEngineSettings = Regex.Replace(trtEngineSettings, @"--stronglyTyped(\s+--stronglyTyped)+", "--stronglyTyped");
            }
            animeJaNaiConf.TrtEngineSettings = string.IsNullOrEmpty(trtEngineSettings)
                ? DEFAULT_TRT_ENGINE_SETTINGS
                : trtEngineSettings;

            foreach(var section in parser.Sections)
            {
                var match=Regex.Match(section.SectionName,@"^slot_([1-9][0-9]*)$");if(!match.Success)continue;
                string number=match.Groups[1].Value;
                var chains=ReadChains(parser,section.SectionName,autoSave,OnMac?animeJaNaiConf.AppleEngine:animeJaNaiConf.SelectedBackend.ToString(),animeJaNaiConf.RifeEngine);
                slots[number]=new UpscaleSlot(autoSave){Vm=this,SlotNumber=number,MpvProfileName=$"upscale-on-{number}",
                    ProfileName=parser.GetValue(section.SectionName,"profile_name","New Profile"),
                    HdrStageControlled=ParseBool(parser.GetValue(section.SectionName,"hdr_stage_controlled","no"))||chains.Any(c=>c.Stages.Any(s=>s.Kind=="hdr")),Chains=chains};
            }

            animeJaNaiConf.UpscaleSlots = new AvaloniaList<UpscaleSlot>(slots.Values);
            if(HdrSettings!=null&&OptionalSdks!=null)foreach(var profile in animeJaNaiConf.UpscaleSlots)MigrateLegacyHdr(profile);

            return animeJaNaiConf;
        }

        private void MigrateLegacyHdr(UpscaleSlot profile)
        {
            if(profile.HdrStageControlled||profile.Chains.Any(c=>!c.PipelineEditable))return;
            profile.HdrStageControlled=true;
            if(HdrSettings.ConversionInProfiles||!HdrSettings.HdrConversion||profile.ProfileName=="New Profile"||profile.Chains.Count==0)return;
            bool nvidia=HdrSettings.Backend=="rtx"||HdrSettings.Backend=="auto"&&_expectedBank.Contains("nvidia");
            string method=nvidia&&(OnWindows||OnLinux&&OptionalSdks.TrueHdrReady)?"nvidia-truehdr":"mpv";
            string runtime=method=="mpv"?"gpu-next":OnWindows?"rtx-video":"cuda";
            var options=new Dictionary<string,string>{{"contrast",(HdrSettings.RtxContrast??100).ToString(ENGLISH_CULTURE)},
                {"saturation",(HdrSettings.RtxSaturation??100).ToString(ENGLISH_CULTURE)},{"middle_gray",(HdrSettings.RtxMiddleGray??50).ToString(ENGLISH_CULTURE)}};
            if(method!="mpv")options["peak"]=Math.Clamp(HdrSettings.Display.Peak??1000,400,2000).ToString(ENGLISH_CULTURE);
            if(method=="mpv") {
                options["mpv_truehdr_look"]=HdrSettings.MpvTrueHdrLook?"yes":"no";
                options["paper_white"]=(HdrSettings.SdrWhite??203).ToString(ENGLISH_CULTURE);
                options["highlights"]=(HdrSettings.HighlightsMpv??50).ToString(ENGLISH_CULTURE);
                options["vibrance"]=(HdrSettings.VibranceMpv??15).ToString(ENGLISH_CULTURE);
            } else if(OnLinux)options["sdk_root"]=OptionalSdks.TrueHdrRoot;
            profile.Chains[0].Stages.Add(new InterpolationStage(profile.Chains[0],new("migrated-hdr","hdr",method,runtime,true,"",new(1,1),"quality",options)));
        }

        private AvaloniaList<UpscaleChain> ReadChains(ConfigParser parser,string section,bool autoSave,string backend,string policy)
        {
            var values=parser.Sections.First(s=>s.SectionName==section).Keys.ToDictionary(k=>k.Name,k=>parser.GetValue(section,k.Name));
            var numbers=values.Keys.Select(k=>Regex.Match(k,@"^chain_([1-9][0-9]*)_")).Where(m=>m.Success)
                .Select(m=>int.Parse(m.Groups[1].Value)).Distinct().Order();
            var chains=new AvaloniaList<UpscaleChain>();
            foreach(int n in numbers)
            {
                var chain=new UpscaleChain(autoSave){Vm=this,ChainNumber=n.ToString(),
                    MinResolution=parser.GetValue(section,$"chain_{n}_min_resolution","0x0"),
                    MaxResolution=parser.GetValue(section,$"chain_{n}_max_resolution","0x0"),
                    MinFps=NumericOr(parser,section,$"chain_{n}_min_fps"),MaxFps=NumericOr(parser,section,$"chain_{n}_max_fps")};
                chain.LoadPipeline(values,backend,policy);chains.Add(chain);
            }
            return chains;
        }

        public void ReadAnimeJaNaiConfToCurrentSlot(string fullPath, bool autoSave = false)
        {
            ReadAnimeJaNaiConfToCurrentSlot(new ConfigParser(fullPath), autoSave);
        }

        public void ReadAnimeJaNaiConfToCurrentSlot(ConfigParser parser, bool autoSave = false)
        {
            _profileReadDepth++;
            using var reading=System.Reactive.Disposables.Disposable.Create(()=>_profileReadDepth--);
            if(parser.Sections.Count()!=1||parser.Sections.First().SectionName!="slot")throw new InvalidDataException("Expected a single exported profile section.");
            var chains=ReadChains(parser,"slot",autoSave,OnMac?AnimeJaNaiConf.AppleEngine:AnimeJaNaiConf.SelectedBackend.ToString(),AnimeJaNaiConf.RifeEngine);
            CurrentSlot.ProfileName=parser.GetValue("slot","profile_name","New Profile");CurrentSlot.HdrStageControlled=ParseBool(parser.GetValue("slot","hdr_stage_controlled","no"))||chains.Any(c=>c.Stages.Any(s=>s.Kind=="hdr"));CurrentSlot.Chains=chains;MigrateLegacyHdr(CurrentSlot);
            reading.Dispose();if(autoSave)WriteAnimeJaNaiConf();
        }

        static bool ParseBool(string value)
        {
            return value?.ToLower() == "yes";
        }

        public void WriteAnimeJaNaiConf()
        {
            if (_profileReadDepth>0 || AnimeJaNaiConf?.UpscaleSlots.Any(s => s.Chains.Any(c => c.PipelineEditing)) == true) return;
            foreach (var slot in AnimeJaNaiConf?.UpscaleSlots ?? [])
                foreach (var chain in slot.Chains) chain.RefreshPipeline();
            try { WriteAnimeJaNaiConf(AnimeJaNaiConfPath); }
            catch (Exception e) when (e is IOException or InvalidDataException) { BackendNotice = e.Message; }
        }

        public void WriteAnimeJaNaiConf(string fullPath)
        {
            var parser = ParsedAnimeJaNaiConf(AnimeJaNaiConf);
            if (parser is null) return;
            lock (_profileSaveLock)
            {
                var temp = fullPath + ".manager-" + Guid.NewGuid().ToString("N");
                try
                {
                    parser.Save(temp);
                    var text = IniPreservingSave.Merge(File.Exists(fullPath) ? File.ReadAllText(fullPath) : "", File.ReadAllText(temp));
                    if (Path.GetFullPath(fullPath) == Path.GetFullPath(AnimeJaNaiConfPath) &&
                        Path.GetFullPath(DataDir) == Path.GetFullPath(Path.Combine(RootDir, "animejanai")))
                    {
                        AnimeJaNai.Platform.PlatformServices.SaveActiveConfig(RootDir, _expectedHash, System.Text.Encoding.UTF8.GetBytes(text), _expectedBank);
                        _expectedHash = AnimeJaNai.Platform.PlatformServices.FileHash(fullPath);
                        HdrSettings.NotifyPlayer();
                    }
                    else File.WriteAllText(fullPath, text);
                    if(Path.GetFullPath(fullPath)==Path.GetFullPath(AnimeJaNaiConfPath)&&AnimeJaNaiConf.UpscaleSlots.All(p=>p.HdrStageControlled&&p.Chains.All(c=>c.PipelineEditable)))
                        HdrSettings.RetireLegacyConversion();
                }
                finally { if (File.Exists(temp)) File.Delete(temp); }
            }
        }

        public static ConfigParser? ParsedAnimeJaNaiConf(AnimeJaNaiConf conf)
        {
            if (conf is null)
            {
                return null;
            }

            foreach(var profile in conf.UpscaleSlots)
                if(profile.Chains.Sum(c=>c.Stages.Count(s=>s.Kind=="hdr"))>1)
                    throw new InvalidDataException("Only one HDR conversion stage is allowed per profile, across all its chains.");
            var parser = new ConfigParser();

            parser.SetValue("global", "config_version", CONFIG_VERSION.ToString());
            parser.SetValue("global", "backend", OnMac ? conf.AppleEngine : conf.SelectedBackend.ToString());
            parser.SetValue("global", "backend_auto_fallback", conf.BackendAutoFallback ? "yes" : "no");
            if (OnMac)
            {
                // Read by the player's script (device, automatic profile) and by the engine (reuse).
                parser.SetValue("global", "apple_device", conf.AppleDevice);
                parser.SetValue("global", "engine_reuse", conf.EngineReuse);
                parser.SetValue("global", "apple_auto_profile", conf.AppleAutoProfile ? "yes" : "no");
                parser.SetValue("global", "engine_rife_device", conf.RifeDevice);
                parser.SetValue("global", "engine_boost", conf.EngineBoost ? "yes" : "no");
            }
            parser.SetValue("global", "logging", conf.EnableLogging ? "yes" : "no");
            parser.SetValue("global", "rife_engine", conf.RifeEngine);
            parser.SetValue("global", "rife_drawings", conf.RifeDrawings ? "yes" : "no");
            // Startup is Off until a custom profile is explicitly selected.
            if (conf.DefaultSlot is int defaultSlot)
            {
                parser.SetValue("global", "default_slot", defaultSlot.ToString(ENGLISH_CULTURE));
            }
            // Write-minimal: only persist the subtitle mode when the experimental GPU path is
            // enabled (absent => the stable [animejanai] subtitle defaults apply).
            if (conf.GpuSubtitles)
            {
                parser.SetValue("global", "sub_render_mode", SUB_RENDER_MODE_GPU);
            }
            if (conf.AutoDisplayRefresh)
            {
                parser.SetValue("global", "display_rate_match", "yes");
            }
            // Write-minimal: only persist trt_engine_settings when it differs from the current
            // default, so future default changes apply automatically to users who didn't customize.
            if (conf.TrtEngineSettings != DEFAULT_TRT_ENGINE_SETTINGS)
            {
                parser.SetValue("global", "trt_engine_settings", conf.TrtEngineSettings);
            }
            foreach (var profile in conf.UpscaleSlots)
            {
                var section = $"slot_{profile.SlotNumber}";
                parser.SetValue(section, "profile_name", profile.ProfileName);
                if(profile.HdrStageControlled)parser.SetValue(section,"hdr_stage_controlled","yes");

                foreach (var chain in profile.Chains)
                {
                    parser.SetValue(section, $"chain_{chain.ChainNumber}_min_resolution", string.Create(ENGLISH_CULTURE, $"{chain.MinResolution}"));
                    parser.SetValue(section, $"chain_{chain.ChainNumber}_max_resolution", string.Create(ENGLISH_CULTURE, $"{chain.MaxResolution}"));
                    parser.SetValue(section, $"chain_{chain.ChainNumber}_min_fps", string.Create(ENGLISH_CULTURE, $"{chain.MinFps}"));
                    parser.SetValue(section, $"chain_{chain.ChainNumber}_max_fps", string.Create(ENGLISH_CULTURE, $"{chain.MaxFps}"));

                    chain.WritePipeline(parser, section);
                }
            }

            return parser;
        }

        public void WriteAnimeJaNaiCurrentProfileConf(string fullPath)
        {
            var parser = ParsedAnimeJaNaiProfileConf(CurrentSlot);
            parser.Save(fullPath);
        }

        public ConfigParser ParsedAnimeJaNaiProfileConf(UpscaleSlot slot)
        {
            if(slot.Chains.Sum(c=>c.Stages.Count(s=>s.Kind=="hdr"))>1)throw new InvalidDataException("Only one HDR conversion stage is allowed per profile, across all its chains.");
            var parser = new ConfigParser();
            var section = "slot";

            parser.SetValue(section, "profile_name", slot.ProfileName);
            if(slot.HdrStageControlled)parser.SetValue(section,"hdr_stage_controlled","yes");
            foreach (var chain in slot.Chains)
            {
                parser.SetValue(section, $"chain_{chain.ChainNumber}_min_resolution", string.Create(ENGLISH_CULTURE, $"{chain.MinResolution}"));
                parser.SetValue(section, $"chain_{chain.ChainNumber}_max_resolution", string.Create(ENGLISH_CULTURE, $"{chain.MaxResolution}"));
                parser.SetValue(section, $"chain_{chain.ChainNumber}_min_fps", string.Create(ENGLISH_CULTURE, $"{chain.MinFps}"));
                parser.SetValue(section, $"chain_{chain.ChainNumber}_max_fps", string.Create(ENGLISH_CULTURE, $"{chain.MaxFps}"));

                chain.WritePipeline(parser, section);
            }

            return parser;
        }

        public void CheckAndDoBackup()
        {
            Task.Run(() =>
            {
                if (!Path.Exists(BackupPath))
                {
                    Directory.CreateDirectory(BackupPath);
                }

                var files = Directory.EnumerateFiles(BackupPath)
                            .Where(f => Path.GetFileName(f).StartsWith("autobackup_"))
                            .OrderByDescending(f => f)
                            .ToList();

                var currentConfStr = ParsedAnimeJaNaiConf(AnimeJaNaiConf)?.ToString();

                if (files.Count >= 10)
                {
                    // Delete oldest backup
                    File.Delete(files.Last());
                }

                if (files.Count > 0)
                {
                    var backupConf = ReadAnimeJaNaiConf(files.First());
                    if (currentConfStr == ParsedAnimeJaNaiConf(backupConf)?.ToString())
                    {
                        // Backup already exists - no need to create another backup
                        return;
                    }
                }

                WriteAnimeJaNaiConf(Path.Join(BackupPath, $"autobackup_{DateTime.Now:yyyyMMdd-HHmmss}.conf"));
            });
        }

#pragma warning disable CA1822 // Mark members as static
        public async void LaunchBenchmark()
#pragma warning restore CA1822 // Mark members as static
        {
            await Task.Run(async () =>
            {
                using var process = new Process();

                if (OperatingSystem.IsWindows())
                {
                    process.StartInfo.FileName = OperatingSystem.IsWindows() ? "cmd.exe" : "sh";
                    process.StartInfo.Arguments = @"/C .\benchmarks\animejanai_benchmark_all.bat";
                }
                else
                {
                    // Linux: the same benchmark run via the cross-platform .NET tool.
                    process.StartInfo.FileName = "/bin/sh";
                    process.StartInfo.Arguments = "./benchmarks/animejanai_benchmark_all.sh";
                }

                process.StartInfo.RedirectStandardOutput = false;
                process.StartInfo.RedirectStandardError = false;
                process.StartInfo.UseShellExecute = false;
                process.StartInfo.CreateNoWindow = false;
                process.StartInfo.WorkingDirectory = DataDir;

                process.Start();
                await process.WaitForExitAsync();
            });
        }

#pragma warning disable CA1822 // Mark members as static
        public async void OpenModelsDirectory()
#pragma warning restore CA1822 // Mark members as static
        {
            await Task.Run(() =>
            {
                Process.Start(new ProcessStartInfo(OperatingSystem.IsWindows() ? "explorer.exe" : OperatingSystem.IsMacOS() ? "open" : "xdg-open") { ArgumentList = { OnnxPath }, UseShellExecute = false });
            });
        }

        public void SelectDefaultProfile()
        {
            if (SelectedMpvProfile == CurrentSlot.MpvProfileName)
            {
                // Toggling off the current default leaves the player upscaling-off by default.
                SelectedMpvProfile = null;
                WriteDefaultProfile(true);
            }
            else
            {
                SelectedMpvProfile = CurrentSlot.MpvProfileName;
                WriteDefaultProfile();
            }
        }

        // The default profile is stored as [global] default_slot in animejanai.conf and applied by
        // scripts/animejanai_backend.lua at startup. These map the Manager's mpv profile names to the
        // native filter slot numbers the player switches between: Quality/Balanced/Performance are the
        // built-in slots 1001/1002/1003, custom profiles use their own number 1-9, and Off is slot 0.
        private static int? MpvProfileToSlot(string? mpvProfile)
        {
            if (string.IsNullOrEmpty(mpvProfile))
            {
                return null;
            }
            switch (mpvProfile)
            {
                case "upscale-off": return 0;



            }
            var m = Regex.Match(mpvProfile, @"^upscale-on-(\d+)$");
            return m.Success ? int.Parse(m.Groups[1].Value, ENGLISH_CULTURE) : (int?)null;
        }

        private static string? SlotToMpvProfile(int? slot)
        {
            switch (slot)
            {
                case null: return null;
                case 0: return "upscale-off";



            }
            return slot.Value is >= 1 and <= 9 ? $"upscale-on-{slot.Value}" : null;
        }

        private string? ReadCurrentProfileFromMpvConf() => SlotToMpvProfile(AnimeJaNaiConf?.DefaultSlot);

        private void WriteDefaultProfile(bool upscaleOff = false)
        {
            if (LinuxFeatures && !(AnimeJaNaiConf.OnnxRuntimeSelected ? OnnxRuntimeSelectable : AnimeJaNaiConf.VulkanSelected ? VulkanSelectable : TrtSelectable) && !upscaleOff) { BackendNotice = "The selected inference backend is unavailable. Normal playback remains available."; return; }
            var profile = upscaleOff ? "upscale-off" : CurrentSlot.MpvProfileName;
            AnimeJaNaiConf.DefaultSlot = MpvProfileToSlot(profile);
            WriteAnimeJaNaiConf();
        }

        private void InitializeSelectedSlot()
        {
            if (SelectedMpvProfile == null || SelectedMpvProfile == "upscale-off")
            {
                return;
            }

            var customSlot = AnimeJaNaiConf.UpscaleSlots.Where(x => x.MpvProfileName == SelectedMpvProfile).FirstOrDefault();

            if (customSlot != null)
            {
                HandleShowCustomProfile(customSlot.SlotNumber);
            }
        }
    }

    [DataContract]
    public class AnimeJaNaiConf : ReactiveObject
    {
        public AnimeJaNaiConf(bool autoSave = false)
        {
            Debug.WriteLine($"autoSave? {autoSave}");
            if (autoSave)
            {
                this.WhenAnyValue(
                    x => x.EnableLogging,
                    x => x.TensorRtSelected,
                    x => x.DirectMlSelected,
                    x => x.VulkanSelected,
                    x => x.BackendAutoFallback,
                    x => x.TrtEngineSettings).Subscribe(x =>
                    {
                        Vm?.WriteAnimeJaNaiConf();
                    });

                // The ONNX Runtime engines, in their own subscription (same tuple size limit).
                this.WhenAnyValue(
                    x => x.MiGraphXSelected,
                    x => x.OpenVinoSelected).Subscribe(x =>
                    {
                        Vm?.WriteAnimeJaNaiConf();
                    });

                // Apple Silicon engine settings.
                this.WhenAnyValue(
                    x => x.AppleEngine,
                    x => x.AppleDevice,
                    x => x.EngineReuse,
                    x => x.AppleAutoProfile,
                    x => x.RifeDevice,
                    x => x.EngineBoost).Subscribe(x =>
                    {
                        Vm?.WriteAnimeJaNaiConf();
                    });

                // Separate subscription: WhenAnyValue's tuple overload doesn't extend to this many
                // properties in one call.
                this.WhenAnyValue(
                    x => x.GpuSubtitles,
                    x => x.RifeEngine,
                    x => x.AutoDisplayRefresh).Subscribe(x =>
                    {
                        Vm?.WriteAnimeJaNaiConf();
                    });
                this.WhenAnyValue(x => x.RifeDrawings).Subscribe(x =>
                {
                    Vm?.WriteAnimeJaNaiConf();
                });

            }
        }

        private MainWindowViewModel? _vm;
        public MainWindowViewModel? Vm
        {
            get => _vm;
            set => this.RaiseAndSetIfChanged(ref _vm, value);
        }

        private bool _enableLogging = false;
        [DataMember]
        public bool EnableLogging
        {
            get => _enableLogging;
            set => this.RaiseAndSetIfChanged(ref _enableLogging, value);
        }

        // The slot the player loads by default at startup (Quality/Balanced/Performance = 1001/1002/
        // 1003, custom profiles = 1-9, Off = 0). null means no default is stored, so the slot baked
        // into the vf line (Balanced) applies. Persisted as [global] default_slot in animejanai.conf
        // and applied by scripts/animejanai_backend.lua. Not auto-saved here; the Profiles tab writes
        // it explicitly via SelectDefaultProfile.
        private int? _defaultSlot;
        public int? DefaultSlot
        {
            get => _defaultSlot;
            set => this.RaiseAndSetIfChanged(ref _defaultSlot, value);
        }

        private bool _tensorRtSelected = true;
        [DataMember]
        public bool TensorRtSelected
        {
            get => _tensorRtSelected;
            set
            {
                this.RaiseAndSetIfChanged(ref _tensorRtSelected, value);
            }
        }

        private bool _directMlSelected = false;
        [DataMember]
        public bool DirectMlSelected
        {
            get => _directMlSelected;
            set
            {
                this.RaiseAndSetIfChanged(ref _directMlSelected, value);
            }
        }

        private bool _vulkanSelected;
        [DataMember]
        public bool VulkanSelected
        {
            get => _vulkanSelected;
            set => this.RaiseAndSetIfChanged(ref _vulkanSelected, value);
        }

        // ONNX Runtime engine (Linux): MIGraphX for AMD on ROCm, OpenVINO for Intel.
        private bool _miGraphXSelected;
        [DataMember]
        public bool MiGraphXSelected
        {
            get => _miGraphXSelected;
            set => this.RaiseAndSetIfChanged(ref _miGraphXSelected, value);
        }

        private bool _openVinoSelected;
        [DataMember]
        public bool OpenVinoSelected
        {
            get => _openVinoSelected;
            set => this.RaiseAndSetIfChanged(ref _openVinoSelected, value);
        }

        public bool OnnxRuntimeSelected => MiGraphXSelected || OpenVinoSelected;

        // Per-profile Standard vs Sharp model selection for the three built-in default profiles.
        // Persisted as [global] quality_preset / balanced_preset / performance_preset and honored at
        // playback by animejanai_config.py (slots 1001/1002/1003). false => standard, true => sharp.


        private string _trtEngineSettings = "--builderOptimizationLevel=5 --optShapes=input:%video_resolution% --skipInference";
        [DataMember]
        public string TrtEngineSettings
        {
            get => _trtEngineSettings;
            set
            {
                this.RaiseAndSetIfChanged(ref _trtEngineSettings, value);
                this.RaisePropertyChanged(nameof(TrtStaticOnnxSelected));
                this.RaisePropertyChanged(nameof(TrtStaticSelected));
                this.RaisePropertyChanged(nameof(TrtDynamicSelected));
                this.RaisePropertyChanged(nameof(TrtDynamicMinResolution));
                this.RaisePropertyChanged(nameof(TrtDynamicOptResolution));
                this.RaisePropertyChanged(nameof(TrtDynamicMaxResolution));
                this.RaisePropertyChanged(nameof(TrtBuilderOptimizationLevel));
            }
        }

        public bool TrtDynamicSelected => TrtEngineSettings.Contains("--minShapes=");
        public bool TrtStaticSelected => TrtEngineSettings.Contains("--optShapes=") && !TrtDynamicSelected;
        public bool TrtStaticOnnxSelected => !TrtEngineSettings.Contains("--optShapes=") && !TrtDynamicSelected;

        private static string ShapeToResolution(string shapeArg)
        {
            var match = Regex.Match(shapeArg, @"input:1x3x(\d+)x(\d+)");
            if (match.Success)
                return $"{match.Groups[2].Value}x{match.Groups[1].Value}";
            return "0x0";
        }

        private static string ResolutionToShape(string resolution)
        {
            var match = Regex.Match(resolution, @"(\d+)x(\d+)");
            if (match.Success)
                return $"1x3x{match.Groups[2].Value}x{match.Groups[1].Value}";
            return "1x3x0x0";
        }

        private static string? ExtractShapeValue(string settings, string prefix)
        {
            var match = Regex.Match(settings, prefix + @"=(\S+)");
            return match.Success ? match.Groups[1].Value : null;
        }

        public string TrtDynamicMinResolution
        {
            get
            {
                var val = ExtractShapeValue(TrtEngineSettings, "--minShapes");
                return val != null ? ShapeToResolution(val) : "8x8";
            }
            set
            {
                var shape = ResolutionToShape(value);
                TrtEngineSettings = Regex.Replace(TrtEngineSettings, @"--minShapes=\S+", $"--minShapes=input:{shape}");
            }
        }

        public string TrtDynamicOptResolution
        {
            get
            {
                if (!TrtDynamicSelected) return "1920x1080";
                var val = ExtractShapeValue(TrtEngineSettings, "--optShapes");
                return val != null ? ShapeToResolution(val) : "1920x1080";
            }
            set
            {
                var shape = ResolutionToShape(value);
                if (TrtDynamicSelected)
                    TrtEngineSettings = Regex.Replace(TrtEngineSettings, @"--optShapes=\S+", $"--optShapes=input:{shape}");
            }
        }

        public string TrtDynamicMaxResolution
        {
            get
            {
                var val = ExtractShapeValue(TrtEngineSettings, "--maxShapes");
                return val != null ? ShapeToResolution(val) : "1920x1080";
            }
            set
            {
                var shape = ResolutionToShape(value);
                TrtEngineSettings = Regex.Replace(TrtEngineSettings, @"--maxShapes=\S+", $"--maxShapes=input:{shape}");
            }
        }

        public string TrtBuilderOptimizationLevel
        {
            get
            {
                var match = Regex.Match(TrtEngineSettings, @"--builderOptimizationLevel=(\d+)");
                return match.Success ? match.Groups[1].Value : "5";
            }
            set
            {
                var match = Regex.Match(value ?? "", @"\d+");
                var level = match.Success ? match.Value : "5";
                if (Regex.IsMatch(TrtEngineSettings, @"--builderOptimizationLevel=\d+"))
                    TrtEngineSettings = Regex.Replace(TrtEngineSettings, @"--builderOptimizationLevel=\d+", $"--builderOptimizationLevel={level}");
                else
                    TrtEngineSettings = InsertBeforeTactics(TrtEngineSettings, $"--builderOptimizationLevel={level}");
            }
        }

        private static string RemoveShapeArgs(string settings)
        {
            var result = Regex.Replace(settings, @"\s*--(?:min|opt|max)Shapes=\S+", "");
            return Regex.Replace(result, @"\s+", " ").Trim();
        }

        private static string InsertBeforeTactics(string settings, string toInsert)
        {
            var idx = settings.IndexOf("--tacticSources");
            if (idx >= 0)
                return settings.Insert(idx, toInsert + " ");
            return settings + " " + toInsert;
        }

        public void SetTrtStaticOnnx()
        {
            TrtEngineSettings = RemoveShapeArgs(TrtEngineSettings);
        }

        public void SetTrtStatic()
        {
            var settings = RemoveShapeArgs(TrtEngineSettings);
            TrtEngineSettings = InsertBeforeTactics(settings, "--optShapes=input:%video_resolution%");
        }

        public void SetTrtDynamic()
        {
            var settings = RemoveShapeArgs(TrtEngineSettings);
            TrtEngineSettings = InsertBeforeTactics(settings, "--minShapes=input:1x3x8x8 --optShapes=input:1x3x1080x1920 --maxShapes=input:1x3x1080x1920");
        }

        private AvaloniaList<UpscaleSlot> _upscaleSlots = [];
        [DataMember]
        public AvaloniaList<UpscaleSlot> UpscaleSlots
        {
            get => _upscaleSlots;
            set
            {
                this.RaiseAndSetIfChanged(ref _upscaleSlots, value);
                Vm?.RaisePropertyChanged("AllSlots");
            }
        }

        // ---- Apple Silicon ([global] backend, apple_device, engine_reuse, apple_auto_profile) ----
        // Three engines run the same models: Core ML through ONNX Runtime ("CoreML", the first one
        // shipped), Core ML alone ("CoreMLNative") and Metal ("Metal").
        public static readonly string[] AppleEngines = ["CoreML", "CoreMLNative", "Metal"];
        // AUTO = Core ML chooses; NPU = the Neural Engine; GPU+NPU = both on one frame;
        // GPU+NPU+CPU = the CPU takes tiles as well.
        public static readonly string[] AppleDevices = ["AUTO", "GPU", "NPU", "CPU", "GPU+NPU", "GPU+NPU+CPU"];

        private string _appleEngine = "CoreML";
        [DataMember]
        public string AppleEngine
        {
            get => _appleEngine;
            private set
            {
                this.RaiseAndSetIfChanged(ref _appleEngine, value);
                this.RaisePropertyChanged(nameof(AppleOrtSelected));
                this.RaisePropertyChanged(nameof(AppleCoreMlSelected));
                this.RaisePropertyChanged(nameof(AppleMetalSelected));
                this.RaisePropertyChanged(nameof(AppleDeviceChoice));
                this.RaisePropertyChanged(nameof(AppleDeviceNote));
            }
        }
        public bool AppleOrtSelected { get => AppleEngine == "CoreML"; set { if (value) UserSelectAppleEngine("CoreML"); else this.RaisePropertyChanged(); } }
        public bool AppleCoreMlSelected { get => AppleEngine == "CoreMLNative"; set { if (value) UserSelectAppleEngine("CoreMLNative"); else this.RaisePropertyChanged(); } }
        public bool AppleMetalSelected { get => AppleEngine == "Metal"; set { if (value) UserSelectAppleEngine("Metal"); else this.RaisePropertyChanged(); } }

        public void SetAppleEngine(string? value)
        {
            string wanted = (value ?? "").Trim();
            AppleEngine = AppleEngines.FirstOrDefault(e => e.Equals(wanted, StringComparison.OrdinalIgnoreCase)) ?? "CoreML";
            SetAppleDevice(AppleDevice);
        }

        public void UserSelectAppleEngine(string engine)
        {
            SetAppleEngine(engine);
            Vm?.RefreshComponentAwareness();
        }

        private string _appleDevice = "AUTO";
        [DataMember]
        public string AppleDevice
        {
            get => _appleDevice;
            private set
            {
                this.RaiseAndSetIfChanged(ref _appleDevice, value);
                foreach (string name in new[] { nameof(AppleDeviceAuto), nameof(AppleDeviceGpu), nameof(AppleDeviceNpu), nameof(AppleDeviceCpu), nameof(AppleDeviceGpuNpu), nameof(AppleDeviceGpuNpuCpu), nameof(AppleDeviceNote) })
                    this.RaisePropertyChanged(name);
            }
        }

        // Metal is the GPU: its choices are the GPU alone, or the GPU with the Neural Engine and
        // the CPU (both reached through Core ML). The two Core ML engines take every target.
        public bool AppleDeviceChoice => AppleEngine != "Metal";
        public static string NormalizeAppleDevice(string engine, string? value)
        {
            string wanted = (value ?? "").Trim().ToUpperInvariant().Replace(" ", "");
            wanted = wanted.Replace("ANE", "NPU");
            // The order of a list of processors does not matter: GPU first, then NPU, then CPU.
            if (wanted.Contains('+'))
            {
                var parts = wanted.Split('+', StringSplitOptions.RemoveEmptyEntries).Distinct().ToList();
                wanted = string.Join("+", new[] { "GPU", "NPU", "CPU" }.Where(parts.Contains));
            }
            wanted = wanted switch { "ALL" or "" => "AUTO", _ => wanted };
            if (!AppleDevices.Contains(wanted)) wanted = "AUTO";
            if (engine == "Metal" && wanted is not ("GPU+NPU" or "GPU+NPU+CPU")) wanted = "GPU";
            return wanted;
        }
        public void SetAppleDevice(string? value) => AppleDevice = NormalizeAppleDevice(AppleEngine, value);
        public bool AppleDeviceAuto { get => AppleDevice == "AUTO"; set { if (value) SetAppleDevice("AUTO"); else this.RaisePropertyChanged(); } }
        public bool AppleDeviceGpu { get => AppleDevice == "GPU"; set { if (value) SetAppleDevice("GPU"); else this.RaisePropertyChanged(); } }
        public bool AppleDeviceNpu { get => AppleDevice == "NPU"; set { if (value) SetAppleDevice("NPU"); else this.RaisePropertyChanged(); } }
        public bool AppleDeviceCpu { get => AppleDevice == "CPU"; set { if (value) SetAppleDevice("CPU"); else this.RaisePropertyChanged(); } }
        public bool AppleDeviceGpuNpu { get => AppleDevice == "GPU+NPU"; set { if (value) SetAppleDevice("GPU+NPU"); else this.RaisePropertyChanged(); } }
        public bool AppleDeviceGpuNpuCpu { get => AppleDevice == "GPU+NPU+CPU"; set { if (value) SetAppleDevice("GPU+NPU+CPU"); else this.RaisePropertyChanged(); } }
        public string AppleDeviceNote => (AppleEngine, AppleDevice) switch
        {
            ("Metal", "GPU+NPU") => "Metal on the GPU and Core ML on the Neural Engine share the tiles of each frame.",
            ("Metal", "GPU+NPU+CPU") => "Metal on the GPU, Core ML on the Neural Engine and on the CPU share the tiles of each frame. The CPU also decodes the video: compare with GPU + Neural Engine in Run Benchmarks.",
            (_, "GPU+NPU+CPU") => "The GPU, the Neural Engine and the CPU share the tiles of each frame. The CPU also decodes the video: compare with GPU + Neural Engine in Run Benchmarks.",
            ("Metal", _) => "The whole network runs on the GPU through Metal.",
            (_, "AUTO") => "Core ML decides where each operation runs.",
            (_, "GPU") => "Core ML on the GPU (the CPU remains its fallback for an operation the GPU cannot run).",
            (_, "NPU") => "Core ML on the Neural Engine (the CPU remains its fallback).",
            (_, "CPU") => "Core ML on the CPU only: for comparison, far slower.",
            _ => "The GPU and the Neural Engine share the tiles of each frame: each takes the next tile when it is free.",
        };

        // What is not recomputed: nothing, a frame identical to the last computed one (anime holds
        // a drawing for several frames), or each tile of the picture that did not change.
        private string _engineReuse = "frame";
        [DataMember]
        public string EngineReuse
        {
            get => _engineReuse;
            private set
            {
                this.RaiseAndSetIfChanged(ref _engineReuse, value);
                foreach (string name in new[] { nameof(EngineReuseOff), nameof(EngineReuseFrame), nameof(EngineReuseTiles) })
                    this.RaisePropertyChanged(name);
            }
        }
        public void SetEngineReuse(string? value)
        {
            string wanted = (value ?? "").Trim().ToLowerInvariant();
            EngineReuse = wanted is "off" or "no" or "false" or "0" ? "off" : wanted is "tiles" or "areas" ? "tiles" : "frame";
            this.RaisePropertyChanged(nameof(EngineBoostChoice));
        }

        // [global] engine_rife_device: the processor of the RIFE interpolation, which Core ML runs
        // whatever the engine that upscales. auto = Core ML chooses.
        private string _rifeDevice = "auto";
        [DataMember]
        public string RifeDevice
        {
            get => _rifeDevice;
            private set
            {
                this.RaiseAndSetIfChanged(ref _rifeDevice, value);
                foreach (string name in new[] { nameof(RifeDeviceAuto), nameof(RifeDeviceGpu), nameof(RifeDeviceNpu), nameof(RifeDeviceCpu) })
                    this.RaisePropertyChanged(name);
            }
        }
        public void SetRifeDevice(string? value)
        {
            string wanted = (value ?? "").Trim().ToLowerInvariant();
            RifeDevice = wanted switch { "gpu" => "gpu", "npu" or "ane" => "npu", "cpu" => "cpu", _ => "auto" };
        }
        public bool RifeDeviceAuto { get => RifeDevice == "auto"; set { if (value) SetRifeDevice("auto"); else this.RaisePropertyChanged(); } }
        public bool RifeDeviceGpu { get => RifeDevice == "gpu"; set { if (value) SetRifeDevice("gpu"); else this.RaisePropertyChanged(); } }
        public bool RifeDeviceNpu { get => RifeDevice == "npu"; set { if (value) SetRifeDevice("npu"); else this.RaisePropertyChanged(); } }
        public bool RifeDeviceCpu { get => RifeDevice == "cpu"; set { if (value) SetRifeDevice("cpu"); else this.RaisePropertyChanged(); } }

        // [global] engine_boost: a drawing that stays on screen for several frames is computed
        // with the Balanced model of the profile's family when that time covers it. The engine
        // applies it with whole-frame reuse and profiles that do not interpolate.
        private bool _engineBoost = false;
        [DataMember]
        public bool EngineBoost { get => _engineBoost; set => this.RaiseAndSetIfChanged(ref _engineBoost, value); }
        public bool EngineBoostChoice => EngineReuse == "frame";
        public bool EngineReuseOff { get => EngineReuse == "off"; set { if (value) SetEngineReuse("off"); else this.RaisePropertyChanged(); } }
        public bool EngineReuseFrame { get => EngineReuse == "frame"; set { if (value) SetEngineReuse("frame"); else this.RaisePropertyChanged(); } }
        public bool EngineReuseTiles { get => EngineReuse == "tiles"; set { if (value) SetEngineReuse("tiles"); else this.RaisePropertyChanged(); } }

        // The player moves to a lighter profile when this Mac cannot keep up with the video.
        private bool _appleAutoProfile = true;
        [DataMember]
        public bool AppleAutoProfile { get => _appleAutoProfile; set => this.RaiseAndSetIfChanged(ref _appleAutoProfile, value); }

        public void SetTensorRtSelected()
        {
            TensorRtSelected = true;
            DirectMlSelected = false;
            VulkanSelected = false;
            MiGraphXSelected = false;
            OpenVinoSelected = false;
        }

        public void SetDirectMlSelected()
        {
            if (!MainWindowViewModel.OnWindows) { SetTensorRtSelected(); return; }
            DirectMlSelected = true;
            TensorRtSelected = false;
            VulkanSelected = false;
            MiGraphXSelected = false;
            OpenVinoSelected = false;
        }

        public void SetVulkanSelected()
        {
            if (!MainWindowViewModel.OnLinux) { SetTensorRtSelected(); return; }
            TensorRtSelected = false;
            DirectMlSelected = false;
            VulkanSelected = true;
            MiGraphXSelected = false;
            OpenVinoSelected = false;
        }

        // openVino false = native MIGraphX (Windows/Linux); OpenVINO is Linux only.
        public void SetOnnxRuntimeSelected(bool openVino)
        {
            if (MainWindowViewModel.OnMac || (openVino && !MainWindowViewModel.OnLinux)) { SetTensorRtSelected(); return; }
            TensorRtSelected = false;
            DirectMlSelected = false;
            VulkanSelected = false;
            MiGraphXSelected = !openVino;
            OpenVinoSelected = openVino;
        }

        public void UserSelectMiGraphX()
        {
            BackendAutoFallback = false;
            SetOnnxRuntimeSelected(false);
            Vm?.RefreshComponentAwareness();
        }

        public void UserSelectOpenVino()
        {
            BackendAutoFallback = false;
            SetOnnxRuntimeSelected(true);
            Vm?.RefreshComponentAwareness();
        }

        public void UserSelectVulkan()
        {
            BackendAutoFallback = false;
            SetVulkanSelected();
            Vm?.RefreshComponentAwareness();
        }

        public void UserSelectTensorRt()
        {
            BackendAutoFallback = false;
            SetTensorRtSelected();
            Vm?.RefreshComponentAwareness();
        }

        public void UserSelectDirectMl()
        {
            BackendAutoFallback = false;
            SetDirectMlSelected();
        }

        private bool _backendAutoFallback;
        [DataMember]
        public bool BackendAutoFallback
        {
            get => _backendAutoFallback;
            set => this.RaiseAndSetIfChanged(ref _backendAutoFallback, value);
        }

        // [global] rife_engine: how the frames between two source frames are made by a profile
        // that interpolates before the upscale. fast: the upscaler runs once per source frame and
        // the in-between frames are the two upscaled pictures moved by RIFE's motion. quality: every
        // in-between frame is interpolated, then upscaled. Read by the engines that have both.
        private string _rifeEngine = "fast";
        [DataMember]
        public string RifeEngine
        {
            get => _rifeEngine;
            private set
            {
                var canonical = AnimeJaNai.Platform.QualitySemantics.CanonicalTemporal(value);
                this.RaiseAndSetIfChanged(ref _rifeEngine, canonical);
                this.RaisePropertyChanged(nameof(RifeEngineFast));
                this.RaisePropertyChanged(nameof(RifeEngineQuality));
            }
        }
        public void SetRifeEngine(string? value) =>
            RifeEngine = value ?? "";
        public bool RifeEngineFast { get => RifeEngine == "fast"; set { if (value) SetRifeEngine("fast"); else this.RaisePropertyChanged(); } }
        public bool RifeEngineQuality { get => RifeEngine == "quality"; set { if (value) SetRifeEngine("quality"); else this.RaisePropertyChanged(); } }

        // [global] rife_drawings: anime holds a drawing for two to four frames; with yes the motion
        // to the next drawing is spread over that whole time instead of happening in one frame.
        private bool _rifeDrawings = true;
        [DataMember]
        public bool RifeDrawings { get => _rifeDrawings; set => this.RaiseAndSetIfChanged(ref _rifeDrawings, value); }

        // Experimental GPU subtitle rendering ([global] sub_render_mode=gpu). Off => the player
        // uses the stable subtitle defaults from the managed [animejanai] profile.
        private bool _gpuSubtitles;
        [DataMember]
        public bool GpuSubtitles
        {
            get => _gpuSubtitles;
            set => this.RaiseAndSetIfChanged(ref _gpuSubtitles, value);
        }

        public Backend SelectedBackend => MiGraphXSelected ? Backend.MIGraphX : OpenVinoSelected ? Backend.OpenVINO
            : VulkanSelected ? Backend.Vulkan : DirectMlSelected ? Backend.DirectML : Backend.TensorRT;

        private bool _autoDisplayRefresh;
        [DataMember]
        public bool AutoDisplayRefresh
        {
            get => _autoDisplayRefresh;
            set => this.RaiseAndSetIfChanged(ref _autoDisplayRefresh, value);
        }
    }

    [DataContract]
    public class UpscaleSlot : ReactiveObject
    {
        public UpscaleSlot(bool autoSave = false)
        {
            if (autoSave)
            {
                this.WhenAnyValue(
                    x => x.SlotNumber,
                    x => x.ProfileName,
                    x => x.Chains.Count
                ).Subscribe(x =>
                {
                    Vm?.WriteAnimeJaNaiConf();
                });
            }

            this.WhenAnyValue(x => x.Vm).Subscribe(x =>
            {
                sub?.Dispose();
                sub = Vm.WhenAnyValue(
                    x => x.SelectedSlotNumber,
                    x => x.ShowCustomProfiles
                    ).Subscribe(x =>
                {
                    this.RaisePropertyChanged(nameof(ActiveSlot));
                    Vm?.RaisePropertyChanged("AllSlots");
                });

                sub2?.Dispose();
                sub2 = Vm.WhenAnyValue(x => x.SelectedMpvProfile).Subscribe(x =>
                {
                    this.RaisePropertyChanged(nameof(IsSelectedMpvProfile));
                });
            });
        }

        private IDisposable? sub;
        private IDisposable? sub2;

        private MainWindowViewModel? _vm;
        public MainWindowViewModel? Vm
        {
            get => _vm;
            set => this.RaiseAndSetIfChanged(ref _vm, value);
        }

        private string _slotNumber = string.Empty;
        [DataMember]
        public string SlotNumber
        {
            get => _slotNumber;
            set => this.RaiseAndSetIfChanged(ref _slotNumber, value);
        }

        [DataMember] public bool HdrStageControlled { get; set; } = true;

        public bool ActiveSlot => (Vm?.ShowCustomProfiles ?? false) && SlotNumber == Vm?.SelectedSlotNumber;


        public string SlotIcon => $"Number{SlotNumber}Circle";

        public bool ShowSlot => int.Parse(_slotNumber) < 10;

        private string _profileName = string.Empty;
        [DataMember]
        public string ProfileName
        {
            get => _profileName;
            set => this.RaiseAndSetIfChanged(ref _profileName, value);
        }

        private string _descriptionText = string.Empty;
        [DataMember]
        public string DescriptionText
        {
            get => _descriptionText;
            set => this.RaiseAndSetIfChanged(ref _descriptionText, value);
        }

        private string _mpvProfileName;
        [DataMember]
        public string MpvProfileName
        {
            get => _mpvProfileName;
            set => this.RaiseAndSetIfChanged(ref _mpvProfileName, value);
        }

        public bool IsSelectedMpvProfile => MpvProfileName == Vm?.SelectedMpvProfile;

        private AvaloniaList<UpscaleChain> _chains = [];
        [DataMember]
        public AvaloniaList<UpscaleChain> Chains
        {
            get => _chains;
            set => this.RaiseAndSetIfChanged(ref _chains, value);
        }
    }

    [DataContract]
    public partial class UpscaleChain : ReactiveObject
    {
        public UpscaleChain(bool autoSave = false)
        {
            AutoSave = autoSave;

            if (autoSave)
            {
                this.WhenAnyValue(x=>x.ChainNumber,x=>x.MinResolution,x=>x.MaxResolution,x=>x.MinFps,x=>x.MaxFps)
                    .Subscribe(_=>Vm?.WriteAnimeJaNaiConf());
            }
        }

        public bool AutoSave { get; set; }

        public MainWindowViewModel? Vm { get; set; }

        private string _chainNumber = string.Empty;
        [DataMember]
        public string ChainNumber
        {
            get => _chainNumber;
            set => this.RaiseAndSetIfChanged(ref _chainNumber, value);
        }

        private string _minResolution = "0x0";
        [DataMember]
        public string MinResolution
        {
            get => _minResolution;
            set => this.RaiseAndSetIfChanged(ref _minResolution, value);
        }

        private string _maxResolution = "0x0";
        [DataMember]
        public string MaxResolution
        {
            get => _maxResolution;
            set => this.RaiseAndSetIfChanged(ref _maxResolution, value);
        }

        private string _minFps = 0.ToString();
        [DataMember]
        public string MinFps
        {
            get => _minFps;
            set => this.RaiseAndSetIfChanged(ref _minFps, value);
        }

        private string _maxFps = 0.ToString();
        [DataMember]
        public string MaxFps
        {
            get => _maxFps;
            set => this.RaiseAndSetIfChanged(ref _maxFps, value);
        }

    }

    public enum Backend
    {
        TensorRT,
        DirectML,
        Vulkan,
        NCNN,
        MIGraphX,
        OpenVINO
    }
}
