# Focused checks

Run the smallest relevant project with the paired player source through `PlatformSource` and `ReferenceExporterSource` when working in isolated checkouts. These are local fixtures, not GPU capability claims. Most projects take an output folder and refuse to run again into a folder that holds a conclusive `summary.json`.

- `NavigationUi`: profile identity, navigation, computed result, recommended profiles, engine change round trip, preservation and wide/narrow captures.
- `AddStepUi`: the three step categories through real keyboard clicks; a step is created active when its files are present.
- `ModularStages`: categories, supported choices per engine, imported/disabled stages, exact-size (legacy) sizing, explicit backend switch.
- `ChainCleanup`, `ConversionRetirement`: historical profiles; HDR steps (any position) become the one global HDR setting.
- `InterpolationPersistence`, `ProfileRoundTrip`: profile persistence, format 3 → 4, newer-format guard, unknown data.
- `SettingsUi` (also with `AJN_UI_PLATFORM=macos`): display, HDR conversion choice, engine options per platform.
- `TrueHdrSdkStore`, `TrueHdrOptionalSdkUi`: optional TrueHDR archive installation and the causes shown until NVIDIA TrueHDR can be chosen.
- `PreparationUi`: owned worker lifecycle and second-job cancellation.

The profile contract these checks follow is `docs/CONTRAT-PROFILS-V2.md` in the player repository. A prior successful campaign is not an instruction to replay every check.
