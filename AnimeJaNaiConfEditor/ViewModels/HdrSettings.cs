using ReactiveUI;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Reactive.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace AnimeJaNaiConfEditor.ViewModels
{
    // Display upscaling and HDR settings of the player, stored in
    // portable_config/script-opts/animejanai_hdr_auto.conf (mpv script-opts format: key=value).
    // That file is read at player startup by user-scripts/animejanai_hdr_auto.lua; this editor
    // only rewrites the values it knows and keeps every other line and comment untouched.
    //
    // UI values are percentages where the file stores 0..1 fractions, so the controls read
    // naturally ("Highlight Boost 85 %") while the file stays compatible with the scripts.
    public class HdrSettings : ReactiveObject
    {
        private static readonly CultureInfo INV = CultureInfo.InvariantCulture;
        private readonly string _path;
        private readonly ConfLines _conf;
        private bool _loading;

        public HdrSettings(string path)
        {
            _path = path;
            _conf = ConfLines.Load(path);
            _loading = true;
            ReadValues();
            _loading = false;

            // Coalesce bursts (NumericUpDown spinning, toggle groups) into one write.
            Changed.Where(_ => !_loading)
                   .Throttle(TimeSpan.FromMilliseconds(300))
                   .Subscribe(_ => Save());
        }

        public bool ConfFound => File.Exists(_path);
        public string ConfPath => _path;

        // ---------------------------------------------------------------- display upscaling
        private string _upscale = "auto";
        public string Upscale
        {
            get => _upscale;
            set
            {
                this.RaiseAndSetIfChanged(ref _upscale, value);
                this.RaisePropertyChanged(nameof(UpscaleAuto));
                this.RaisePropertyChanged(nameof(UpscaleNvidia));
                this.RaisePropertyChanged(nameof(UpscaleAmd));
                this.RaisePropertyChanged(nameof(UpscaleClassic));
                this.RaisePropertyChanged(nameof(UpscaleOff));
            }
        }
        public bool UpscaleAuto { get => Upscale == "auto"; set { if (value) Upscale = "auto"; else this.RaisePropertyChanged(); } }
        public bool UpscaleNvidia { get => Upscale == "nvidia"; set { if (value) Upscale = "nvidia"; else this.RaisePropertyChanged(); } }
        public bool UpscaleAmd { get => Upscale == "amd"; set { if (value) Upscale = "amd"; else this.RaisePropertyChanged(); } }
        public bool UpscaleClassic { get => Upscale == "classic"; set { if (value) Upscale = "classic"; else this.RaisePropertyChanged(); } }
        public bool UpscaleOff { get => Upscale == "no"; set { if (value) Upscale = "no"; else this.RaisePropertyChanged(); } }

        private string _targetResolution = "auto";
        public string TargetResolution
        {
            get => _targetResolution;
            // Kept as typed (partial input while editing); validated when written.
            set => this.RaiseAndSetIfChanged(ref _targetResolution, value ?? "");
        }

        // ---------------------------------------------------------------- extra frame generation
        // "amf": AMD AMF frame rate conversion x2 inside the player, after AnimeJaNai/RIFE (AMD only).
        // NVIDIA Smooth Motion / AMD Fluid Motion Frames are driver features, set outside the player.
        private string _frameGeneration = "off";
        public string FrameGeneration
        {
            get => _frameGeneration;
            set
            {
                this.RaiseAndSetIfChanged(ref _frameGeneration, value);
                this.RaisePropertyChanged(nameof(FrameGenerationOff));
                this.RaisePropertyChanged(nameof(FrameGenerationAmf));
            }
        }
        public bool FrameGenerationOff { get => FrameGeneration == "off"; set { if (value) FrameGeneration = "off"; else this.RaisePropertyChanged(); } }
        public bool FrameGenerationAmf { get => FrameGeneration == "amf"; set { if (value) FrameGeneration = "amf"; else this.RaisePropertyChanged(); } }

        // ---------------------------------------------------------------- RIFE and display refresh
        // RIFE factor p/q chosen per video so the output frame rate divides the refresh rate.
        private bool _rifeDisplaySync;
        public bool RifeDisplaySync { get => _rifeDisplaySync; set => this.RaiseAndSetIfChanged(ref _rifeDisplaySync, value); }

        private decimal? _rifeMaxFactor = 2;
        public decimal? RifeMaxFactor { get => _rifeMaxFactor; set => this.RaiseAndSetIfChanged(ref _rifeMaxFactor, Clamp(value, 1, 8, 2)); }

        // ---------------------------------------------------------------- SDR to HDR
        private bool _hdrConversion = true;
        public bool HdrConversion
        {
            get => _hdrConversion;
            set
            {
                this.RaiseAndSetIfChanged(ref _hdrConversion, value);
                this.RaisePropertyChanged(nameof(EnhancementControlsEnabled));
            }
        }

        private string _backend = "auto";
        public string Backend
        {
            get => _backend;
            set
            {
                this.RaiseAndSetIfChanged(ref _backend, value);
                this.RaisePropertyChanged(nameof(BackendAuto));
                this.RaisePropertyChanged(nameof(BackendRtx));
                this.RaisePropertyChanged(nameof(BackendMpv));
            }
        }
        public bool BackendAuto { get => Backend == "auto"; set { if (value) Backend = "auto"; else this.RaisePropertyChanged(); } }
        public bool BackendRtx { get => Backend == "rtx"; set { if (value) Backend = "rtx"; else this.RaisePropertyChanged(); } }
        public bool BackendMpv { get => Backend == "mpv"; set { if (value) Backend = "mpv"; else this.RaisePropertyChanged(); } }

        // ---------------------------------------------------------------- HDR enhancement
        private bool _enhance = true;
        public bool Enhance
        {
            get => _enhance;
            set
            {
                this.RaiseAndSetIfChanged(ref _enhance, value);
                this.RaisePropertyChanged(nameof(EnhancementControlsEnabled));
            }
        }
        public bool EnhancementControlsEnabled => HdrConversion && Enhance;

        private decimal? _blackDepth = 30;
        public decimal? BlackDepth { get => _blackDepth; set => this.RaiseAndSetIfChanged(ref _blackDepth, Clamp(value, 0, 80, 30)); }

        private decimal? _sdrWhite = 203;
        public decimal? SdrWhite { get => _sdrWhite; set => this.RaiseAndSetIfChanged(ref _sdrWhite, Clamp(value, 80, 400, 203)); }

        private decimal? _highlightsMpv = 85;
        public decimal? HighlightsMpv { get => _highlightsMpv; set => this.RaiseAndSetIfChanged(ref _highlightsMpv, Clamp(value, 0, 100, 85)); }

        private decimal? _highlightsRtx = 50;
        public decimal? HighlightsRtx { get => _highlightsRtx; set => this.RaiseAndSetIfChanged(ref _highlightsRtx, Clamp(value, 0, 100, 50)); }

        private decimal? _vibranceMpv = 30;
        public decimal? VibranceMpv { get => _vibranceMpv; set => this.RaiseAndSetIfChanged(ref _vibranceMpv, Clamp(value, 0, 80, 30)); }

        private decimal? _vibranceRtx = 20;
        public decimal? VibranceRtx { get => _vibranceRtx; set => this.RaiseAndSetIfChanged(ref _vibranceRtx, Clamp(value, 0, 80, 20)); }

        private decimal? _subtitleWhite = 200;
        public decimal? SubtitleWhite { get => _subtitleWhite; set => this.RaiseAndSetIfChanged(ref _subtitleWhite, Clamp(value, 80, 1000, 200)); }

        // Look presets: fill the enhancement values in one click (still editable afterwards).
        public void PresetNatural() => ApplyPreset(15, 50, 25, 15, 10);
        public void PresetVivid() => ApplyPreset(30, 85, 50, 30, 20);
        public void PresetMaximum() => ApplyPreset(80, 100, 100, 80, 80);
        private void ApplyPreset(decimal black, decimal hlMpv, decimal hlRtx, decimal vibMpv, decimal vibRtx)
        {
            BlackDepth = black;
            HighlightsMpv = hlMpv;
            HighlightsRtx = hlRtx;
            VibranceMpv = vibMpv;
            VibranceRtx = vibRtx;
        }

        private bool _enhanceNativeHdr;
        public bool EnhanceNativeHdr { get => _enhanceNativeHdr; set => this.RaiseAndSetIfChanged(ref _enhanceNativeHdr, value); }

        // ---------------------------------------------------------------- displays, per GPU
        public DisplaySettings Nvidia { get; } = new("nvidia");
        public DisplaySettings Amd { get; } = new("amd");

        // ---------------------------------------------------------------- persistence
        private void ReadValues()
        {
            Upscale = _conf.Get("vsr", "auto").ToLowerInvariant() switch
            {
                "yes" or "true" or "on" => "auto",
                "false" or "off" => "no",
                var v when v is "auto" or "nvidia" or "amd" or "classic" or "no" => v,
                _ => "auto",
            };
            TargetResolution = NormalizeResolution(_conf.Get("target_resolution", "auto"));
            FrameGeneration = _conf.Get("frame_generation", "off").ToLowerInvariant() == "amf" ? "amf" : "off";
            RifeDisplaySync = ParseBool(_conf.Get("rife_display_sync", "no"));
            RifeMaxFactor = Number(_conf.Get("rife_max_factor", "2"));
            SubtitleWhite = Number(_conf.Get("subtitle_white", "200"));
            HdrConversion = ParseBool(_conf.Get("hdr_conversion", "yes"));
            var backend = _conf.Get("hdr_backend", "auto").ToLowerInvariant();
            Backend = backend is "rtx" or "mpv" ? backend : "auto";
            Enhance = ParseBool(_conf.Get("grade", "yes"));
            BlackDepth = Percent(_conf.Get("shadows", "0.30"));
            SdrWhite = Number(_conf.Get("paper_white", "203"));
            HighlightsMpv = Percent(_conf.Get("highlights", "0.85"));
            HighlightsRtx = Percent(_conf.Get("highlights_rtx", "0.50"));
            VibranceMpv = Percent(_conf.Get("vibrance", "0.30"));
            VibranceRtx = Percent(_conf.Get("vibrance_rtx", "0.20"));
            EnhanceNativeHdr = ParseBool(_conf.Get("grade_native_hdr", "no"));
            Nvidia.Read(_conf, defaultMode: "follow", defaultPeak: "1000", defaultGamut: "display-p3", defaultOled: "yes");
            Amd.Read(_conf, defaultMode: "enable", defaultPeak: "auto", defaultGamut: "display-p3", defaultOled: "auto");
            Nvidia.Changed.Merge(Amd.Changed).Where(_ => !_loading)
                  .Throttle(TimeSpan.FromMilliseconds(300)).Subscribe(_ => Save());
        }

        private readonly object _saveLock = new();
        public void Save()
        {
            lock (_saveLock)
            {
                _conf.Set("vsr", Upscale);
                _conf.Set("target_resolution", NormalizeResolution(TargetResolution));
                _conf.Set("frame_generation", FrameGeneration);
                _conf.Set("rife_display_sync", RifeDisplaySync ? "yes" : "no");
                _conf.Set("rife_max_factor", ((int)(RifeMaxFactor ?? 2)).ToString(INV));
                _conf.Set("subtitle_white", ((int)(SubtitleWhite ?? 200)).ToString(INV));
                _conf.Set("hdr_conversion", HdrConversion ? "yes" : "no");
                _conf.Set("hdr_backend", Backend);
                _conf.Set("grade", Enhance ? "yes" : "no");
                _conf.Set("shadows", Fraction(BlackDepth));
                _conf.Set("paper_white", ((int)(SdrWhite ?? 203)).ToString(INV));
                _conf.Set("highlights", Fraction(HighlightsMpv));
                _conf.Set("highlights_rtx", Fraction(HighlightsRtx));
                _conf.Set("vibrance", Fraction(VibranceMpv));
                _conf.Set("vibrance_rtx", Fraction(VibranceRtx));
                _conf.Set("grade_native_hdr", EnhanceNativeHdr ? "yes" : "no");
                Nvidia.Write(_conf);
                Amd.Write(_conf);
                try
                {
                    if (_conf.Save(_path)) NotifyPlayer();
                }
                catch (Exception e)
                {
                    System.Diagnostics.Debug.WriteLine($"HDR settings not saved: {e.Message}");
                }
            }
        }

        // Live apply: mpv.conf sets input-ipc-server=mpvpipe. If the player is running, every
        // script re-reads the file (user-scripts react to animejanai-reload-options).
        // Settings that only matter when a video starts (RIFE factor) apply from the next file.
        private static void NotifyPlayer()
        {
            try
            {
                using var pipe = new NamedPipeClientStream(".", "mpvpipe", PipeDirection.Out);
                pipe.Connect(200);
                var line = Encoding.UTF8.GetBytes("{\"command\":[\"script-message\",\"animejanai-reload-options\"]}\n");
                pipe.Write(line, 0, line.Length);
                pipe.Flush();
            }
            catch (Exception)
            {
                // Player not running (or another pipe name): the file is read at the next start.
            }
        }

        // ---------------------------------------------------------------- helpers
        internal static bool ParseBool(string v) =>
            v.Trim().ToLowerInvariant() is "yes" or "true" or "1" or "on";

        private static decimal? Number(string v) =>
            decimal.TryParse(v.Trim(), NumberStyles.Float, INV, out var d) ? d : null;

        private static decimal? Percent(string v) =>
            Number(v) is decimal d ? Math.Round(d * 100m) : null;

        private static string Fraction(decimal? percent) =>
            ((percent ?? 0m) / 100m).ToString("0.00", INV);

        private static decimal? Clamp(decimal? v, decimal lo, decimal hi, decimal fallback) =>
            v is decimal d ? Math.Clamp(d, lo, hi) : fallback;

        private static string NormalizeResolution(string? v)
        {
            var s = (v ?? "").Trim().ToLowerInvariant().Replace(" ", "");
            return Regex.IsMatch(s, @"^\d{2,5}x\d{2,5}$") ? s : "auto";
        }
    }

    // Display description for the PC of one GPU vendor (*_nvidia or *_amd keys).
    public class DisplaySettings : ReactiveObject
    {
        private readonly string _suffix;
        public DisplaySettings(string suffix) { _suffix = suffix; }

        private bool _isThisPc;
        public bool IsThisPc { get => _isThisPc; set => this.RaiseAndSetIfChanged(ref _isThisPc, value); }

        // Windows HDR: "enable" = turned on by the player for playback, then restored; "follow" = untouched.
        private string _windowsHdr = "follow";
        public string WindowsHdr
        {
            get => _windowsHdr;
            set
            {
                this.RaiseAndSetIfChanged(ref _windowsHdr, value);
                this.RaisePropertyChanged(nameof(WindowsHdrEnable));
                this.RaisePropertyChanged(nameof(WindowsHdrFollow));
            }
        }
        public bool WindowsHdrEnable { get => WindowsHdr == "enable"; set { if (value) WindowsHdr = "enable"; else this.RaisePropertyChanged(); } }
        public bool WindowsHdrFollow { get => WindowsHdr == "follow"; set { if (value) WindowsHdr = "follow"; else this.RaisePropertyChanged(); } }

        private bool _peakAuto;
        public bool PeakAuto
        {
            get => _peakAuto;
            set
            {
                this.RaiseAndSetIfChanged(ref _peakAuto, value);
                this.RaisePropertyChanged(nameof(PeakManual));
                this.RaisePropertyChanged(nameof(PeakAutoChoice));
            }
        }
        // Toggle-button pair: clicking the active button keeps it active.
        public bool PeakManual { get => !PeakAuto; set { if (value) PeakAuto = false; else this.RaisePropertyChanged(); } }
        public bool PeakAutoChoice { get => PeakAuto; set { if (value) PeakAuto = true; else this.RaisePropertyChanged(); } }

        private decimal? _peak = 1000;
        public decimal? Peak { get => _peak; set => this.RaiseAndSetIfChanged(ref _peak, value is decimal d ? Math.Clamp(d, 100, 10000) : 1000); }

        private string _gamut = "display-p3";
        public string Gamut
        {
            get => _gamut;
            set
            {
                this.RaiseAndSetIfChanged(ref _gamut, value);
                this.RaisePropertyChanged(nameof(GamutAuto));
                this.RaisePropertyChanged(nameof(GamutP3));
                this.RaisePropertyChanged(nameof(GamutBt2020));
            }
        }
        public bool GamutAuto { get => Gamut == "auto"; set { if (value) Gamut = "auto"; else this.RaisePropertyChanged(); } }
        public bool GamutP3 { get => Gamut == "display-p3"; set { if (value) Gamut = "display-p3"; else this.RaisePropertyChanged(); } }
        public bool GamutBt2020 { get => Gamut == "bt.2020"; set { if (value) Gamut = "bt.2020"; else this.RaisePropertyChanged(); } }

        private bool _oled;
        public bool Oled { get => _oled; set => this.RaiseAndSetIfChanged(ref _oled, value); }

        // NVIDIA keeps the historical key name "mode"; AMD/Intel use "amd_mode".
        private string ModeKey => _suffix == "nvidia" ? "mode" : "amd_mode";

        internal void Read(ConfLines conf, string defaultMode, string defaultPeak, string defaultGamut, string defaultOled)
        {
            var mode = conf.Get(ModeKey, defaultMode).ToLowerInvariant();
            WindowsHdr = mode is "enable" or "follow" ? mode : defaultMode;
            var peak = conf.Get("peak_" + _suffix, defaultPeak).Trim().ToLowerInvariant();
            if (decimal.TryParse(peak, NumberStyles.Float, CultureInfo.InvariantCulture, out var nits))
            {
                PeakAuto = false;
                Peak = nits;
            }
            else
            {
                PeakAuto = true;
            }
            var gamut = conf.Get("gamut_" + _suffix, defaultGamut).ToLowerInvariant();
            Gamut = gamut is "auto" or "display-p3" or "bt.2020" ? gamut : "auto";
            Oled = conf.Get("oled_" + _suffix, defaultOled).Trim().ToLowerInvariant() == "yes";
        }

        internal void Write(ConfLines conf)
        {
            conf.Set(ModeKey, WindowsHdr);
            conf.Set("peak_" + _suffix, PeakAuto ? "auto" : ((int)(Peak ?? 1000)).ToString(CultureInfo.InvariantCulture));
            conf.Set("gamut_" + _suffix, Gamut);
            conf.Set("oled_" + _suffix, Oled ? "yes" : "auto");
        }
    }

    // Line-preserving key=value file: comments, blank lines, order and unknown keys survive a save.
    internal class ConfLines
    {
        private static readonly Regex KEY = new(@"^\s*([A-Za-z0-9_\-]+)\s*=(.*)$");
        private readonly List<string> _lines = new();
        private string _newline = Environment.NewLine;

        public static ConfLines Load(string path)
        {
            var conf = new ConfLines();
            if (File.Exists(path))
            {
                var text = File.ReadAllText(path, Encoding.UTF8);
                conf._newline = text.Contains("\r\n") ? "\r\n" : "\n";
                conf._lines.AddRange(text.Replace("\r\n", "\n").Split('\n'));
                if (conf._lines.Count > 0 && conf._lines[^1] == "")
                {
                    conf._lines.RemoveAt(conf._lines.Count - 1);
                }
            }
            else
            {
                conf._lines.Add("# Display upscaling and HDR settings, written by AnimeJaNai Manager (HDR).");
            }
            return conf;
        }

        private int Find(string key)
        {
            for (int i = 0; i < _lines.Count; i++)
            {
                var m = KEY.Match(_lines[i]);
                if (m.Success && m.Groups[1].Value == key) return i;
            }
            return -1;
        }

        public string Get(string key, string fallback)
        {
            var i = Find(key);
            if (i < 0) return fallback;
            var value = KEY.Match(_lines[i]).Groups[2].Value.Trim();
            return value.Length == 0 ? fallback : value;
        }

        public void Set(string key, string value)
        {
            var i = Find(key);
            var line = $"{key}={value}";
            if (i < 0) _lines.Add(line);
            else _lines[i] = line;
        }

        // Returns true when the file content changed.
        public bool Save(string path)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var text = string.Join(_newline, _lines) + _newline;
            if (File.Exists(path) && File.ReadAllText(path, Encoding.UTF8) == text) return false;
            var tmp = path + ".tmp";
            File.WriteAllText(tmp, text, new UTF8Encoding(false));
            File.Move(tmp, path, true);
            return true;
        }
    }
}
