using AnimeJaNai.Platform;
using ReactiveUI;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Reactive;
using System.Text.Json;
using System.Threading.Tasks;

namespace AnimeJaNaiConfEditor.ViewModels;

public sealed class ReferenceExportViewModel : ViewModelBase
{
    readonly Func<string> root;
    public ObservableCollection<ReferenceExporter.Session> Sessions { get; } = new();
    ReferenceExporter.Session? selected;
    public ReferenceExporter.Session? SelectedSession
    {
        get => selected;
        set { this.RaiseAndSetIfChanged(ref selected, value); this.RaisePropertyChanged(nameof(CanExport)); }
    }
    bool busy;
    public bool IsBusy
    {
        get => busy;
        private set
        {
            this.RaiseAndSetIfChanged(ref busy, value);
            this.RaisePropertyChanged(nameof(NotBusy)); this.RaisePropertyChanged(nameof(CanExport));
        }
    }
    public bool NotBusy => !IsBusy;
    public bool CanExport => NotBusy && SelectedSession != null;
    string status = "Open a video in the player, then refresh sessions.";
    public string Status { get => status; private set => this.RaiseAndSetIfChanged(ref status, value); }
    public ReactiveCommand<Unit, Unit> Refresh { get; }
    public ReferenceExportViewModel(Func<string> root)
    {
        this.root = root;
        Refresh = ReactiveCommand.CreateFromTask(() => RefreshAsync());
    }
    public async Task RefreshAsync(IEnumerable<string>? endpoints = null)
    {
        if (IsBusy) return;
        IsBusy = true;
        var previous = SelectedSession;
        try
        {
            var found = await ReferenceExporter.FindSessions(endpoints ?? ReferenceExporter.DiscoverEndpoints());
            SelectedSession = null; Sessions.Clear();
            foreach (var session in found.Sessions) Sessions.Add(session);
            if (previous != null)
                foreach (var session in Sessions) if (session.SameSelection(previous)) SelectedSession = session;
            if (SelectedSession == null && Sessions.Count == 1) SelectedSession = Sessions[0];
            Status = Sessions.Count == 0 ? "No video session found. Check that the player is open."
                : Sessions.Count == 1 ? "Session found. Export the observed playback reference."
                : "Choose the video session to export.";
            if (found.Errors.Length != 0) Status += "\nUnavailable sessions: " + string.Join("; ", found.Errors);
        }
        catch (Exception e) { SelectedSession = null; Sessions.Clear(); Status = "Cannot refresh sessions: " + e.Message; }
        finally { IsBusy = false; }
    }
    public async Task<bool> ExportAsync(string output, ReferenceExporter.Session? expectedSelection = null)
    {
        if (IsBusy) return false;
        var session = SelectedSession;
        if (session == null) { Status = "Select a video session first."; return false; }
        if (expectedSelection != null && !session.SameSelection(expectedSelection))
        { Status = "Selection changed. Choose the session again; no report was written."; return false; }
        IsBusy = true;
        try
        {
            if (File.Exists(output)) throw new IOException("Destination already exists. Choose a new JSON filename; the existing file was kept.");
            var report = await ReferenceExporter.CaptureSelected(root(), session);
            ReferenceExporter.WriteNew(output, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
            Status = "Reference exported to " + Path.GetFullPath(output)
                + "\nPartial observation: loaded weights, intermediate stages and effective precision may be unreported. See limitations in the report.";
            return true;
        }
        catch (Exception e) { Status = "Reference export failed: " + e.Message; return false; }
        finally { IsBusy = false; }
    }
    public void DestinationError(string message) => Status = "Reference export failed: " + message;
}
