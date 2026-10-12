# Global Settings UI regressions

Requires .NET 10. Runs the real compiled view with Avalonia Headless and Skia,
without opening a desktop window or using the GPU inference engines.

```sh
dotnet run --project tests/SettingsUi -p:RuntimeIdentifier=linux-x64 -- artifacts/settings.png
```

Use `win-x64` on Windows. The temporary data directory is isolated from the
installed player. Checks automatic common display bindings, collapsed advanced
controls, keyboard OLED edits, save/reload and layout at a narrow width. The
rendered full and narrow frames are saved for visual review, rather than used
as platform-sensitive pixel baselines. The UI test runs in both platform jobs.
