using Avalonia.Collections;
using ReactiveUI;
using System;
using System.Linq;
using System.Collections.Specialized;
using System.Reactive.Linq;
using System.Reactive.Disposables;

namespace AnimeJaNaiConfEditor.ViewModels;

public partial class MainWindowViewModel
{
    public bool ProfilesSelected => SelectedTabIndex == 0;
    public bool PreparationSelected => SelectedTabIndex == 1;
    public bool EnginesSelected => SelectedTabIndex == 2;
    public bool SettingsSelected => SelectedTabIndex == 3;
    public void NavigateProfiles() => SelectedTabIndex = 0;
    public void ShowPreparation() => SelectedTabIndex = 1;
    public void ShowEngines() => SelectedTabIndex = 2;
    public void ShowPreparationEngineSettings() => ShowEngines();

    public AvaloniaList<UpscaleSlot> VisibleProfiles { get; } = [];
    public UpscaleSlot? SelectedProfile
    {
        get => CurrentSlot;
        set { if (value is not null && value.SlotNumber != SelectedSlotNumber) HandleShowCustomProfile(value.SlotNumber); }
    }
    public bool HasSelectedProfile => CurrentSlot is not null && VisibleProfiles.Contains(CurrentSlot);
    public bool CanAddProfile => Enumerable.Range(1, 9).Any(n => FreeProfileNumber(n.ToString()));
    public string ProfileCapacityNotice => CanAddProfile ? "Up to 9 playback profiles. Your names, your choices." : "All 9 playback profiles are in use. Export a profile before replacing it.";
    public string DefaultProfileAction => CurrentSlot?.IsSelectedMpvProfile == true ? "Startup profile · click to turn off" : "Use at startup";
    public string ProfileOverview => CurrentSlot?.Chains.Count switch
    {
        0 => "Add a rule, then choose your first processing step.",
        1 => "One processing chain. Steps run in the order shown below.",
        int n => $"{n} rules. Each rule has its own processing chain and source conditions.",
        _ => ""
    };
    private bool FreeProfileNumber(string number) => AnimeJaNaiConf.UpscaleSlots.FirstOrDefault(p => p.SlotNumber == number) is not { } p || !NamedOrConfigured(p);
    private static bool NamedOrConfigured(UpscaleSlot p) => p.Chains.Count > 0 || (!string.IsNullOrWhiteSpace(p.ProfileName) && p.ProfileName != "New Profile");

    private CompositeDisposable _profileObservers = new();

    private void InitializeNavigation()
    {
        this.WhenAnyValue(x => x.AnimeJaNaiConf).Subscribe(conf =>
        {
            _profileObservers.Dispose();
            _profileObservers = new CompositeDisposable();
            foreach (var slot in conf.UpscaleSlots) ObserveProfile(slot);
            NotifyCollectionChangedEventHandler handler = (_, e) =>
            {
                if (e.NewItems is not null) foreach (UpscaleSlot slot in e.NewItems) ObserveProfile(slot);
                RefreshProfileList();
            };
            conf.UpscaleSlots.CollectionChanged += handler;
            _profileObservers.Add(Disposable.Create(() => conf.UpscaleSlots.CollectionChanged -= handler));
            RefreshProfileList();
        });
        RefreshNavigation();
    }
    private void ObserveProfile(UpscaleSlot slot)
    {
        _profileObservers.Add(slot.WhenAnyValue(p => p.ProfileName, p => p.Chains.Count).Subscribe(_ => RefreshProfileList()));
    }
    private void RefreshNavigation()
    {
        ShowGlobalSettings = SettingsSelected;
        foreach (string p in new[] { nameof(ProfilesSelected), nameof(PreparationSelected), nameof(EnginesSelected), nameof(SettingsSelected) }) this.RaisePropertyChanged(p);
    }
    private void RefreshProfileList()
    {
        var profiles = AnimeJaNaiConf.UpscaleSlots.Where(p => p.ShowSlot && NamedOrConfigured(p)).ToArray();
        // Keep item instances and selection stable while a name is being edited.
        foreach (var p in VisibleProfiles.Where(p => !profiles.Contains(p)).ToArray()) VisibleProfiles.Remove(p);
        foreach (var p in profiles) if (!VisibleProfiles.Contains(p)) VisibleProfiles.Add(p);
        if (!VisibleProfiles.Contains(CurrentSlot) && VisibleProfiles.FirstOrDefault() is { } first)
        { ShowCustomProfiles = true; SelectedSlotNumber = first.SlotNumber; }
        RefreshProfileSelection();
    }
    private void RefreshProfileSelection()
    {
        foreach (string p in new[] { nameof(SelectedProfile), nameof(HasSelectedProfile), nameof(CanAddProfile), nameof(ProfileCapacityNotice), nameof(DefaultProfileAction), nameof(ProfileOverview), nameof(CurrentSlot) }) this.RaisePropertyChanged(p);
    }
    public void AddProfile()
    {
        int number = Enumerable.Range(1, 9).FirstOrDefault(n => FreeProfileNumber(n.ToString()));
        if (number == 0) return;
        _profileReadDepth++;
        try
        {
            var profile = AnimeJaNaiConf.UpscaleSlots.FirstOrDefault(p => p.SlotNumber == number.ToString());
            if (profile is null)
            {
                profile = new UpscaleSlot(true) { Vm = this, SlotNumber = number.ToString(), MpvProfileName = $"upscale-on-{number}" };
                AnimeJaNaiConf.UpscaleSlots.Add(profile);
            }
            profile.ProfileName = "Untitled profile";
            profile.HdrStageControlled = true;
            profile.Chains.Add(new UpscaleChain(true) { Vm = this, ChainNumber = "1" });
            HandleShowCustomProfile(profile.SlotNumber);
        }
        finally { _profileReadDepth--; }
        RefreshProfileList();
        WriteAnimeJaNaiConf();
    }
    public void DuplicateProfile()
    {
        if (!CanAddProfile || CurrentSlot is not { } source) return;
        var copy = ParsedAnimeJaNaiProfileConf(source);
        string name = source.ProfileName;
        AddProfile();
        _profileReadDepth++;
        try { ReadAnimeJaNaiConfToCurrentSlot(copy, true); CurrentSlot.ProfileName = name + " copy"; }
        finally { _profileReadDepth--; }
        RefreshProfileList();
        WriteAnimeJaNaiConf();
    }
    public void DeleteCurrentProfile()
    {
        if (CurrentSlot is not { } profile) return;
        CheckAndDoBackup();
        _profileReadDepth++;
        try
        {
            if (profile.IsSelectedMpvProfile) { AnimeJaNaiConf.DefaultSlot = 0; SelectedMpvProfile = "upscale-off"; }
            profile.ProfileName = "New Profile";
            profile.Chains.Clear();
        }
        finally { _profileReadDepth--; }
        RefreshProfileList();
        WriteAnimeJaNaiConf();
    }
}
