# Engines and optional components

Open **Engines & components** from the main navigation or a step's setup action. Backend components, optional SDK imports and preparation-engine settings live here; profiles keep their processing settings.

mpv HDR is included without an SDK. On Linux NVIDIA, TrueHDR can import the original NVIDIA Maxine Core and TrueHDR feature archives using the download links and archive pickers. Files are verified and installed into managed versions; source archives, custom paths and user-modified older versions are preserved. Windows uses the delivered RTX Video integration. SDK/driver availability is reported separately from physical HDR output.

FRUC installation and activation have been removed. Old SDK files are not deleted.

GMFSS Fortuna runs as a separate preparation process. Its advanced setup selects an existing Python CUDA/CuPy environment and the pinned upstream source and weights; nothing is silently downloaded. Prepare then chooses the source, destination, factor and output representation. This provider currently accepts CFR SDR8 inputs; it refuses unsupported precision/color/timing rather than silently reducing them. Original inputs and existing destinations are never overwritten.

See the paired player's `tools/interpolation/gmfss-fortuna.json` for source identity and its qualification report for tested hardware and remaining limits.
