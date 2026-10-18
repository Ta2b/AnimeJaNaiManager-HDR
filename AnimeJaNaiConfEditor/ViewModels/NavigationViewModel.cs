using Avalonia.Collections;
using ReactiveUI;
using System;
using System.Linq;
using System.Collections.Generic;
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
    // The preparation engine's settings are folded on the Engines page; this shortcut unfolds them.
    private bool _preparationEngineExpanded;
    public bool PreparationEngineExpanded { get => _preparationEngineExpanded; set => this.RaiseAndSetIfChanged(ref _preparationEngineExpanded, value); }
    public void ShowPreparationEngineSettings() { PreparationEngineExpanded = true; ShowEngines(); }

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
            profile.Chains.Add(new UpscaleChain(true) { Vm = this, ChainNumber = "1" });
            HandleShowCustomProfile(profile.SlotNumber);
        }
        finally { _profileReadDepth--; }
        RefreshProfileList();
        WriteAnimeJaNaiConf();
    }
    // Recommended profiles: the starter banks of the player (profiles/linux-nvidia for TensorRT
    // and the other ONNX engines, profiles/linux-amd for Vulkan), same names and contents. They
    // are read through the profile import, so they get the same steps as the shipped banks.
    // Added in free slots; a name already present is skipped.
    public string RecommendedProfilesHint => AnimeJaNaiConf.VulkanSelected
        ? "Starter profiles for Vulkan: 360p → 720p, 720p → 1440p and lighter ones, with or without RIFE ×2."
        : "Balanced 1080p → 2160p, Performance 1080p → 2160p + RIFE ×2, Performance 720p → 1440p (with or without RIFE ×2), and RIFE ×2 alone.";
    private static string RecommendedRule(int n, int minFps, int maxFps, string model, int height, string rife)
    {
        var lines = new List<string> { $"chain_{n}_min_resolution=0x0", $"chain_{n}_max_resolution=1920x1080", $"chain_{n}_min_fps={minFps}", $"chain_{n}_max_fps={maxFps}" };
        if (model.Length > 0)
        {
            if (height > 0) { lines.Add($"chain_{n}_model_1_resize_height_before_upscale={height}"); lines.Add($"chain_{n}_model_1_resize_factor_before_upscale=100"); }
            lines.Add($"chain_{n}_model_1_name={model}");
        }
        lines.Add($"chain_{n}_rife=" + (rife.Length > 0 ? "yes" : "no"));
        if (rife.Length > 0) lines.AddRange([$"chain_{n}_rife_model={rife}", $"chain_{n}_rife_factor_numerator=2", $"chain_{n}_rife_factor_denominator=1", $"chain_{n}_rife_before_upscale=yes"]);
        return string.Join("\n", lines);
    }
    internal List<(string Name, string Rules)> RecommendedProfiles()
    {
        var models = GetAllModels();
        string Pick(params string[] names) => names.FirstOrDefault(n => models.Contains(n)) ?? models.FirstOrDefault() ?? "";
        const string sharpBalanced = "2x_AnimeJaNai_HD_V3.1Sharp1_Balanced_SPANF3_b8f64_unshuffle_fp16", balancedStd = "2x_AnimeJaNai_HD_V3.1_Balanced_SPANF3_b8f64_unshuffle_fp16";
        const string sharpPerformance = "2x_AnimeJaNai_HD_V3.1Sharp1_Performance_SPANF3_b5f48_unshuffle_fp16", performanceStd = "2x_AnimeJaNai_HD_V3.1_Performance_SPANF3_b5f48_unshuffle_fp16";
        string balanced = Pick(sharpBalanced, balancedStd), performance = Pick(sharpPerformance, performanceStd), standard = Pick(performanceStd, sharpPerformance);
        if (AnimeJaNaiConf.VulkanSelected)
            return
            [
                ("Performance · 360p → 720p", RecommendedRule(1, 0, 61, standard, 360, "")),
                ("Performance · 360p → 720p + RIFE ×2", RecommendedRule(1, 0, 61, performance, 360, "46")),
                ("RIFE ×2 · source resolution", RecommendedRule(1, 0, 61, "", 0, "46")),
                ("Performance · 720p → 1440p", RecommendedRule(1, 0, 61, performance, 720, "")),
                ("Light · 240p → 480p", RecommendedRule(1, 0, 61, performance, 240, "")),
                ("Light · 180p → 360p + RIFE ×2", RecommendedRule(1, 0, 61, performance, 180, "46")),
            ];
        string rife = RifeModels.Select(RifeLabelToValue).Contains("426") ? "426" : RifeModels.Select(RifeLabelToValue).FirstOrDefault() ?? "426";
        return
        [
            ("Balanced · 1080p → 2160p", RecommendedRule(1, 0, 61, balanced, 0, "")),
            ("Performance · 1080p → 2160p + RIFE ×2", RecommendedRule(1, 0, 31, performance, 0, rife) + "\n" + RecommendedRule(2, 31, 61, performance, 0, "")),
            ("Performance · 720p → 1440p", RecommendedRule(1, 0, 61, performance, 720, "")),
            ("Performance · 720p → 1440p + RIFE ×2", RecommendedRule(1, 0, 31, performance, 720, rife) + "\n" + RecommendedRule(2, 31, 61, performance, 720, "")),
            ("RIFE ×2 · source resolution", RecommendedRule(1, 0, 31, "", 0, rife)),
        ];
    }
    public void AddRecommendedProfiles()
    {
        var added = new List<string>(); var skipped = new List<string>(); string? first = null;
        foreach (var (name, rules) in RecommendedProfiles())
        {
            if (AnimeJaNaiConf.UpscaleSlots.Any(p => p.ProfileName == name && NamedOrConfigured(p))) continue;
            if (!CanAddProfile) { skipped.Add(name); continue; }
            AddProfile();
            var parser = new Salaros.Configuration.ConfigParser("[slot]\nprofile_name=" + name + "\n" + rules + "\n");
            _profileReadDepth++;
            try
            {
                var chains = ReadChains(parser, "slot", true, OnMac ? AnimeJaNaiConf.AppleEngine : AnimeJaNaiConf.SelectedBackend.ToString(), "quality");
                CurrentSlot.ProfileName = name; CurrentSlot.Chains = chains;
            }
            finally { _profileReadDepth--; }
            first ??= CurrentSlot.SlotNumber; added.Add(name);
        }
        if (first != null)
        {
            HandleShowCustomProfile(first);
            // No startup profile yet: the first added one becomes it (same as "Use at startup").
            if ((AnimeJaNaiConf.DefaultSlot ?? 0) == 0) { SelectedMpvProfile = CurrentSlot.MpvProfileName; AnimeJaNaiConf.DefaultSlot = int.Parse(first); }
        }
        string notice = added.Count == 0 && skipped.Count == 0 ? "The recommended profiles are already in your list."
            : (added.Count > 0 ? "Added: " + string.Join(", ", added) + "." : "")
              + (skipped.Count > 0 ? (added.Count > 0 ? " " : "") + "No free place for: " + string.Join(", ", skipped) + " (9 profiles at most; delete one to add it)." : "");
        ProfileMigrationNotice = notice;
        this.RaisePropertyChanged(nameof(ProfileMigrationNotice)); this.RaisePropertyChanged(nameof(HasProfileMigrationNotice)); this.RaisePropertyChanged(nameof(HasAnyProfileNotice));
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
