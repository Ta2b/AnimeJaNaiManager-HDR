# AnimeJaNai Manager

Native AMD integration (14 October 2026): MIGraphX + HIP is exposed on Windows
and Linux when the native library is installed and the GPU is eligible. The
optional GPU engines component carries the Windows runtime for RX 9070 XT;
Linux needs a compatible system ROCm installation. Library presence enables the
choice; driver/runtime compatibility is resolved when the player opens a model.
Classic DirectML remains available on Windows. Native rank-5 temporal AMD models
are not supported yet. No new tests or benchmarks were executed for this work;
the Release build succeeded. See the player's `docs/CHANTIER-GPU-SPECIALISE.md`
for source refs, build evidence and the exact integration checkpoint.

Linux HDR port: [LINUX.md](LINUX.md) describes Vulkan/TensorRT selection,
SDR/PQ output, Unix IPC and profile preservation. Windows keeps its existing
TensorRT/DirectML backends.

Manager for [mpv-upscale-2x_animejanai](https://github.com/the-database/mpv-upscale-2x_animejanai): edit upscaling profiles and chains, and install or remove hardware-specific components (TensorRT runtime, per-GPU kernel packs, RIFE interpolation models) with recommendations based on the detected GPU.

Formerly AnimeJaNaiConfEditor; the executable is `AnimeJaNaiManager.exe` and ships at the install root next to `mpvnet.exe`.

## Global Settings

The page has one Display section for every GPU. New installs use automatic
peak brightness and gamut, with OLED off unless explicitly confirmed. The
player follows system HDR; SDR conversion and Natural grading are enabled
when HDR output is negotiated. Advanced controls are collapsed. Existing
settings and legacy display descriptions are preserved during migration.
Windows Vulkan inference is deferred; this UI change keeps the existing engines.

## Experimental automatic display refresh

In **Profiles**, enable **Automatic Display Refresh Rate (Experimental)** and
restart the player. Local files are scanned before playback to choose one
display refresh for the whole episode, including across seeks. Fixed-rate
videos use an exact or near multiple. Mixed-rate videos prefer a common
multiple of all measured cadences; otherwise, AJN minimizes estimated timing
error weighted by each cadence's duration. Unverified inputs use the highest
supported progressive refresh at the current resolution.

Playback pauses with a notice before a needed switch, waits for the display
to settle, and resumes while preserving manual pauses. Matching consecutive
episodes retain their rate. The original refresh is restored when playback
ends or the player closes normally. This is off by default. Scanning can add
startup time, and the TV may briefly go black during a switch. This setting
does not change `video-sync` or guarantee perfect VFR presentation.

This setting requires the companion AJN player with `display-rate-match`
support and the updated `animejanai_backend.lua`. It is saved as
`[global] display_rate_match=yes` in `animejanai/animejanai.conf` and included
in full config exports. It does not change the video frame rate or enable RIFE.

![297521638-76a8db5b-8c67-4b0c-911a-9b02598fb37a](https://github.com/the-database/AnimeJaNaiConfEditor/assets/25811902/e3b5d268-794c-40d3-b0ae-2d781d98d6ae)

## Builds and releases

The HDR workflow checks and builds Windows, Linux and macOS (Apple Silicon)
independently, then publishes the three runtime packages together. `hdr-<commit SHA>` identifies the exact source;
`latest-hdr-settings-0.8` remains available for existing installations. PRs run
checks without publication. `player-source-ref.txt` pins the shared player code.
The previous upstream release workflow is retired. See the companion player's
[release guide](https://github.com/Ta2b/mpv-AnimeJaNai-HDR/blob/hdr-3.8/docs/RELEASE.md).
