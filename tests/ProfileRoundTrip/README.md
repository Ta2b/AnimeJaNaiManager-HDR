# Profile serialization regression

Run `dotnet run --project tests/ProfileRoundTrip` from the repository root.
Requires .NET 10 and the same UI/parser packages as the Manager.

The test compiles the production view-model files and checks both values of
RIFE ensemble through profile export to disk and import from disk. It creates isolated fixture
configuration under its output directory; no installed configuration, UI window,
model loading or component download is used. A failed round trip exits nonzero.

The Linux port additionally checks HDR SDR/PQ persistence with comments,
unknown keys, DirectML refusal, Vulkan selection and converted-model filtering,
RIFE 4.6 round trips and the existing reload message over a Unix socket.
Pass `-p:PlatformSource=/absolute/path/to/mpv-AnimeJaNai-HDR/AnimeJaNaiPlatform/PlatformServices.cs`
when the shared source is outside the sibling checkout. Fixtures never execute
inference; GPU parity and cadence are tested in the player's `tests/inference`.

Common display tests cover automatic defaults, active Natural grading, legacy
bank migration, retained comments, common-key precedence, OLED off and delayed
GPU detection after a user edit. `../SettingsUi` adds real Avalonia headless
rendering, keyboard edits, folded controls, save/reload and a narrow-window check.
