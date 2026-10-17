# Modular processing profiles

Profiles are freely composed from four categories: Interpolation, Upscale, Downscale and HDR. Add a step, select its technology and settings, then enable it when the required files and combination are available. Reorder, disable or remove steps directly in the ordered list. The order summary describes the actual processing order; downscale before RIFE reduces its input resolution.

Only RIFE exposes Quality/Fast. Quality retains full predictions at every requested instant; Fast uses the supported temporal shortcuts. Modes belong to the step. The launcher projects a compatible single-RIFE profile into a private runtime configuration, so an old hidden global mode cannot override the selected mode.

There is at most one HDR step per profile, including inactive steps and alternate resolution rules. mpv HDR needs no SDK; NVIDIA TrueHDR requires its platform support and optional components. Current inference engines consume SDR, so HDR conversion remains last.

Linux NVIDIA supports arbitrary delivered ordered combinations and independent Area/Spline36 downscale. Other readers accept their representable native layouts; unsupported combinations remain readable with an explicit reason. New methods are not substituted for imported methods. Historical sizing retains its original arithmetic and position until the user explicitly chooses downscale-only sizing.

FRUC is retired. Imported FRUC settings and SDK files are preserved, but an active FRUC step is rejected. The three old built-in profiles are removed; existing custom profiles and unknown configuration data remain.

The shared profile resolver supplies exact model requirements and availability reasons to the Manager and launcher. File presence is not a claim of GPU compatibility or measured speed. Consecutive inference groups do not guarantee that every backend can physically fuse them.
