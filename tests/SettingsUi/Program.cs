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
File.WriteAllText(Path.Combine(data, "animejanai.conf"), "[global]\nconfig_version=3\nbackend=Vulkan\ndefault_slot=1\n[slot_1]\nprofile_name=Performance\nchain_1_rife=no\n");
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
    Check("RTX Video HDR follows the NVIDIA detection", Toggle("RTX Video HDR").IsEnabled == vm.RtxHdrSelectable);
    if (OperatingSystem.IsLinux())
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
    if (OperatingSystem.IsLinux())
    {
        // "Run Benchmarks" on Linux: the launcher's output must reach the progress window.
        Directory.CreateDirectory(Path.Combine(data, "benchmarks"));
        File.WriteAllText(Path.Combine(data, "benchmarks", "animejanai_benchmark_all.sh"),
            "#!/bin/sh\nprintf 'Performance @ 1280x720 '\nsleep 1\necho '123.4 fps'\necho 'to stderr' >&2\n");
        typeof(MainWindow).GetMethod("ShowBenchmarkWindow", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .Invoke(window, null);
        string shown = "";
        for (int i = 0; i < 300 && !shown.Contains("Finished"); i++)
        {
            Thread.Sleep(100); Dispatcher.UIThread.RunJobs();
            var progress = window.OwnedWindows.FirstOrDefault(w => w.Title == "AnimeJaNai playback benchmark");
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
