# AH-369 — Add step menu regression

The production Flyout opened with a valid UpscaleChain context and bound command,
but the first category click added nothing. Button dispatches Click before its
command; synchronous Hide detached the menu and cleared that binding. Queue Hide
on the UI dispatcher so the command completes first.

`receipts/before.json` records the first failed Interpolation click on b69968a.
`receipts/after.json` records the same route after the code-behind fix, then one
click per remaining category, new inactive selection, menu dismissal, save/reload,
one-HDR, 16-stage and future-schema guards. The boundary is populated directly;
successful clicks are not repeated. All files are private fixture data. No video
engine, hardware or FPS claim is involved.

Run once with a new absolute output directory, from this repository. Set the
shared-source paths to an isolated PLAYER checkout at the `player-source-ref.txt`
revision (its shared files also match .105):

```sh
dotnet run --project tests/AddStepUi/AddStepUi.csproj -c Release -r linux-x64 \
  -p:PlatformSource=/absolute/player/AnimeJaNaiPlatform/PlatformServices.cs \
  -p:ReferenceExporterSource=/absolute/player/AnimeJaNaiPlatform/ReferenceExporter.cs \
  -- /absolute/private-proof/after
```

Avalonia Headless uses the real app styles and real keyboard Space press/release
on attached buttons. It does not call the category commands directly for the
four addition assertions. This exercises the same Button.OnClick dispatch used
by pointer input; no native desktop or three-OS interaction claim is made.
