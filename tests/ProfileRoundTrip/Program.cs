using AnimeJaNaiConfEditor.ViewModels;
using ReactiveUI.Builder;

try
{
    RxAppBuilder.CreateReactiveUIBuilder().WithCoreServices().BuildApp();
    if (args.Length == 3 && args[0] == "--save-hdr")
    {
        var live = new HdrSettings(args[1]);
        live.LinuxOutput = args[2]; live.RtxContrast = 130; live.Save();
        if (live.SaveNotice.Length > 0) throw new Exception(live.SaveNotice);
        return 0;
    }
    string data = Path.Combine(AppContext.BaseDirectory, "fixtures", Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(Path.Combine(data, "onnx"));
    Environment.SetEnvironmentVariable("ANIMEJANAI_DATA_DIR", data);
    // AJN_UI_PLATFORM=macos: the whole test as on a Mac, on whatever machine runs it. After the
    // folder above: the view model reads it the first time it is touched.
    if (Environment.GetEnvironmentVariable("AJN_UI_PLATFORM") == "macos") MainWindowViewModel.Platform = MainWindowViewModel.Os.MacOS;
    File.WriteAllText(Path.Combine(data, "animejanai.conf"),
        "[global]\nconfig_version=2\nbackend=DirectML\ndefault_slot=0\n" +
        "[slot_1]\nprofile_name=Round trip\nchain_1_rife=yes\nchain_1_rife_ensemble=yes\n");
    var vm = new MainWindowViewModel();
    vm.SelectedSlotNumber = "1";
    if (vm.AnimeJaNaiConf.AutoDisplayRefresh)
        throw new Exception("Automatic display refresh must default to off");
    foreach (bool enabled in new[] { true, false })
    {
        vm.AnimeJaNaiConf.AutoDisplayRefresh = enabled;
        string full = Path.Combine(data, $"display-refresh-{enabled}.conf");
        vm.WriteAnimeJaNaiConf(full);
        if (vm.ReadAnimeJaNaiConf(full).AutoDisplayRefresh != enabled)
            throw new Exception($"Full config lost automatic display refresh={enabled}");
        if (vm.ReadAnimeJaNaiConf(Path.Combine(data, "animejanai.conf")).AutoDisplayRefresh != enabled)
            throw new Exception($"Autosave lost automatic display refresh={enabled}");
        Console.WriteLine($"PASS automatic display refresh={enabled} export/reload and autosave");
    }
    InterpolationStage Rife() => vm.CurrentSlot.Chains[0].Stages.Single(s => s.IsRife);
    foreach (bool ensemble in new[] { true, false })
    {
        Rife().Ensemble = ensemble;
        string profile = Path.Combine(data, $"ensemble-{ensemble}.conf");
        vm.WriteAnimeJaNaiCurrentProfileConf(profile);
        // Change the destination first so a skipped assignment cannot pass.
        Rife().Ensemble = !ensemble;
        vm.ReadAnimeJaNaiConfToCurrentSlot(profile, false);
        if (Rife().Ensemble != ensemble)
            throw new Exception($"Profile import lost RIFE ensemble={ensemble}");
        Console.WriteLine($"PASS ensemble={ensemble}");
    }
    {
        // Contract v2: a v3 file with the HDR step the old migration copied into each profile
        // (TrueHDR 200/200, last, active) opens as format 4 without the step; HDR becomes the
        // global NVIDIA TrueHDR choice and the user is told once, its look not copied.
        string v3 = Path.Combine(data, "v3-hdr.conf");
        File.WriteAllText(v3, "[global]\nconfig_version=3\nbackend=TensorRT\ndefault_slot=1\n[slot_1]\nprofile_name=Old\nhdr_stage_controlled=yes\nchain__rife=no\n" +
            "chain_1_pipeline_version=1\nchain_1_stage_count=2\nchain_1_stage_1_id=up\nchain_1_stage_1_kind=upscale\nchain_1_stage_1_method=onnx\nchain_1_stage_1_runtime=tensorrt\nchain_1_stage_1_enabled=yes\nchain_1_stage_1_model=perf\n" +
            "chain_1_stage_2_id=hdr\nchain_1_stage_2_kind=hdr\nchain_1_stage_2_method=nvidia-truehdr\nchain_1_stage_2_runtime=cuda\nchain_1_stage_2_enabled=yes\nchain_1_stage_2_contrast=200\nchain_1_stage_2_saturation=200\n");
        var old = vm.ReadAnimeJaNaiConf(v3);
        if (old.UpscaleSlots[0].Chains.Count != 1 || old.UpscaleSlots[0].Chains[0].Stages.Any(st => st.Kind == "hdr"))
            throw new Exception("The HDR step or the phantom rule survived the opening");
        if (!vm.HdrSettings.HdrConversion || vm.HdrSettings.Backend != "rtx" || !vm.ProfileMigrationNotice.Contains("contrast 200"))
            throw new Exception("The active TrueHDR step did not become the global choice with its notice: " + vm.ProfileMigrationNotice);
        string written = Path.Combine(data, "v4-hdr.conf");
        vm.AnimeJaNaiConf = old; vm.WriteAnimeJaNaiConf(written);
        string text = File.ReadAllText(written);
        if (!text.Contains("config_version=4") || text.Contains("kind=hdr") || text.Contains("chain__") || text.Contains("hdr_stage_controlled") || !text.Contains("chain_1_model_1_name=perf"))
            throw new Exception("Format 4 save kept retired data or lost the legacy keys:\n" + text);
        vm.AnimeJaNaiConf = vm.ReadAnimeJaNaiConf(Path.Combine(data, "animejanai.conf"), true); vm.SelectedSlotNumber = "1";
        Console.WriteLine("PASS v3 profile HDR step becomes the global TrueHDR choice; format 4 has no step, phantom rule or marker");
    }
    {
        // A file written by a newer Manager is shown but never saved over.
        string newer = Path.Combine(data, "newer.conf");
        File.WriteAllText(newer, "[global]\nconfig_version=9\nbackend=TensorRT\n[slot_1]\nprofile_name=Newer\n");
        var current = vm.AnimeJaNaiConf;
        vm.ReadAnimeJaNaiConf(newer, true);
        if (vm.NewerConfiguration != 9 || !vm.HasNewerConfigurationNotice) throw new Exception("Newer format not detected");
        string before = File.ReadAllText(Path.Combine(data, "animejanai.conf"));
        vm.WriteAnimeJaNaiConf();
        if (File.ReadAllText(Path.Combine(data, "animejanai.conf")) != before) throw new Exception("Autosave wrote over a newer configuration");
        vm.ReadAnimeJaNaiConf(Path.Combine(data, "animejanai.conf"), true);
        vm.AnimeJaNaiConf = current;
        if (vm.NewerConfiguration != 0) throw new Exception("The guard stayed on for a current file");
        Console.WriteLine("PASS a newer configuration format is never saved over");
    }
    var hdrPath = Path.Combine(data, "hdr.conf");
    File.WriteAllText(hdrPath, "# retained comment\nunknown_option=custom\nlinux_hdr_output=sdr # output choice\n");
    var hdr = new HdrSettings(hdrPath);
    hdr.LinuxOutput = "pq"; hdr.Save();
    var saved = File.ReadAllText(hdrPath);
    if (!saved.Contains("# retained comment") || !saved.Contains("unknown_option=custom") || !saved.Contains("linux_hdr_output=pq # output choice"))
        throw new Exception("HDR save lost comments/options or PQ selection");
    if (new HdrSettings(hdrPath).LinuxOutput != "pq") throw new Exception("HDR reload lost PQ selection");
    Console.WriteLine("PASS HDR comments, unknown options, SDR/PQ save and reload");
    string defaultsPath = Path.Combine(data, "defaults.conf");
    var defaults = new HdrSettings(defaultsPath);
    if (!defaults.Display.PeakAuto || defaults.Display.Peak is not null || defaults.Display.Oled ||
        defaults.Display.Gamut != "auto" || defaults.Display.WindowsHdr != "follow" || !defaults.LinuxAuto ||
        !defaults.HdrConversion || !defaults.Enhance || defaults.FrameGeneration != "off" ||
        defaults.RifeDisplaySync || defaults.EnhanceNativeHdr || defaults.BlackDepth != 15 || defaults.VibranceMpv != 15)
        throw new Exception("New-install settings are not shared, automatic and Natural");
    defaults.Save();
    string newConf = File.ReadAllText(defaultsPath);
    if (!newConf.Contains("display_peak=auto") || !newConf.Contains("display_oled=no") ||
        newConf.Contains("peak_nvidia=") || newConf.Contains("peak_amd="))
        throw new Exception("New settings wrote per-GPU display descriptions");
    Console.WriteLine("PASS new-install display defaults, auto HDR and active Natural grade");
    string legacyPath = Path.Combine(data, "legacy-display.conf");
    const string legacy = "# personal screen\npeak_nvidia=1000 # calibrated\noled_nvidia=yes\ngamut_nvidia=display-p3\nmode=follow\n" +
        "peak_amd=650\noled_amd=no\ngamut_amd=bt.2020\namd_mode=enable\nunknown=keep\n";
    File.WriteAllText(legacyPath, legacy);
    var migrated = new HdrSettings(legacyPath, "amd");
    if (migrated.Display.Peak != 650 || migrated.Display.PeakAuto || migrated.Display.Oled ||
        migrated.Display.Gamut != "bt.2020" || migrated.Display.WindowsHdr != "enable")
        throw new Exception("Legacy migration did not preserve the active bank's display");
    migrated.Save();
    string migratedText = File.ReadAllText(legacyPath);
    if (!migratedText.Contains(legacy) || !migratedText.Contains("display_peak=650"))
        throw new Exception("Migration lost legacy screen values or comments");
    var independent = new HdrSettings(legacyPath, "nvidia");
    if (independent.Display.Peak != 650 || independent.Display.Gamut != "bt.2020")
        throw new Exception("New common display changed with the GPU");
    Console.WriteLine("PASS legacy active display migration and GPU-independent round trip");
    independent.Display.Oled = true; independent.Display.PeakAuto = true; independent.Save();
    independent.Display.Oled = false; independent.Save();
    var reset = new HdrSettings(legacyPath, "nvidia");
    if (reset.Display.Oled || !reset.Display.PeakAuto || reset.Display.Peak is not null)
        throw new Exception("Explicit common OLED=false or Auto was overridden by legacy values");
    Console.WriteLine("PASS explicit OLED off and automatic peak override legacy manual values");
    File.WriteAllText(legacyPath, legacy);
    var pendingDisplay = new HdrSettings(legacyPath, "nvidia");
    pendingDisplay.Display.Peak = 800;
    pendingDisplay.SelectLegacyDisplay("amd");
    if (pendingDisplay.Display.Peak != 800)
        throw new Exception("Delayed GPU detection overwrote an edited display");
    Console.WriteLine("PASS delayed legacy detection preserves user display edits");
    var merged = IniPreservingSave.Merge("# comment\n[global]\nbackend=TensorRT # choice\nunknown=keep\ndisplay_rate_match=yes\n", "[global]\nbackend=TensorRT\n");
    if (!merged.Contains("# comment") || !merged.Contains("# choice") || !merged.Contains("unknown=keep") || merged.Contains("display_rate_match"))
        throw new Exception("INI merge lost comments/unknown keys or retained removed managed option");
    Console.WriteLine("PASS INI preserves comments and removes optional managed keys");
    {
        var linuxNvidia = MainWindowViewModel.GpuOptions.For(false, true, false, true);   // hybrid Intel + NVIDIA
        var linuxAmd = MainWindowViewModel.GpuOptions.For(false, false, true, false);
        var linuxIntel = MainWindowViewModel.GpuOptions.For(false, false, false, true);
        var winNvidia = MainWindowViewModel.GpuOptions.For(true, true, false, null);
        var winOther = MainWindowViewModel.GpuOptions.For(true, false, null, null);
        var winPending = MainWindowViewModel.GpuOptions.For(true, null, null, null);
        if (!linuxNvidia.RtxHdr || linuxNvidia.RtxVsr || linuxNvidia.Amf || linuxNvidia.MiGraphX || !linuxNvidia.OpenVino)
            throw new Exception("Linux NVIDIA: RTX Video HDR must be offered, AMD-only options greyed");
        if (linuxAmd.RtxHdr || !linuxAmd.MiGraphX || linuxAmd.OpenVino || linuxAmd.Amf)
            throw new Exception("Linux AMD: only MIGraphX is vendor-specific");
        if (linuxIntel.RtxHdr || linuxIntel.MiGraphX || !linuxIntel.OpenVino)
            throw new Exception("Linux Intel: only OpenVINO is vendor-specific");
        if (!winNvidia.RtxHdr || !winNvidia.RtxVsr || winNvidia.Amf || winNvidia.MiGraphX || winNvidia.OpenVino)
            throw new Exception("Windows NVIDIA: RTX options offered, AMD options greyed");
        if (winOther.RtxHdr || winOther.RtxVsr || !winOther.Amf)
            throw new Exception("Windows without NVIDIA: RTX options greyed, AMD options offered");
        if (!winPending.RtxHdr || !winPending.RtxVsr || !winPending.Amf)
            throw new Exception("Pending detection must not grey anything on Windows");
        if (!linuxNvidia.SmoothMotion || linuxAmd.SmoothMotion || linuxIntel.SmoothMotion || winNvidia.SmoothMotion || winPending.SmoothMotion)
            throw new Exception("Smooth Motion is a Linux NVIDIA setting only");
        string layers = Path.Combine(data, "layers"); Directory.CreateDirectory(layers);
        File.WriteAllText(Path.Combine(layers, "other.json"), "{\"layer\":{}}");
        if (MainWindowViewModel.SmoothMotionLayerInstalled(layers)) throw new Exception("Smooth Motion found without the driver layer");
        File.WriteAllText(Path.Combine(layers, "nvidia_layers.json"), "{\"enable_environment\":{\"NVPRESENT_ENABLE_SMOOTH_MOTION\":\"1\"}}");
        if (!MainWindowViewModel.SmoothMotionLayerInstalled(layers)) throw new Exception("Smooth Motion driver layer not recognised");
        string smoothPath = Path.Combine(data, "smooth.conf");
        File.WriteAllText(smoothPath, "# keep\nrife_display_sync=no\n");
        var smooth = new HdrSettings(smoothPath);
        if (smooth.SmoothMotion) throw new Exception("Smooth Motion must be off by default");
        if (MainWindowViewModel.OnLinux)
        {
            smooth.SmoothMotion = true; smooth.Save();
            if (!new HdrSettings(smoothPath).SmoothMotion || !File.ReadAllText(smoothPath).Contains("smooth_motion=yes") || !File.ReadAllText(smoothPath).Contains("# keep"))
                throw new Exception("Smooth Motion lost at save and reload");
        }
        Console.WriteLine("PASS GPU-specific options follow the GPUs of the machine");
        Console.WriteLine("PASS Smooth Motion: Linux NVIDIA only, driver layer required, saved and reloaded");
    }
    if (MainWindowViewModel.OnLinux)
    {
        vm.AnimeJaNaiConf.UserSelectDirectMl();
        if (vm.AnimeJaNaiConf.DirectMlSelected || vm.RtxVsrSelectable || vm.AmfSelectable)
            throw new Exception("Linux offered a Windows-only backend");
        Console.WriteLine("PASS Linux refuses DirectML, RTX VSR and AMF");
        Directory.CreateDirectory(Path.Combine(data, "inference"));
        File.WriteAllText(Path.Combine(data, "inference", "libaji_vulkan.so"), "fixture");
        Directory.CreateDirectory(Path.Combine(data, "rife-vulkan", "rife-v4.6"));
        foreach (string file in new[] { "flownet.param", "flownet.bin" })
            File.WriteAllText(Path.Combine(data, "rife-vulkan", "rife-v4.6", file), "fixture");
        foreach (string file in new[] { "spatial.onnx", "spatial.ncnn.param", "spatial.ncnn.bin", "unsupported.onnx" })
            File.WriteAllText(Path.Combine(data, "onnx", file), "fixture");
        vm.AnimeJaNaiConf.UserSelectVulkan();
        if (!vm.VulkanSelectable || vm.RifeMissing || vm.AnimeJaNaiConf.TensorRtSelected || vm.AnimeJaNaiConf.DirectMlSelected)
            throw new Exception("Vulkan selection does not expose its own installed components");
        if (vm.GetAllModels().Count != 1 || vm.GetAllModels()[0] != "spatial" || MainWindowViewModel.RifeModels.Count != 1)
            throw new Exception("Vulkan offered a model that was not converted");
        if (Rife().Model != "46" || Rife().Runtime != "vulkan")
            throw new Exception("Selecting Vulkan did not move the RIFE step to RIFE 4.6 on Vulkan: " + Rife().Model + "/" + Rife().Runtime);
        // Back to TensorRT with ONNX RIFE models installed: 4.6 does not exist there.
        Directory.CreateDirectory(Path.Combine(data, "rife"));
        foreach (string file in new[] { "rife_v4.25.onnx", "rife_v4.26.onnx", "rife_v4.26_heavy.onnx" })
            File.WriteAllText(Path.Combine(data, "rife", file), "fixture");
        vm.AnimeJaNaiConf.UserSelectTensorRt();
        if (Rife().Model != "426" || Rife().Runtime != "tensorrt")
            throw new Exception("TensorRT kept the Vulkan-only RIFE 4.6: " + Rife().Model);
        vm.AnimeJaNaiConf.UserSelectVulkan();
        if (Rife().Model != "46")
            throw new Exception("Vulkan kept a TensorRT RIFE model: " + Rife().Model);
        Console.WriteLine("PASS RIFE model follows the engine (4.26 on TensorRT, 4.6 on Vulkan)");
        string vulkan = Path.Combine(data, "vulkan.conf");
        vm.WriteAnimeJaNaiConf(vulkan);
        var reloaded = vm.ReadAnimeJaNaiConf(vulkan);
        if (!reloaded.VulkanSelected || reloaded.SelectedBackend != Backend.Vulkan || reloaded.UpscaleSlots[0].Chains[0].Stages.Single(st => st.IsRife).Model != "46")
            throw new Exception("Vulkan backend or RIFE 4.6 lost at round trip");
        Console.WriteLine("PASS Vulkan selection, converted-model filtering and RIFE 4.6 round trip");
        string socketPath = Path.Combine(Path.GetTempPath(), "ajn-ipc-" + Guid.NewGuid().ToString("N"));
        using var listener = new System.Net.Sockets.Socket(System.Net.Sockets.AddressFamily.Unix,
            System.Net.Sockets.SocketType.Stream, System.Net.Sockets.ProtocolType.Unspecified);
        listener.Bind(new System.Net.Sockets.UnixDomainSocketEndPoint(socketPath)); listener.Listen(1);
        Environment.SetEnvironmentVariable("ANIMEJANAI_IPC_PATH", socketPath);
        try
        {
            HdrSettings.NotifyPlayer();
            using var connection = listener.Accept(); connection.ReceiveTimeout = 1000;
            var buffer = new byte[512]; int count = connection.Receive(buffer);
            var line = System.Text.Encoding.UTF8.GetString(buffer, 0, count);
            if (line != "{\"command\":[\"script-message\",\"animejanai-reload-options\"]}\n")
                throw new Exception("Incorrect Unix socket command");
            Console.WriteLine("PASS Unix socket sends the existing reload message");
        }
        finally { Environment.SetEnvironmentVariable("ANIMEJANAI_IPC_PATH", null); File.Delete(socketPath); }
    }
    {
        // macOS (Apple Silicon) settings, whatever the platform the test runs on.
        var real = MainWindowViewModel.Platform;
        MainWindowViewModel.Platform = MainWindowViewModel.Os.MacOS;
        try
        {
            foreach (string file in new[] { "perf.onnx", "perf.coreml.onnx", "perf.ajm", "perf.mlpatch", "custom.onnx" })
                File.WriteAllText(Path.Combine(data, "onnx", file), "fixture");
            string N(string engine, string? device) => AnimeJaNaiConf.NormalizeAppleDevice(engine, device);
            if (N("CoreML", "ALL") != "AUTO" || N("CoreMLNative", "ane") != "NPU" || N("CoreML", "gpu+ane") != "GPU+NPU" || N("CoreML", "") != "AUTO"
                || N("CoreMLNative", "CPU") != "CPU" || N("CoreML", "bogus") != "AUTO")
                throw new Exception("Compute target names are not normalised");
            if (N("Metal", "NPU") != "GPU" || N("Metal", "AUTO") != "GPU" || N("Metal", "CPU") != "GPU" || N("Metal", "GPU+ANE") != "GPU+NPU")
                throw new Exception("Metal only takes the GPU, alone or with the Neural Engine");
            if (N("CoreML", "cpu+ane+gpu") != "GPU+NPU+CPU" || N("Metal", "GPU+NPU+CPU") != "GPU+NPU+CPU" || N("CoreMLNative", "NPU+GPU") != "GPU+NPU"
                || N("CoreML", "NPU+CPU") != "AUTO" || N("Metal", "NPU+CPU") != "GPU")
                throw new Exception("Lists of processors are not normalised");
            Console.WriteLine("PASS macOS compute targets: every target for Core ML, GPU or GPU + Neural Engine for Metal");

            string macPath = Path.Combine(data, "mac.conf");
            File.WriteAllText(macPath, "# keep me\n[global]\nconfig_version=3\nbackend=CoreML\ndefault_slot=1\nunknown=keep\n" +
                "[slot_1]\nprofile_name=Mac\nchain_1_model_1_name=perf\nchain_1_rife=no\n");
            var first = vm.ReadAnimeJaNaiConf(macPath);
            if (first.AppleEngine != "CoreML" || first.AppleDevice != "AUTO" || first.EngineReuse != "frame" || !first.AppleAutoProfile)
                throw new Exception("macOS defaults: ONNX Runtime Core ML, automatic target, whole frames reused, automatic profile");
            File.WriteAllText(macPath, "[global]\nbackend=metal\napple_device=ANE\nengine_reuse=areas\napple_auto_profile=no\n[slot_1]\nprofile_name=Mac\n");
            var metal = vm.ReadAnimeJaNaiConf(macPath);
            if (metal.AppleEngine != "Metal" || metal.AppleDevice != "GPU" || metal.EngineReuse != "tiles" || metal.AppleAutoProfile || !metal.AppleMetalSelected || metal.AppleDeviceChoice)
                throw new Exception("macOS settings are not read: " + metal.AppleEngine + " " + metal.AppleDevice + " " + metal.EngineReuse);
            Console.WriteLine("PASS macOS engine, compute target, reuse and automatic profile are read with their defaults");

            File.WriteAllText(macPath, "# keep me\n[global]\nconfig_version=3\nbackend=CoreML\nunknown=keep\n[slot_1]\nprofile_name=Mac\n");
            vm.AnimeJaNaiConf.UserSelectAppleEngine("CoreMLNative");
            vm.AnimeJaNaiConf.SetAppleDevice("GPU+NPU");
            vm.AnimeJaNaiConf.SetEngineReuse("tiles");
            vm.AnimeJaNaiConf.AppleAutoProfile = false;
            vm.AnimeJaNaiConf.SetRifeDevice("ANE");
            vm.AnimeJaNaiConf.EngineBoost = true;
            if (vm.AnimeJaNaiConf.EngineBoostChoice) throw new Exception("The heavier model on held drawings is offered with whole-frame reuse only");
            vm.WriteAnimeJaNaiConf(macPath);
            string written = File.ReadAllText(macPath);
            foreach (string line in new[] { "backend=CoreMLNative", "apple_device=GPU+NPU", "engine_reuse=tiles", "apple_auto_profile=no",
                "engine_rife_device=npu", "engine_boost=yes", "# keep me", "unknown=keep" })
                if (!written.Contains(line)) throw new Exception("macOS save lost: " + line + "\n" + written);
            var again = vm.ReadAnimeJaNaiConf(macPath);
            if (again.AppleEngine != "CoreMLNative" || again.AppleDevice != "GPU+NPU" || again.EngineReuse != "tiles" || again.AppleAutoProfile
                || again.RifeDevice != "npu" || !again.EngineBoost)
                throw new Exception("macOS settings lost at reload");
            if (first.RifeDevice != "auto" || first.EngineBoost) throw new Exception("macOS defaults: automatic RIFE processor, no heavier model");
            Console.WriteLine("PASS macOS settings survive save and reload, comments and unknown keys kept");

            vm.AnimeJaNaiConf.UserSelectAppleEngine("CoreML");
            var listed = vm.GetAllModels();
            if (!listed.Contains("custom") || !listed.Contains("perf") || listed.Any(name => name.EndsWith(".coreml")))
                throw new Exception("ONNX Runtime lists every ONNX model, never the .coreml variants: " + string.Join(",", listed));
            vm.AnimeJaNaiConf.UserSelectAppleEngine("CoreMLNative");
            if (!vm.GetAllModels().SequenceEqual(new[] { "perf" })) throw new Exception("Core ML native lists the models converted for it");
            vm.AnimeJaNaiConf.SetAppleDevice("NPU");
            vm.AnimeJaNaiConf.UserSelectAppleEngine("Metal");
            if (!vm.GetAllModels().SequenceEqual(new[] { "perf" }) || vm.AnimeJaNaiConf.AppleDevice != "GPU")
                throw new Exception("Metal lists the models converted for it and moves a Core ML target to the GPU");
            if (vm.TrtSelectable || vm.VulkanSelectable || vm.RtxHdrSelectable || vm.SmoothMotionSelectable
                || !vm.AppleMetalSelectable || !vm.AppleCoreMlSelectable || !vm.MacFeatures || vm.WindowsFeatures || vm.LinuxFeatures)
                throw new Exception("macOS offered an engine or an option of another platform");
            Console.WriteLine("PASS macOS model lists follow the engine; engines and options of other platforms are off");

            // RIFE is offered once its models are in the installation.
            Directory.CreateDirectory(Path.Combine(data, "rife"));
            File.WriteAllText(Path.Combine(data, "rife", "rife_v4.26.onnx"), "fixture");
            vm.RefreshComponentAwareness();
            if (vm.RifeMissing) throw new Exception("macOS did not offer RIFE with its models installed");
            Console.WriteLine("PASS macOS offers RIFE when its models are installed");

            MainWindowViewModel.Platform = MainWindowViewModel.Os.Linux;
            string linuxPath = Path.Combine(data, "not-mac.conf");
            vm.WriteAnimeJaNaiConf(linuxPath);
            string other = File.ReadAllText(linuxPath);
            if (other.Contains("apple_device") || other.Contains("backend=Metal") || other.Contains("engine_reuse")
                || other.Contains("engine_boost") || other.Contains("engine_rife_device"))
                throw new Exception("macOS keys written on another platform");
            Console.WriteLine("PASS other platforms do not write the macOS keys");
        }
        finally { MainWindowViewModel.Platform = real; }
    }
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine(ex);
    return 1;
}
