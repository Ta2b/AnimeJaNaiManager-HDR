# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

AnimeJaNaiConfEditor is a Windows/Linux/macOS desktop GUI for configuring AnimeJaNai, an AI-powered anime video upscaling tool. It manages upscaling profiles, processing chains, and AI model settings that get written to an INI-style config file consumed by mpv/AnimeJaNai.

## Build Commands

```bash
dotnet build                     # Debug build
dotnet build -c Release          # Release build
dotnet publish -c Release        # Publish self-contained win-x64 executable
```

Regression checks: `dotnet run --project tests/ProfileRoundTrip` for persistence/migration/IPC;
`dotnet run --project tests/SettingsUi -p:RuntimeIdentifier=linux-x64 -- artifacts/settings.png`
for Avalonia headless keyboard/binding/layout checks (use win-x64 on Windows).
Both take `AJN_UI_PLATFORM=macos` to run as on a Mac from any machine.

## Platforms

`MainWindowViewModel.Platform` (`Os.Windows`, `Os.Linux`, `Os.MacOS`) decides what the
window shows and what is written; use `OnWindows` / `OnLinux` / `OnMac`, never
`OperatingSystem.Is*()` for a feature (the tests override `Platform`). macOS (Apple
Silicon) has its own Global Settings panel, written to `[global]` of `animejanai.conf`:
`backend` (`CoreML` = Core ML through ONNX Runtime, `CoreMLNative`, `Metal`),
`apple_device` (`AUTO`, `GPU`, `NPU`, `CPU`, `GPU+NPU`, `GPU+NPU+CPU`; Metal only takes the
ones that start with the GPU), `engine_reuse` (`off`, `frame`, `tiles`), `apple_auto_profile`,
`engine_boost` (Balanced model on held drawings, offered with `engine_reuse=frame`) and
`engine_rife_device` (`auto`, `gpu`, `npu`, `cpu`: the processor of RIFE). Model lists follow the
engine: `NAME.mlpatch` for Core ML native, `NAME.ajm` for Metal; `NAME.coreml.onnx` is a
variant, never listed. RIFE is offered when `rife/*.onnx` exist (no ensemble variants on
macOS). No TensorRT, DirectML, Vulkan, component packs or built-in slots on macOS. Opened from its application without the player, the Manager uses
`~/Library/Application Support/AnimeJaNai` and tells every running player to reload.

## Architecture

**Framework:** Avalonia UI (.NET 10.0) with ReactiveUI (MVVM pattern).

**Data model hierarchy:** `AnimeJaNaiConf` → `UpscaleSlot[]` → `UpscaleChain[]` → `UpscaleModel[]`. All nested in `MainWindowViewModel.cs`, which is the central file (~1400 lines) containing both the ViewModel and all model classes.

**Auto-save:** Model properties use `RaiseAndSetIfChanged` and subscribe via Rx to trigger `WriteAnimeJaNaiConf()` on any change. Modifying model properties automatically persists to the config file.

**View binding:** `ViewLocator.cs` maps ViewModels to Views by convention (replacing "ViewModel" suffix with "View"). Currently there is only one view: `MainWindow`.

**Config persistence:** Uses `Salaros.ConfigParser` to read/write `animejanai.conf` in INI format. Sections are `[global]` and `[slot_N]`, with chain/model properties using `chain_N_model_N_` key prefixes.

**Player settings:** Some `[global]` keys configure mpv rather than the inference engine — `default_slot` and `sub_render_mode` (the GPU subtitle toggle). The engine ignores them; `portable_config/scripts/animejanai_backend.lua` in the mpv-upscale-2x_animejanai package reads them at startup. The mpv options themselves live in a managed profile (`[subs-gpu]` in `mpv-animejanai.conf`), so the settings can change with the player build without a Manager release. This app never writes `mpv.conf`.

**Relative paths from executable:** The app resolves paths relative to its own location — `./animejanai.conf`, `./onnx/`, `./backups/`, and `../portable_config/mpv.conf` for mpv integration.

## Key Conventions

- Decimal formatting uses `CultureInfo.InvariantCulture` to handle non-English Windows locales
- Default profiles are read-only; custom profiles (slots 4+) are editable
- Backup system auto-creates up to 10 backups in `./backups/`, deduplicating by content
- Pre-packaged ONNX neural network models live in `./onnx/`

## Repository and release workflow

The active fork is `Ta2b/AnimeJaNaiManager-HDR`, branch `hdr-settings-0.8`.
The project path remains `AnimeJaNaiConfEditor`; the runtime assembly name is
`AnimeJaNaiManager`. Do not rename these project paths.

`.github/workflows/build-hdr.yml` is the only Manager release workflow. It runs
on pushes to the active HDR branch and manual dispatch; pull requests run checks
and builds without publication. The Windows/Linux/macOS matrix tests persistence and
the real Avalonia view, then publishes self-contained runtimes. A single publish
job uploads the three platforms together to `hdr-<commit SHA>` and the compatible
rolling tag `latest-hdr-settings-0.8`. Existing assets are not removed. The macOS
asset is `AnimeJaNaiManager-HDR-macos-arm64.zip`: `AnimeJaNai Manager.app`, ad-hoc
signed, started once on the runner before it is published. A failing test publishes the
end of its output as an annotation (`What failed`).

`player-source-ref.txt` pins the shared `AnimeJaNaiPlatform/PlatformServices.cs`
source from the companion player. The player uses `manager-source-ref.txt` to
pin this Manager build. Both pins are exact commit SHAs, not rolling branches.

Runtime packages contain the executable and native Skia/HarfBuzz libraries;
`animejanai.conf`, `onnx/` and PDBs are excluded. Releases are published
prereleases, so the assembler can download their assets without authentication.
The obsolete upstream `release.yml` has been removed. Publication order is
in the companion player's `docs/RELEASE.md`.

`setup-dotnet` uses `10.x`, matching the `net10.0` projects.

## Shared display settings

Global Settings contains one Display section, independent of the GPU.
`HdrSettings.Display` writes `windows_hdr`, `display_peak`, `display_gamut`
and `display_oled`; old per-vendor keys are migration fallbacks only and
remain in the file. Delayed GPU detection must not overwrite user edits.
New display defaults are Auto/Auto/OLED off, system HDR followed; HDR
conversion and Natural grading are enabled on confirmed HDR output.
The companion player ships Performance without RIFE as its startup choice.
Advanced settings stay accessible in collapsed expanders.
