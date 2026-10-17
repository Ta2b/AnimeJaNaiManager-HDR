using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace AnimeJaNaiConfEditor.ViewModels;

internal static class IniPreservingSave
{
    // Retain comments and unknown options while allowing removal of managed optional keys.
    internal static string Merge(string original, string generated)
    {
        var sections = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
        string section = "";
        foreach (string line in generated.Replace("\r\n", "\n").Split('\n'))
        {
            var heading = Regex.Match(line, @"^\s*\[([^]]+)\]");
            if (heading.Success) { section = heading.Groups[1].Value; sections.TryAdd(section, new()); }
            var key = Regex.Match(line, @"^\s*([^#;=\s]+)\s*=");
            if (key.Success) { sections.TryAdd(section, new()); sections[section][key.Groups[1].Value] = line; }
        }
        var result = new List<string>();
        section = "";
        void Flush()
        {
            if (!sections.TryGetValue(section, out var remaining)) return;
            result.AddRange(remaining.Values); remaining.Clear();
        }
        string[] globals = ["config_version", "backend", "backend_auto_fallback", "logging", "default_slot",
            "sub_render_mode", "display_rate_match", "trt_engine_settings", "quality_preset", "balanced_preset", "performance_preset", "default_preset"];
        foreach (string line in original.Replace("\r\n", "\n").TrimEnd('\n').Split('\n'))
        {
            var heading = Regex.Match(line, @"^\s*\[([^]]+)\]");
            if (heading.Success) { Flush(); section = heading.Groups[1].Value; result.Add(line); continue; }
            var key = Regex.Match(line, @"^\s*([^#;=\s]+)\s*=");
            if (key.Success)
            {
                string name = key.Groups[1].Value;
                if (sections.TryGetValue(section, out var remaining) && remaining.Remove(name, out var replacement))
                {
                    // Keep inline comments as well as full comment lines.
                    var comment = Regex.Match(line, @"\s+[#;].*$");
                    result.Add(replacement + (comment.Success ? comment.Value : "")); continue;
                }
                if ((section == "global" && globals.Contains(name)) ||
                    (Regex.IsMatch(section, @"^slot_\d+$") && (name == "profile_name" || Regex.IsMatch(name, @"^chain_\d+_")))) continue;
            }
            result.Add(line);
        }
        Flush();
        foreach (var entry in sections.Where(s => s.Value.Count > 0))
        {
            result.Add("[" + entry.Key + "]"); result.AddRange(entry.Value.Values);
        }
        return string.Join(original.Contains("\r\n") ? "\r\n" : "\n", result) + "\n";
    }
}
