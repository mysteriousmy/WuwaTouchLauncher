# Wuwa Linux window backends

Local modifications to Avalonia 12.1.4, revision
`2d79ba55fae1b1e71c69f93a668080c9029e6a31` (MIT).
These packages are repository build inputs, not official Avalonia releases.
They retain assembly version 12.1.4.0 and pin their Avalonia dependencies exactly.

- X11: a factory creates an override-redirect window before mapping. Root
  structure events maintain its order above ordinary windows using Xlib only.
- Wayland: a layer-shell overlay uses the existing Avalonia rendering and input
  machinery. It supports output selection, logical positioning, partial input
  regions and configure acknowledgements tied to the surface generation.
  Floating layer sizes remain application-owned: configure size hints caused
  by approaching an output edge are acknowledged without shrinking the client.
  Explicit content/output resize requests still update the layout and buffers.
- Standard application windows continue to use Avalonia's ordinary backends.
- Without layer-shell, overlay windows use a native xdg-toplevel. The compositor handles
  dragging and its window menu; partial input regions retain the unlock button.
  There is no client-controlled position or always-on-top protocol in this path.
  `RequireLayerShell` remains an opt-in backend option, not the app default.
- Skia: keep the owned font stream and typeface alive until the native font
  read completes. This fixes an observed NativeAOT SIGSEGV during synthetic
  bold creation; the GDB stack ended in `SkMemoryStream::read` / `memcpy`.

Run `python3 tools/prepare-linux-backends.py` from the repository root.
The script checks the upstream archive hash, applies the small upstream patch,
builds the additions in this directory and creates local NuGet packages.
Source, build output and package cache live under ignored `artifacts/`.
The source generator is built from the same upstream revision with Roslyn 5.9.0
(the latest stable compiler API verified on 2026-10-17); it is not shipped in the application.

Changes to upstream files belong in the patch; additions belong in X11/ or
Wayland/. All three local packages use version 12.1.4.1 and retain Avalonia's MIT
license. `--force` verifies a clean rebuild; `--update-locks` is only for an
intentional dependency change. The publish script clears earlier compiler
outputs because deterministic package timestamps must not reuse stale AOT code.
Updating Avalonia requires reviewing the patch and repeating actual
NativeAOT publication plus X11/Wayland runtime checks. See
`docs/LINUX-STATIC.md` and `docs/NATIVEAOT.md` for measured results and limits.
