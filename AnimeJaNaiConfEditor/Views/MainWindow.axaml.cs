using AnimeJaNaiConfEditor.Services;
using AnimeJaNaiConfEditor.ViewModels;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using ReactiveUI.Avalonia;
using FluentAvalonia.UI.Controls;
using FluentAvalonia.UI.Windowing;
using Material.Icons.Avalonia;
using ReactiveUI;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reactive.Linq;
using System.Threading.Tasks;

namespace AnimeJaNaiConfEditor.Views
{
    public partial class MainWindow : FAAppWindow
    {
        public MainWindow()
        {
            AvaloniaXamlLoader.Load(this);
            Closing += MainWindow_Closing;
            Opened += MainWindow_Opened;
        }

        private void MainWindow_Opened(object? sender, EventArgs e)
        {
            if (DataContext is MainWindowViewModel vm)
            {

            }
        }

        private async void MainWindow_Closing(object? sender, WindowClosingEventArgs e)
        {
            if (DataContext is MainWindowViewModel vm)
            {

            }
        }

        private async void ImportFullConfButtonClick(object? sender, RoutedEventArgs e)
        {
            if (DataContext is MainWindowViewModel vm)
            {
                // Get top level from the current control. Alternatively, you can use Window reference instead.
                var topLevel = GetTopLevel(this);

                // Start async operation to open the dialog.
                var storageProvider = topLevel.StorageProvider;

                var files = await storageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
                {
                    Title = "Import Profile Conf File",
                    AllowMultiple = false,
                    FileTypeFilter = new FilePickerFileType[] { new("AnimeJaNai Conf File") { Patterns = new[] { "*.conf" }, MimeTypes = new[] { "*/*" } }, FilePickerFileTypes.All },
                    SuggestedStartLocation = await storageProvider.TryGetFolderFromPathAsync(vm.BackupPath),
                });

                if (files.Count >= 1)
                {

                    var inPath = files[0].TryGetLocalPath();

                    if (inPath != null)
                    {
                        var td = new FATaskDialog
                        {
                            Title = "Confirm Full Conf Import",
                            ShowProgressBar = false,
                            Content = "The following full conf file will be imported. All configuration settings will be backed up and then all configuration settings for ALL PROFILES will be replaced with the imported conf file.\n\n" +
    inPath,
                            Buttons =
            {
                FATaskDialogButton.OKButton,
                FATaskDialogButton.CancelButton
            }
                        };


                        td.Closing += async (s, e) =>
                        {
                            if ((FATaskDialogStandardResult)e.Result == FATaskDialogStandardResult.OK)
                            {
                                var deferral = e.GetDeferral();

                                td.ShowProgressBar = true;
                                int value = 0;


                                await Task.Run(() =>
                                {
                                    vm.CheckAndDoBackup();
                                    // autoSave: true wires the imported slots/chains/models for
                                    // persistence; the explicit write commits the import itself to
                                    // animejanai.conf (assigning AnimeJaNaiConf does not trigger a save).
                                    vm.AnimeJaNaiConf = vm.ReadAnimeJaNaiConf(inPath, true);
                                    vm.WriteAnimeJaNaiConf();
                                });

                                deferral.Complete();
                            }
                        };

                        td.XamlRoot = this;
                        _ = await td.ShowAsync();
                    }

                }
            }
        }

        private async void ImportCurrentProfileConfButtonClick(object? sender, RoutedEventArgs e)
        {
            if (DataContext is MainWindowViewModel vm)
            {
                // Get top level from the current control. Alternatively, you can use Window reference instead.
                var topLevel = GetTopLevel(this);

                // Start async operation to open the dialog.
                var storageProvider = topLevel.StorageProvider;

                var files = await storageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
                {
                    Title = "Import Full Conf File",
                    AllowMultiple = false,
                    FileTypeFilter = new FilePickerFileType[] { new("AnimeJaNai Profile Conf File") { Patterns = new[] { "*.pconf" }, MimeTypes = new[] { "*/*" } }, FilePickerFileTypes.All },
                    SuggestedStartLocation = await storageProvider.TryGetFolderFromPathAsync(vm.BackupPath),
                });

                if (files.Count >= 1)
                {

                    var inPath = files[0].TryGetLocalPath();

                    if (inPath != null)
                    {
                        if (vm.CurrentSlot.Chains.Count == 0)
                        {
                            // blank slot, no need to prompt before importing and no need to do backup
                            vm.ReadAnimeJaNaiConfToCurrentSlot(inPath, true);
                        }
                        else
                        {
                            var td = new FATaskDialog
                            {
                                Title = "Confirm Profile Conf Import",
                                ShowProgressBar = false,
                                Content = $"The following profile conf file will be imported to the current profile {vm.CurrentSlot.ProfileName}. All configuration settings will be backed up and then all configuration settings for the current profile {vm.CurrentSlot.ProfileName} will be overwritten.\n\n" +
                                inPath,
                                Buttons =
                            {
                                FATaskDialogButton.OKButton,
                                FATaskDialogButton.CancelButton
                            }
                            };


                            td.Closing += async (s, e) =>
                            {
                                if ((FATaskDialogStandardResult)e.Result == FATaskDialogStandardResult.OK)
                                {
                                    var deferral = e.GetDeferral();

                                    td.ShowProgressBar = true;

                                    await Task.Run(() =>
                                    {
                                        vm.CheckAndDoBackup();
                                        vm.ReadAnimeJaNaiConfToCurrentSlot(inPath, true);
                                    });

                                    deferral.Complete();
                                }
                            };

                            td.XamlRoot = this;
                            _ = await td.ShowAsync();
                        }
                    }

                } 
            }
        }

        private async void CloneSelectedProfileToCurrentProfile(object? sender, RoutedEventArgs e)
        {
            if (DataContext is MainWindowViewModel vm)
            {
                if (vm.SelectedProfileToClone != null)
                {
                    if (vm.CurrentSlot.Chains.Count == 0)
                    {
                        // Current slot is blank - no need to ask user for confirmation before cloning and no need to do backup
                        vm.ReadAnimeJaNaiConfToCurrentSlot(
                            vm.ParsedAnimeJaNaiProfileConf(vm.SelectedProfileToClone),
                            true);
                    }
                    else
                    {
                        var td = new FATaskDialog
                        {
                            Title = "Confirm Profile Conf Import",
                            ShowProgressBar = false,
                            Content = $"The profile {vm.SelectedProfileToClone.ProfileName} will be cloned to the current profile {vm.CurrentSlot.ProfileName}. All configuration settings will be backed up and then all configuration settings for the current profile {vm.CurrentSlot.ProfileName} will be overwritten.",
                            Buttons =
                        {
                            FATaskDialogButton.OKButton,
                            FATaskDialogButton.CancelButton
                        }
                        };


                        td.Closing += async (s, e) =>
                        {
                            if ((FATaskDialogStandardResult)e.Result == FATaskDialogStandardResult.OK)
                            {
                                var deferral = e.GetDeferral();

                                td.ShowProgressBar = true;
                                int value = 0;


                                await Task.Run(() =>
                                {
                                    vm.CheckAndDoBackup();
                                    vm.ReadAnimeJaNaiConfToCurrentSlot(
                                        vm.ParsedAnimeJaNaiProfileConf(vm.SelectedProfileToClone),
                                        true);
                                });

                                deferral.Complete();
                            }
                        };

                        td.XamlRoot = this;
                        _ = await td.ShowAsync();
                    }
                }
            }
        }

        private async void ExportReferenceButtonClick(object? sender, RoutedEventArgs e)
        {
            if (DataContext is not MainWindowViewModel vm || !vm.Reference.CanExport) return;
            var selected = vm.Reference.SelectedSession;
            try
            {
                var topLevel = GetTopLevel(this) ?? throw new InvalidOperationException("Reference window is unavailable.");
                var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
                {
                    Title = "Export playback reference", DefaultExtension = "json",
                    SuggestedFileName = "animejanai-reference-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".json",
                    FileTypeChoices = new FilePickerFileType[] { new("JSON reference") { Patterns = new[] { "*.json" } } },
                });
                if (file == null) return;
                string? path = file.TryGetLocalPath();
                if (path == null) vm.Reference.DestinationError("Choose a local JSON destination.");
                else await vm.Reference.ExportAsync(path, selected);
            }
            catch (Exception error) { vm.Reference.DestinationError(error.Message); }
        }

        private async void ExportFullConfButtonClick(object? sender, RoutedEventArgs e)
        {
            if (DataContext is MainWindowViewModel vm)
            {
                // Get top level from the current control. Alternatively, you can use Window reference instead.
                var topLevel = GetTopLevel(this);

                var storageProvider = topLevel.StorageProvider;

                // Start async operation to open the dialog.
                var file = await storageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
                {
                    Title = "Export Full Conf File",
                    DefaultExtension = "conf",
                    FileTypeChoices = new FilePickerFileType[]
                    {
                    new("AnimeJaNai Conf File (*.conf)") { Patterns = new[] { "*.conf" } },
                    },
                    SuggestedStartLocation = await storageProvider.TryGetFolderFromPathAsync(vm.BackupPath),
                });

                if (file is not null)
                {

                    //vm.OutputFilePath = file.TryGetLocalPath() ?? "";

                    var outPath = file.TryGetLocalPath();

                    if (outPath != null)
                    {
                        vm.WriteAnimeJaNaiConf(outPath);
                    }

                }
            }
        }

        private async void ExportCurrentProfileConfButtonClick(object? sender, RoutedEventArgs e)
        {
            if (DataContext is MainWindowViewModel vm)
            {
                // Get top level from the current control. Alternatively, you can use Window reference instead.
                var topLevel = GetTopLevel(this);

                var storageProvider = topLevel.StorageProvider;

                // Start async operation to open the dialog.
                var file = await storageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
                {
                    Title = "Export Current Profile Conf File",
                    DefaultExtension = "conf",
                    FileTypeChoices = new FilePickerFileType[]
                    {
                    new("AnimeJaNai Profile Conf File (*.pconf)") { Patterns = new[] { "*.pconf" } },
                    },
                    SuggestedStartLocation = await storageProvider.TryGetFolderFromPathAsync(vm.BackupPath),
                });

                if (file is not null)
                {
                    var outPath = file.TryGetLocalPath();

                    if (outPath != null)
                    {
                        vm.WriteAnimeJaNaiCurrentProfileConf(outPath);
                    }
                }
            }
        }

        private async void RunBenchmarkButtonClick(object? sender, RoutedEventArgs e)
        {
            if (DataContext is not MainWindowViewModel vm) return;

            if (MainWindowViewModel.OnMac)
            {
                // macOS: the benchmark compares the engines and the processors, not resolutions
                // of one engine; the full run adds other sizes and the Balanced model.
                const string quick = "quick", full = "full";
                var mac = new FATaskDialog
                {
                    Title = "Measure the engines on this Mac",
                    Content = new TextBlock
                    {
                        TextWrapping = TextWrapping.Wrap,
                        MaxWidth = 460,
                        Text =
                            "Each engine (Core ML through ONNX Runtime, Core ML native, Metal) is timed on each processor " +
                            "(Auto, GPU, Neural Engine, GPU + Neural Engine) and the results are ranked.\n\n" +
                            "Quick: the Performance model at 720p, a few minutes. Full: also 540p, 1080p and the Balanced " +
                            "model, ten to fifteen minutes. The first run is longer: Core ML compiles each model once.\n\n" +
                            "Close the player and leave the Mac idle while it runs.",
                    },
                    Buttons =
                    {
                        new FATaskDialogButton("Quick", quick),
                        new FATaskDialogButton("Full", full),
                        FATaskDialogButton.CancelButton,
                    },
                };
                mac.XamlRoot = this;
                var choice = await mac.ShowAsync();
                if (Equals(choice, quick)) ShowBenchmarkWindow("rapide");
                else if (Equals(choice, full)) ShowBenchmarkWindow(null);
                return;
            }

            const string runResult = "run";
            var td = new FATaskDialog
            {
                Title = "Run playback benchmark",
                Content = new TextBlock
                {
                    TextWrapping = TextWrapping.Wrap,
                    MaxWidth = 460,
                    Text =
                        "This measures your real playback fps across several resolutions for the " +
                        "Balanced and Performance templates.\n\n" +
                        (OperatingSystem.IsWindows()
                            ? "mpv windows will open and close on their own while it runs. Do not close " +
                              "or click them, or the results will be invalid.\n\n"
                            : "It runs offscreen and shows its progress in a window. Close the player and " +
                              "leave the computer idle while it runs, or the results will be invalid.\n\n") +
                        "The first run builds a TensorRT engine per resolution (about a minute " +
                        "each, cached afterward), so the whole benchmark can take 10+ minutes depending on your hardware.",
                },
                Buttons =
                {
                    new FATaskDialogButton("Start benchmark", runResult),
                    FATaskDialogButton.CancelButton,
                },
            };
            td.XamlRoot = this;
            if (!Equals(await td.ShowAsync(), runResult)) return;
            // Windows: the launcher opens its own console. Linux has no console to open: the
            // benchmark's output is shown here while it runs.
            if (OperatingSystem.IsWindows()) vm.LaunchBenchmark();
            else ShowBenchmarkWindow(null);
        }

        private void ShowBenchmarkWindow(string? argument)
        {
            var output = new TextBox
            {
                IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.NoWrap,
                FontFamily = new Avalonia.Media.FontFamily(MainWindowViewModel.OnMac ? "Menlo" : "monospace"), FontSize = 13,
                BorderThickness = new Thickness(0),
                Text = MainWindowViewModel.OnMac
                    ? "Measuring the engines. Leave the player closed and the Mac idle while it runs.\n\n"
                    : "Starting the playback benchmark. Leave the player closed and the computer idle while it runs.\n\n",
            };
            var window = new Window
            {
                Title = MainWindowViewModel.OnMac ? "AnimeJaNai engine benchmark" : "AnimeJaNai playback benchmark", Width = 820, Height = 520,
                WindowStartupLocation = WindowStartupLocation.CenterOwner, Content = output,
            };
            var process = new System.Diagnostics.Process();
            process.StartInfo.FileName = "/bin/sh";
            // One pipe for both streams, so that errors appear where they happened.
            string script = Path.Combine(MainWindowViewModel.DataDir, "benchmarks", "animejanai_benchmark_all.sh");
            process.StartInfo.ArgumentList.Add("-c");
            process.StartInfo.ArgumentList.Add("exec /bin/sh \"$0\" \"$@\" 2>&1");
            process.StartInfo.ArgumentList.Add(script);
            if (argument != null) process.StartInfo.ArgumentList.Add(argument);
            process.StartInfo.WorkingDirectory = MainWindowViewModel.DataDir;
            process.StartInfo.UseShellExecute = false;
            process.StartInfo.RedirectStandardOutput = true;
            bool closed = false;
            void Append(string text) => Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                if (closed) return;
                output.Text += text;
                output.CaretIndex = output.Text.Length;
            });
            // Character by character: a cell prints its name, then its result a minute later.
            async Task Pump(StreamReader reader)
            {
                var buffer = new char[256];
                int read;
                while ((read = await reader.ReadAsync(buffer, 0, buffer.Length)) > 0) Append(new string(buffer, 0, read));
            }
            window.Closed += (_, _) =>
            {
                closed = true;
                try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch { }
                process.Dispose();
            };
            window.Show(this);
            _ = Task.Run(async () =>
            {
                try
                {
                    if (!File.Exists(script))
                    {
                        Append("The benchmark is not installed here (" + script + ").\nRestart the player once after an update, then try again.\n");
                        return;
                    }
                    process.Start();
                    await Pump(process.StandardOutput);
                    await process.WaitForExitAsync();
                    Append(process.ExitCode == 0
                        ? (MainWindowViewModel.OnMac ? "\nFinished. Choose the fastest engine and compute target in Global Settings.\n"
                            : "\nFinished. The results are saved; \"Submit to Catalog\" can share them.\n")
                        : "\nThe benchmark stopped (code " + process.ExitCode + ").\n");
                }
                catch (Exception ex) when (!closed) { Append("\nCould not run the benchmark: " + ex.Message + "\n"); }
                catch (Exception) { }
            });
        }

        private async void SubmitBenchmarkButtonClick(object? sender, RoutedEventArgs e)
        {
            if (DataContext is not MainWindowViewModel) return;

            var benchmarkTxt = Path.Combine(MainWindowViewModel.DataDir, "benchmark.txt");
            if (!File.Exists(benchmarkTxt))
            {
                await ShowInfoDialog("No benchmark results yet",
                    "Run the benchmark first (\"Run Benchmarks\"), then come back here to submit the results.");
                return;
            }

            BenchmarkSubmission sub;
            try
            {
                sub = await Task.Run(() =>
                {
                    var s = BenchmarkSubmission.FromBenchmarkFile(benchmarkTxt);
                    s.FillSystemInfo(MainWindowViewModel.DataDir);
                    return s;
                });
            }
            catch (Exception ex)
            {
                await ShowInfoDialog("Couldn't read benchmark results", ex.Message);
                return;
            }

            if (!sub.HasResults)
            {
                await ShowInfoDialog("No benchmark results found",
                    "benchmark.txt didn't contain any results. Try running the benchmark again.");
                return;
            }

            // The dialog's state: name/note are two-way bound; the JSON preview is derived
            // reactively from them, so it always shows exactly what will be sent.
            var model = new SubmitDialogModel(sub);

            var preview = new TextBox
            {
                IsReadOnly = true,
                AcceptsReturn = true,
                TextWrapping = TextWrapping.NoWrap,
                MaxHeight = 260,
                FontFamily = new FontFamily("Consolas, Cascadia Mono, monospace"),
                FontSize = 11,
            };
            preview.Bind(TextBox.TextProperty, new Binding(nameof(SubmitDialogModel.Preview)) { Mode = BindingMode.OneWay });

            var submittedBy = new TextBox
            {
                Watermark = "Optional: a name or handle to credit you (blank = anonymous)",
                MaxLength = 60,
                Margin = new Thickness(0, 8, 0, 0),
            };
            submittedBy.Bind(TextBox.TextProperty, new Binding(nameof(SubmitDialogModel.SubmittedBy)) { Mode = BindingMode.TwoWay });

            var note = new TextBox
            {
                Watermark = "Optional note: anything notable not already captured above (e.g. undervolt, cooling, laptop on battery)",
                AcceptsReturn = true,
                TextWrapping = TextWrapping.Wrap,
                MaxLength = 280,
                MinHeight = 60,
                MaxHeight = 90,
                Margin = new Thickness(0, 8, 0, 0),
            };
            note.Bind(TextBox.TextProperty, new Binding(nameof(SubmitDialogModel.Note)) { Mode = BindingMode.TwoWay });
            ScrollViewer.SetHorizontalScrollBarVisibility(note, ScrollBarVisibility.Disabled);
            ScrollViewer.SetVerticalScrollBarVisibility(note, ScrollBarVisibility.Auto);

            // Children inherit this DataContext, so the bindings above resolve against the model.
            var panel = new StackPanel { Width = 460, DataContext = model };
            panel.Children.Add(new TextBlock
            {
                TextWrapping = TextWrapping.Wrap,
                Text = "Below is the hardware data that will be sent to the community benchmark catalog. " +
                       "No account or login is required, and nothing else leaves your machine. " +
                       "You can optionally add your name and a note.",
            });
            panel.Children.Add(new HyperlinkButton
            {
                Content = "Browse the catalog first: " + BenchmarkSubmission.CatalogUrl,
                NavigateUri = new Uri(BenchmarkSubmission.CatalogUrl),
                Padding = new Thickness(0),
                Margin = new Thickness(0, 4, 0, 0),
                FontSize = 12,
            });
            panel.Children.Add(new TextBlock
            {
                Margin = new Thickness(0, 8, 0, 2),
                Opacity = .6,
                FontSize = 11,
                Text = "Data to submit:",
            });
            panel.Children.Add(preview);
            panel.Children.Add(submittedBy);
            panel.Children.Add(note);

            const string submitResult = "submit";
            var td = new FATaskDialog
            {
                Title = "Submit benchmark to community catalog",
                Content = panel,
                ShowProgressBar = false,
                Buttons =
                {
                    new FATaskDialogButton("Submit", submitResult),
                    FATaskDialogButton.CancelButton,
                },
            };

            (bool ok, string message)? outcome = null;
            td.Closing += async (s, ev) =>
            {
                if (!Equals(ev.Result, submitResult)) return;
                var deferral = ev.GetDeferral();
                td.ShowProgressBar = true;
                outcome = await sub.SubmitAsync();   // sub's name/note are kept current by SubmitDialogModel
                deferral.Complete();
            };

            td.XamlRoot = this;
            await td.ShowAsync();

            if (outcome is { } result)
                await ShowInfoDialog(result.ok ? "Submitted" : "Submission failed", result.message);
        }

        // Backs the submit dialog: name/note are two-way bound; the JSON preview is an
        // ObservableAsPropertyHelper derived from them, so it stays in sync with what will be sent.
        private sealed class SubmitDialogModel : ReactiveObject
        {
            private string _submittedBy = "";
            private string _note = "";
            private readonly ObservableAsPropertyHelper<string> _preview;

            public SubmitDialogModel(BenchmarkSubmission sub)
            {
                _preview = this.WhenAnyValue(x => x.SubmittedBy, x => x.Note)
                    .Do(t =>
                    {
                        sub.SubmittedBy = (t.Item1 ?? "").Trim();
                        sub.Note = (t.Item2 ?? "").Trim();
                    })
                    .Select(_ => sub.ToPreviewJson())
                    .ToProperty(this, x => x.Preview);
            }

            public string SubmittedBy { get => _submittedBy; set => this.RaiseAndSetIfChanged(ref _submittedBy, value); }
            public string Note { get => _note; set => this.RaiseAndSetIfChanged(ref _note, value); }
            public string Preview => _preview.Value;
        }

        private async Task ShowInfoDialog(string title, string message)
        {
            var td = new FATaskDialog
            {
                Title = title,
                Content = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, MaxWidth = 460 },
                Buttons = { FATaskDialogButton.OKButton },
            };
            td.XamlRoot = this;
            await td.ShowAsync();
        }
    }
}
