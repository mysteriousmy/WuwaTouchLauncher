# WutheringWaves-FPS-unlocker source snapshot

Upstream: https://github.com/30launchers/WutheringWaves-FPS-unlocker

Commit: `5aa7ecb0fb659b04deedbce43102240c0b4d17bd` (retrieved 2026-10-25).

Only the `ww_plugin_base` source project is vendored here, unchanged. Copyright (c) 2026 30launchers; MIT license in `LICENSE`. The launcher bundles the original DLL identified in `licenses/FPS-PLUGIN-PROVENANCE.md`, not a claim of a reproducible upstream binary.

Build the upstream project with Visual Studio 2022 C++ workload and Windows SDK, Release/x64. Its upstream output is `ww_plugin_base.dll`; copy a deliberately rebuilt version to the launcher as `assets/ww_ulk_plugin_base.dll`, then rebuild the launcher. Preserve this license when redistributing it.
