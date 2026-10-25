# Pinned build and relink inputs

This directory keeps the exact source archives and NuGet inputs used by the release. Large files are split into 40 MiB parts to remain ordinary Git blobs. No Git LFS or GitHub authentication is required.

Run `python3 tools/bootstrap-build-inputs.py` from the repository root. It verifies every part and the reconstructed file against `manifest.json`, then restores the inputs under `artifacts/`.

Source provenance is in `licenses/source-archives.json`; dependency notices and licenses are under `licenses/`. AtomUI is LGPL-3.0, Avalonia and StaticLink retain their upstream licenses. These vendored third-party materials are not relicensed under the launcher's MIT license.

See `docs/THIRD-PARTY.md` and `docs/NATIVEAOT.md` for source modification and relinking instructions. Other native dependency sources are downloaded with hashes from `build/LinuxNative/sources.json`; Microsoft SDK and external compiler tools are not distributed here.
