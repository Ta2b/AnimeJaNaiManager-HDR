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
    foreach (bool ensemble in new[] { true, false })
    {
        vm.CurrentSlot.Chains[0].RifeEnsemble = ensemble;
        string profile = Path.Combine(data, $"ensemble-{ensemble}.conf");
        vm.WriteAnimeJaNaiCurrentProfileConf(profile);
        // Change the destination first so a skipped assignment cannot pass.
        vm.CurrentSlot.Chains[0].RifeEnsemble = !ensemble;
        vm.ReadAnimeJaNaiConfToCurrentSlot(profile, false);
        if (vm.CurrentSlot.Chains[0].RifeEnsemble != ensemble)
            throw new Exception($"Profile import lost RIFE ensemble={ensemble}");
        Console.WriteLine($"PASS ensemble={ensemble}");
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
        if (OperatingSystem.IsLinux())
        {
            smooth.SmoothMotion = true; smooth.Save();
            if (!new HdrSettings(smoothPath).SmoothMotion || !File.ReadAllText(smoothPath).Contains("smooth_motion=yes") || !File.ReadAllText(smoothPath).Contains("# keep"))
                throw new Exception("Smooth Motion lost at save and reload");
        }
        Console.WriteLine("PASS GPU-specific options follow the GPUs of the machine");
        Console.WriteLine("PASS Smooth Motion: Linux NVIDIA only, driver layer required, saved and reloaded");
    }
    if (OperatingSystem.IsLinux())
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
        vm.CurrentSlot.Chains[0].RifeModel = MainWindowViewModel.RifeValueToLabel("46");
        // Back to TensorRT with ONNX RIFE models installed: 4.6 does not exist there.
        Directory.CreateDirectory(Path.Combine(data, "rife"));
        foreach (string file in new[] { "rife_v4.25.onnx", "rife_v4.26.onnx", "rife_v4.26_heavy.onnx" })
            File.WriteAllText(Path.Combine(data, "rife", file), "fixture");
        vm.AnimeJaNaiConf.UserSelectTensorRt();
        if (vm.CurrentSlot.Chains[0].RifeModel != MainWindowViewModel.RifeValueToLabel("426"))
            throw new Exception("TensorRT kept the Vulkan-only RIFE 4.6: " + vm.CurrentSlot.Chains[0].RifeModel);
        vm.AnimeJaNaiConf.UserSelectVulkan();
        if (vm.CurrentSlot.Chains[0].RifeModel != MainWindowViewModel.RifeValueToLabel("46"))
            throw new Exception("Vulkan kept a TensorRT RIFE model: " + vm.CurrentSlot.Chains[0].RifeModel);
        Console.WriteLine("PASS RIFE model follows the engine (4.26 on TensorRT, 4.6 on Vulkan)");
        string vulkan = Path.Combine(data, "vulkan.conf");
        vm.WriteAnimeJaNaiConf(vulkan);
        var reloaded = vm.ReadAnimeJaNaiConf(vulkan);
        if (!reloaded.VulkanSelected || reloaded.SelectedBackend != Backend.Vulkan || reloaded.UpscaleSlots[0].Chains[0].RifeModel != MainWindowViewModel.RifeValueToLabel("46"))
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
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine(ex);
    return 1;
}
