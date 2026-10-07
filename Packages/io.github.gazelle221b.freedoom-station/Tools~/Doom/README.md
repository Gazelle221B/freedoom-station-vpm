# Freedoom Station local payload setup

Unity **2022.3.22f1**, VRChat Worlds SDK **3.10.3**, TMP **3.0.6**, Built-in
pipeline, Windows D3D11. Restore the VPM packages through VCC before opening.
The VPM package declares Worlds SDK 3.10.3 and TMP/uGUI prerequisites.
LightVolumes/LTCGI are optional archived world examples, not terminal dependencies.
Install the editable station templates first from Tools > Doom > Install editable
station assets; see the repository README. Keep package code/program assets in
the embedded package and do not run the legacy loose-project overlay installers.

Open `Assets/Doom/DoomWorld.unity` for the standalone scene, or use **Tools > Doom
> Add station to current scene**. The prefab provides the CRT, eight VR/desktop
Interact controls and the paged license board. The original World scenes are not
edited by this PR. The Scene Descriptor includes PipelineManager; the shared
scene deliberately has no published world ID.

## Local payload setup

No IWAD, baked C arrays, payload binary, generated PNG, golden frame or capture
is distributed in this repository. All such files stay in ignored directories,
including when the IWAD is Freedoom. Do not force-add those outputs.

`source-overlay` contains the reviewed build/driver/test sources from the local
rvc integration. `prepare-source.py` checks out upstream rvc at
`da936a719b4254e91ba422361d7c1d1d0e775b8f`, applies the already verified C
SUM/MPRV/SRET permission patch, overlays those sources and obtains the upstream
pinned Linux, OpenSBI and riscv-tests submodules. No new CPU implementation is
introduced by this packaging step. The shader core in the package carries the existing
integer M-extension and Sv32 permission fixes used for the C/GPU validation.

Run on a Linux build host (WSL Ubuntu 24.04 was used), with the upstream rvc,
Buildroot and Linux prerequisites plus Python 3, Cargo, clang, dtc and cpio:

```sh
python3 Packages/io.github.gazelle221b.freedoom-station/Tools~/Doom/prepare-source.py --destination /path/to/rvc-doom
cd /path/to/rvc-doom
make doom-auto-payload DOOM_IWAD=freedoom DOOM_JOBS=16
cargo build --release --manifest-path elfy/Cargo.toml
make doom-probe
python3 doom_payload/capture_doom.py --auto --plain --frames 3
python3 doom_payload/replay_uart.py doom_payload/build/plain-auto-uart.bin \
  --frame 2 --cols 80 --rows 24 --out doom_payload/build/golden-freedoom-auto.txt
python3 doom_payload/prepare_textures.py
python3 -m unittest discover -s doom_payload -v
```

Freedoom Phase 1 **0.13.0** is selected and hash-checked by `iwad.py`:
`7323bcc168c5a45ff10749b339960e98314740a734c30d4b9f3337001f9e703d`.
The engine source is pinned to `b52f80968a25a90b2ab0cf6c97703876b2d56e59`;
private bounds/startup-text patches are included. `DOOM_IWAD=shareware` remains
available for private comparisons. World auto-start stays ASCII mode C, 79×24,
with no frame-end markers; verify/trace DTBs keep diagnostics separate.
The build fixes SOURCE_DATE_EPOCH, kernel identity, prefix maps and cpio metadata.
Accepted production payload SHA-256:
`14f9859aafbeb8b1d8823b8ebe160d31c57e40c39ded87583e65a8815ca689d9`.

With the Editor closed, copy locally generated textures into the world:

```powershell
python Packages/io.github.gazelle221b.freedoom-station/Tools~/Doom/import-textures.py --source '<source>/doom_payload/build/unity-textures' --project '<Unity project>'
$env:UNITY_EDITOR = '<Unity 2022.3.22f1>/Editor/Unity.exe'
$env:RVC_DOOM_SOURCE = '<source>'
./Packages/io.github.gazelle221b.freedoom-station/Tools~/Doom/run-doom.ps1 -Project '<Unity project>' -Mode Import
./Packages/io.github.gazelle221b.freedoom-station/Tools~/Doom/run-doom.ps1 -Project '<Unity project>' -Mode Verify -Ticks 4096 -Timeout 1800
./Packages/io.github.gazelle221b.freedoom-station/Tools~/Doom/run-doom.ps1 -Project '<Unity project>' -Mode Board
./Packages/io.github.gazelle221b.freedoom-station/Tools~/Doom/run-doom.ps1 -Project '<Unity project>' -Mode Errors
./Packages/io.github.gazelle221b.freedoom-station/Tools~/Doom/run-doom.ps1 -Project '<Unity project>' -Mode Build
```

`texture-imports.json` contains import settings/GUID templates only, no pixels.
`Import` binds all lanes without rebuilding the station or removing its board.
GPU commands use `-batchmode -force-d3d11`, never `-nographics`. Logs, text/PNG
captures and local bundle paths default to ignored `UserSettings/Doom`.
`-Mode Prepare` installs the editable templates with collision checks;
do not run the standalone `DoomBootstrap.Create/Setup` on the installed scene.

For the optional M probe, compile `doom_payload/m_probe_reference.c` on the host
and redirect stdout to `doom_payload/build/m-reference.bin`, then run
`-Mode Probe`. For ISA tests, build the pinned riscv-tests corpus with rvc's
rv32ima/ilp32 toolchain, run `prepare_isa_tests.py --data-dir <riscv-tests/isa>`
and then `-Mode Isa`. Both harnesses require generated local fixtures and fail if
they are missing. C/GPU share the same inventory (p: 61, v: 57).

## Display and SDK compatibility

The framebuffer's 64-byte UART burst uses a 64-element buffer; initialized
single-return paths remove the Nix/fb D3D11 warnings. `fb.h` is generated from
`fb.h.pp`; set `RVC_PERL` and `RVC_PERLPP` for regeneration. Paths are taken from
the environment. CPU-core warnings remain outside this display fix.

SDK 3.10.3 can invoke serialization callbacks on a destroyed UdonSharp proxy.
The local, hash-guarded adapter adds the existing SDK safe-null check to both
callbacks. SDK source is not redistributed by this PR. With the Editor closed:

```powershell
python Packages/io.github.gazelle221b.freedoom-station/Tools~/Doom/source-overlay/doom_payload/unity/apply_local_sdk_fixes.py `
  --project . --backup UserSettings/Doom/sdk-backup `
  --manifest UserSettings/Doom/sdk-fix.json
```

It backs up the original, accepts only the reviewed SDK hash, preserves line
endings, is idempotent and rejects unknown edits. Review it after an SDK upgrade.
`DoomWorldErrorTests` tests invalid-object callbacks, valid proxies, prefab
import/save and 560 actual GPU framebuffer cells.

## License board and client check

The board contains 19 documents over 124 pages; upstream originals preserve BOM
and CRLF bytes with `-text` attributes. Tests reconnect pages and verify each
document's text and SHA-256, atlas coverage and page layout. The static SDF font
is **Rvc License Sans 1.000**, derived from Noto Sans JP/Noto Sans under SIL OFL
1.1. Both original OFL texts are on the board; `FONT.json` records source URLs,
versions, pins and hashes. There is no runtime fallback or Update loop.

`Assets/Doom/LicenseBoard/documents.json` centralizes the source URL
`https://github.com/Gazelle221B/freedoom-station-vpm/tree/v0.1.0` and the historical production payload hash.
Regenerate and verify the board when local payload/source inputs change.
No public source-distribution endpoint or world upload is created by these tools.
Release/license proposals remain in `source-overlay/doom_payload/licenses`.

For human verification, open DoomWorld, use **VRChat SDK > Builder > Build & Test**,
log in if necessary and check CRT auto-start, all eight input buttons and document/
page buttons in VR and desktop. CLI `Build` produces a local bundle only.

See `VALIDATION.md` for measured results and verification scope.
