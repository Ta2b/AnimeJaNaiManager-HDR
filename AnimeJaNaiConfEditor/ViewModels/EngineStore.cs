using ReactiveUI;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace AnimeJaNaiConfEditor.ViewModels
{
    // TensorRT engines built on first play (animejanai/onnx, animejanai/rife). Names end with
    // ".trt-<version>.gpu-<GPU name>.engine". Engines for another TensorRT version or another
    // GPU can never be loaded again; deleting any engine is safe (it is rebuilt when needed).
    public class EngineStore : ReactiveObject
    {
        private readonly string _dataDir;
        public EngineStore(string dataDir) { _dataDir = dataDir; }

        private string _summary = "";
        public string Summary { get => _summary; private set => this.RaiseAndSetIfChanged(ref _summary, value); }

        private bool _hasOutdated;
        public bool HasOutdated { get => _hasOutdated; private set => this.RaiseAndSetIfChanged(ref _hasOutdated, value); }

        private bool _hasEngines;
        public bool HasEngines { get => _hasEngines; private set => this.RaiseAndSetIfChanged(ref _hasEngines, value); }

        // Two-step confirmation for "delete all": the first click arms it for a few seconds.
        private bool _confirmDeleteAll;
        public bool ConfirmDeleteAll
        {
            get => _confirmDeleteAll;
            private set
            {
                this.RaiseAndSetIfChanged(ref _confirmDeleteAll, value);
                this.RaisePropertyChanged(nameof(DeleteAllText));
            }
        }
        public string DeleteAllText => ConfirmDeleteAll ? "Click again to delete all engines" : "Delete All Engines";

        public string? GpuName { get; set; }

        private IEnumerable<FileInfo> Engines()
        {
            foreach (var sub in new[] { "onnx", "rife" })
            {
                var dir = Path.Combine(_dataDir, sub);
                if (!Directory.Exists(dir)) continue;
                foreach (var f in new DirectoryInfo(dir).EnumerateFiles("*.engine")) yield return f;
            }
        }

        private string? CurrentTrt()
        {
            // components.json at the install root: {"installed": {"trt-runtime": "trt-11.3.0.99", ...}}
            try
            {
                var path = Path.Combine(_dataDir, "..", "components.json");
                using var doc = JsonDocument.Parse(File.ReadAllText(path));
                var v = doc.RootElement.GetProperty("installed").GetProperty("trt-runtime").GetString() ?? "";
                var parts = v.Split('.');
                return parts.Length >= 3 ? string.Join('.', parts.Take(3)) : null;  // "trt-11.3.0"
            }
            catch { return null; }
        }

        private bool IsOutdated(FileInfo f, string? trt, string? gpuToken)
        {
            var name = f.Name;
            if (trt != null && name.Contains(".trt-") && !name.Contains("." + trt + ".")) return true;
            int at = name.IndexOf(".gpu-", StringComparison.Ordinal);
            if (gpuToken != null && at >= 0)
            {
                // ".gpu-NVIDIA-GeForce-RTX-4070-Ti-sm89.engine" -> "NVIDIA-GeForce-RTX-4070-Ti"
                var gpu = name.Substring(at + 5);
                if (gpu.EndsWith(".engine", StringComparison.OrdinalIgnoreCase)) gpu = gpu[..^7];
                int sm = gpu.LastIndexOf("-sm", StringComparison.OrdinalIgnoreCase);
                if (sm > 0) gpu = gpu[..sm];
                // Lenient both ways: vendor prefixes differ between NVML and TensorRT.
                if (!gpu.Contains(gpuToken, StringComparison.OrdinalIgnoreCase) &&
                    !gpuToken.Contains(gpu, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        private string? GpuToken() =>
            string.IsNullOrWhiteSpace(GpuName) ? null : GpuName.Trim().Replace(' ', '-');

        public void Refresh()
        {
            var all = Engines().ToList();
            var trt = CurrentTrt();
            var gpu = GpuToken();
            var outdated = all.Where(f => IsOutdated(f, trt, gpu)).ToList();
            HasEngines = all.Count > 0;
            HasOutdated = outdated.Count > 0;
            Summary = all.Count == 0
                ? "No TensorRT engine built yet."
                : $"{all.Count} engines, {all.Sum(f => f.Length) / 1048576.0:0} MB" +
                  (outdated.Count > 0 ? $" — {outdated.Count} outdated ({outdated.Sum(f => f.Length) / 1048576.0:0} MB)" : " — all current");
        }

        private int Delete(IEnumerable<FileInfo> files)
        {
            int failed = 0;
            foreach (var f in files)
            {
                try
                {
                    f.Delete();
                    var log = Path.ChangeExtension(f.FullName, ".build.log");
                    if (File.Exists(log)) File.Delete(log);
                }
                catch { failed++; }  // in use by a running player: kept
            }
            return failed;
        }

        public void DeleteOutdated()
        {
            var trt = CurrentTrt();
            var gpu = GpuToken();
            Delete(Engines().Where(f => IsOutdated(f, trt, gpu)).ToList());
            Refresh();
        }

        public async void DeleteAll()
        {
            if (!ConfirmDeleteAll)
            {
                ConfirmDeleteAll = true;
                await Task.Delay(5000);
                ConfirmDeleteAll = false;
                return;
            }
            ConfirmDeleteAll = false;
            int failed = Delete(Engines().ToList());
            Refresh();
            if (failed > 0) Summary += $" — {failed} in use, close the player and retry";
        }
    }
}
