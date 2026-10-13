using AnimeJaNaiConfEditor.ViewModels;
using AnimeJaNaiConfEditor.Views;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ReactiveUI.Avalonia;

string data = Path.Combine(Path.GetTempPath(), "ajn-settings-ui-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(Path.Combine(data, "onnx"));
// Before the first use of the view model: its folders are read once from the environment.
Environment.SetEnvironmentVariable("ANIMEJANAI_DATA_DIR", data);
// AJN_UI_PLATFORM=macos: the macOS layout, on whatever machine runs the test.
bool describeMac = Environment.GetEnvironmentVariable("AJN_UI_PLATFORM") == "macos";
if (describeMac) MainWindowViewModel.Platform = MainWindowViewModel.Os.MacOS;
bool mac = MainWindowViewModel.OnMac;
if (mac)
    foreach (string file in new[] { "perf.onnx", "perf.coreml.onnx", "perf.ajm", "perf.mlpatch" })
        File.WriteAllText(Path.Combine(data, "onnx", file), "fixture");
File.WriteAllText(Path.Combine(data, "animejanai.conf"), mac
    ? "[global]\nconfig_version=3\nbackend=Metal\napple_device=GPU\ndefault_slot=1\n[slot_1]\nprofile_name=Performance\nchain_1_model_1_name=perf\nchain_1_rife=no\n"
    : "[global]\nconfig_version=3\nbackend=Vulkan\ndefault_slot=1\n[slot_1]\nprofile_name=Performance\nchain_1_rife=no\n");
Environment.SetEnvironmentVariable("ANIMEJANAI_DATA_DIR", data);
try
{
    AppBuilder.Configure<AnimeJaNaiConfEditor.App>().UseSkia()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
        .WithInterFont().UseReactiveUI(_ => { }).SetupWithoutStarting();
    var vm = new MainWindowViewModel(); vm.HandleShowGlobalSettings();
    var window = new MainWindow { DataContext = vm, Width = 1120, Height = 1050 };
    window.Show(); Dispatcher.UIThread.RunJobs();
    void Check(string label, bool condition)
    {
        if (!condition) throw new Exception(label);
        Console.WriteLine("PASS " + label);
    }
    Check("one display panel", window.FindControl<Border>("DisplaySettingsPanel") is not null);
    ToggleButton Named(string content) => window.GetVisualDescendants().OfType<ToggleButton>().First(button => Equals(button.Content, content));
    if (mac)
    {
        var apple = window.FindControl<StackPanel>("AppleSettingsPanel")!;
        Check("Apple Silicon settings shown", apple.IsEffectivelyVisible && Named("Metal").IsEffectivelyVisible);
        Check("engines of other platforms hidden", !Named("TensorRT").IsEffectivelyVisible && !Named("DirectML").IsEffectivelyVisible);
        Check("Metal read from the bank", Named("Metal").IsChecked == true && Named("Core ML (native)").IsChecked == false && Named("Core ML (ONNX Runtime)").IsChecked == false);
        Check("Metal: GPU selected, the other single targets greyed",
            Named("GPU").IsChecked == true && !Named("Neural Engine (NPU)").IsEnabled && !Named("CPU").IsEnabled && !Named("Auto").IsEnabled && Named("GPU + Neural Engine").IsEnabled);
        Named("Core ML (native)").IsChecked = true; Dispatcher.UIThread.RunJobs();
        Check("choosing Core ML (native) reaches the settings", vm.AnimeJaNaiConf.AppleEngine == "CoreMLNative" && Named("Metal").IsChecked == false);
        Check("Core ML: every compute target can be chosen", Named("Neural Engine (NPU)").IsEnabled && Named("CPU").IsEnabled && Named("Auto").IsEnabled);
        Named("Neural Engine (NPU)").IsChecked = true; Dispatcher.UIThread.RunJobs();
        Named("Areas of the frame").IsChecked = true; Dispatcher.UIThread.RunJobs();
        string saved = File.ReadAllText(Path.Combine(data, "animejanai.conf"));
        Check("engine, target and reuse are saved for the player", vm.AnimeJaNaiConf.AppleDevice == "NPU" && Named("GPU").IsChecked == false
            && saved.Contains("backend=CoreMLNative") && saved.Contains("apple_device=NPU") && saved.Contains("engine_reuse=tiles"));
        Named("GPU + Neural Engine").IsChecked = true; Dispatcher.UIThread.RunJobs();
        Named("Metal").IsChecked = true; Dispatcher.UIThread.RunJobs();
        Check("the pair GPU + Neural Engine is kept when moving to Metal", vm.AnimeJaNaiConf.AppleEngine == "Metal" && vm.AnimeJaNaiConf.AppleDevice == "GPU+NPU");
        Check("nothing of the component packs or the other platforms is offered",
            window.GetVisualDescendants().OfType<TabItem>().All(tab => !Equals(tab.Header, "Components") || !tab.IsVisible)
            && window.FindControl<Expander>("PlaybackSettingsExpander")?.IsVisible == false
            && !window.FindControl<Expander>("EngineSettingsExpander")!.IsEffectivelyVisible);
        // The settings added after the first macOS version: each must be written as soon as it
        // is chosen, with nothing else touched afterwards, and be there when the bank is read again.
        ToggleButton Rife(string content) => window.GetVisualDescendants().OfType<WrapPanel>()
            .First(row => row.Children.OfType<TextBlock>().Any(label => label.Text == "RIFE interpolation on"))
            .Children.OfType<ToggleButton>().First(button => Equals(button.Content, content));
        string Bank() => File.ReadAllText(Path.Combine(data, "animejanai.conf"));
        Named("Core ML (native)").IsChecked = true; Dispatcher.UIThread.RunJobs();
        Named("GPU + Neural Engine + CPU").IsChecked = true; Dispatcher.UIThread.RunJobs();
        Check("GPU + Neural Engine + CPU is saved when chosen", Bank().Contains("apple_device=GPU+NPU+CPU"));
        Check("RIFE processor starts on Auto", Rife("Auto").IsChecked == true && Bank().Contains("engine_rife_device=auto"));
        Rife("GPU").IsChecked = true; Dispatcher.UIThread.RunJobs();
        Check("the RIFE processor is saved when chosen", Bank().Contains("engine_rife_device=gpu") && Rife("Auto").IsChecked == false);
        Check("the RIFE processor is still there when the bank is read again", vm.ReadAnimeJaNaiConf(Path.Combine(data, "animejanai.conf")).RifeDevice == "gpu");
        Rife("Neural Engine (NPU)").IsChecked = true; Dispatcher.UIThread.RunJobs();
        Check("changing the RIFE processor again is saved too", Bank().Contains("engine_rife_device=npu"));
        var held = window.GetVisualDescendants().OfType<CheckBox>().First(box => (box.Content as string ?? "").StartsWith("Balanced model on drawings"));
        Check("Balanced on held drawings is not offered with reuse by areas", !held.IsEnabled);
        Named("Whole frames").IsChecked = true; Dispatcher.UIThread.RunJobs();
        Check("Balanced on held drawings is offered with whole-frame reuse", held.IsEnabled);
        held.IsChecked = true; Dispatcher.UIThread.RunJobs();
        var read = vm.ReadAnimeJaNaiConf(Path.Combine(data, "animejanai.conf"));
        Check("Balanced on held drawings is saved when ticked and read again", Bank().Contains("engine_boost=yes") && read.EngineBoost && read.RifeDevice == "npu" && read.AppleDevice == "GPU+NPU+CPU");
    }
    foreach (string name in new[] { "HdrAppearanceExpander", "PlaybackSettingsExpander", "EngineSettingsExpander", "DiagnosticsExpander" })
        Check(name + " initially collapsed", window.FindControl<Expander>(name)?.IsExpanded == false);
    var automatic = window.GetVisualDescendants().OfType<ToggleButton>()
        .First(button => button.DataContext == vm.HdrSettings.Display && Equals(button.Content, "Auto"));
    Check("automatic peak selected through binding", automatic.IsChecked == true);
    var oled = window.GetVisualDescendants().OfType<CheckBox>().Single(box => Equals(box.Content, "This display is OLED"));
    Check("OLED unchecked through binding", oled.IsChecked == false);
    oled.Focus(); window.KeyPressQwerty(PhysicalKey.Space, RawInputModifiers.None); window.KeyReleaseQwerty(PhysicalKey.Space, RawInputModifiers.None);
    Check("OLED keyboard edit reaches common display", vm.HdrSettings.Display.Oled);
    oled.Focus(); window.KeyPressQwerty(PhysicalKey.Space, RawInputModifiers.None); window.KeyReleaseQwerty(PhysicalKey.Space, RawInputModifiers.None);
    Check("OLED keyboard toggle off", !vm.HdrSettings.Display.Oled);
    string output = args.Length > 0 ? Path.GetFullPath(args[0]) : Path.Combine(data, "settings.png");
    Directory.CreateDirectory(Path.GetDirectoryName(output)!);
    using (var frame = window.CaptureRenderedFrame())
    {
        Check("settings frame renders", frame is not null);
        frame!.Save(output);
    }
    var advanced = window.FindControl<Expander>("HdrAppearanceExpander")!;
    advanced.IsExpanded = true; Dispatcher.UIThread.RunJobs();
    ToggleButton Toggle(string content) => window.GetVisualDescendants().OfType<ToggleButton>().First(button => Equals(button.Content, content));
    if (!mac) Check("RTX Video HDR follows the NVIDIA detection", Toggle("RTX Video HDR").IsEnabled == vm.RtxHdrSelectable);
    if (OperatingSystem.IsLinux() && !mac)
    {
        Check("MIGraphX follows the AMD detection", Toggle("MIGraphX").IsEnabled == vm.MiGraphXSelectable);
        Check("OpenVINO follows the Intel detection", Toggle("OpenVINO").IsEnabled == vm.OpenVinoSelectable);
        window.FindControl<Expander>("PlaybackSettingsExpander")!.IsExpanded = true; Dispatcher.UIThread.RunJobs();
        var smooth = window.GetVisualDescendants().OfType<CheckBox>().Single(box => Equals(box.Content, "NVIDIA Smooth Motion (driver frame generation)"));
        Check("Smooth Motion follows the NVIDIA driver detection", smooth.IsEnabled == vm.SmoothMotionSelectable);
    }
    var white = window.GetVisualDescendants().OfType<NumericUpDown>()
        .First(box => box.DataContext == vm && box.Value == vm.HdrSettings.SubtitleWhite);
    white.Value = 230;
    Check("advanced HDR edit reaches view model", vm.HdrSettings.SubtitleWhite == 230);
    vm.HdrSettings.Save();
    var reloaded = new HdrSettings(vm.HdrSettings.ConfPath);
    Check("UI edits survive save and reload", reloaded.SubtitleWhite == 230 && !reloaded.Display.Oled);
    window.Width = 780; Dispatcher.UIThread.RunJobs();
    using (var frame = window.CaptureRenderedFrame()) frame!.Save(Path.ChangeExtension(output, ".narrow.png"));
    Check("small window keeps display controls inside client width", oled.TranslatePoint(new Point(oled.Bounds.Width, 0), window)?.X <= window.ClientSize.Width);
    if (!OperatingSystem.IsWindows())
    {
        // "Run Benchmarks" on Linux and macOS: the launcher's output must reach the progress window.
        Directory.CreateDirectory(Path.Combine(data, "benchmarks"));
        File.WriteAllText(Path.Combine(data, "benchmarks", "animejanai_benchmark_all.sh"),
            "#!/bin/sh\nprintf 'Performance @ 1280x720 '\nsleep 1\necho '123.4 fps'\necho 'to stderr' >&2\n");
        typeof(MainWindow).GetMethod("ShowBenchmarkWindow", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .Invoke(window, new object?[] { null });
        string shown = "";
        for (int i = 0; i < 300 && !shown.Contains("Finished"); i++)
        {
            Thread.Sleep(100); Dispatcher.UIThread.RunJobs();
            var progress = window.OwnedWindows.FirstOrDefault(w => w.Title == (mac ? "AnimeJaNai engine benchmark" : "AnimeJaNai playback benchmark"));
            shown = (progress?.Content as TextBox)?.Text ?? "";
        }
        Check("benchmark output shown live, with errors and the end", shown.Contains("Performance @ 1280x720 123.4 fps") && shown.Contains("to stderr") && shown.Contains("Finished"));
        foreach (var owned in window.OwnedWindows.ToList()) owned.Close();
    }
    window.Close();
    return 0;
}
catch (Exception e) { Console.Error.WriteLine(e); return 1; }
finally { Environment.SetEnvironmentVariable("ANIMEJANAI_DATA_DIR", null); Directory.Delete(data, true); }
