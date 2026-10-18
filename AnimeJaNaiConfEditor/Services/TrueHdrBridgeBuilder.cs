using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace AnimeJaNaiConfEditor.Services;

// Builds libaji_rtx.so, the bridge between the TensorRT engine and NVIDIA TrueHDR, on this
// computer: it is compiled against the SDK the user imported, so no release can ship it.
// Needs a C++ compiler (g++, c++ or clang++) and the SDK headers.
public static class TrueHdrBridgeBuilder
{
    public const string Library = "libaji_rtx.so";

    public static string? Compiler()
    {
        var dirs = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(':', StringSplitOptions.RemoveEmptyEntries)
            .Concat(new[] { "/usr/bin", "/usr/local/bin" });
        foreach (string name in new[] { "g++", "c++", "clang++" })
            foreach (string dir in dirs)
            {
                string path = Path.Combine(dir, name);
                if (File.Exists(path)) return path;
            }
        return null;
    }

    // Headers of an SDK installed by NVIDIA's own tools (include/ and features/*/include/).
    public static (string Core, string Feature)? SystemHeaders(string sdkRoot)
    {
        string core = Path.Combine(sdkRoot, "include");
        if (!File.Exists(Path.Combine(core, "nvVideoEffects.h")) || !File.Exists(Path.Combine(core, "nvCVImage.h"))) return null;
        string feature = Path.Combine(sdkRoot, "features", "nvvfxtruehdr", "include");
        return File.Exists(Path.Combine(feature, "nvVFXTrueHDR.h")) ? (core, feature) : null;
    }

    // Compiles into a temporary file next to the target, then replaces the target atomically.
    public static async Task<string> BuildAsync(string compiler, (string Core, string Feature) headers, string sdkRoot,
        string inferenceDir, CancellationToken cancel)
    {
        Directory.CreateDirectory(inferenceDir);
        string work = Path.Combine(Path.GetTempPath(), "animejanai-truehdr-bridge-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);
        string temp = Path.Combine(inferenceDir, "." + Library + ".building-" + Guid.NewGuid().ToString("N"));
        try
        {
            string source = Path.Combine(work, "aji_rtx_vfx.cpp");
            using (var resource = typeof(TrueHdrBridgeBuilder).Assembly.GetManifestResourceStream("aji_rtx_vfx.cpp")
                ?? throw new InvalidDataException("The TrueHDR bridge source is missing from this Manager."))
            using (var file = File.Create(source)) await resource.CopyToAsync(file, cancel);
            string lib = Path.Combine(sdkRoot, "lib");
            var info = new ProcessStartInfo(compiler) { RedirectStandardError = true, RedirectStandardOutput = true, UseShellExecute = false };
            foreach (string arg in new[] {
                "-O2", "-fPIC", "-shared", "-std=c++17", "-fvisibility=hidden",
                "-I" + headers.Core, "-I" + headers.Feature, source, "-o", temp,
                "-L" + lib, "-lVideoFX", "-lNVCVImage", "-ldl",
                "-Wl,-rpath," + lib,
                "-Wl,-rpath-link," + string.Join(':', lib, Path.Combine(sdkRoot, "external", "cuda", "lib"), Path.Combine(sdkRoot, "external", "tensorrt", "lib")),
                "-Wl,--no-undefined" }) info.ArgumentList.Add(arg);
            using var process = Process.Start(info) ?? throw new IOException("The C++ compiler could not be started.");
            var error = process.StandardError.ReadToEndAsync(cancel);
            var output = process.StandardOutput.ReadToEndAsync(cancel);
            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancel))
            {
                timeout.CancelAfter(TimeSpan.FromMinutes(3));
                try { await process.WaitForExitAsync(timeout.Token); }
                catch (OperationCanceledException) { try { process.Kill(true); } catch (InvalidOperationException) { } throw; }
            }
            string log = (await error + await output).Trim();
            if (process.ExitCode != 0 || !File.Exists(temp))
                throw new IOException("The TrueHDR bridge did not compile: " + (log.Length > 600 ? log[..600] + "…" : log));
            File.Move(temp, Path.Combine(inferenceDir, Library), true);
            return Path.Combine(inferenceDir, Library);
        }
        finally
        {
            if (File.Exists(temp)) File.Delete(temp);
            try { Directory.Delete(work, true); } catch (IOException) { }
        }
    }
}
