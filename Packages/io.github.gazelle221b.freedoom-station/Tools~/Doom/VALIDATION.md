# Validation record

Runtime: Unity 2022.3.22f1, D3D11, NVIDIA GeForce RTX 4070 Ti SUPER,
VRChat Worlds SDK 3.10.3, TMP 3.0.6. Metrics only; capture images, UART data,
golden frames, binaries and textures remain local.

The managed integration before PR packaging was verified as follows:

| Check | Result |
| --- | --- |
| C/GPU M-extension probe | 8 instructions × 10,400 pairs matched |
| C/GPU Sv32 independent permission matrix | 69,120 cases matched |
| C/GPU ISA | p 61/61; v 57/57; MPRV guest cases 9/9 |
| Freedoom E1M1 comparator | 3 frames passed, unchanged scene/HUD threshold 0.72 |
| C/GPU sample hashes, frames 1–3 | `8d6fc0f0`, `832bfd69`, `2c305ab8` matched |
| Linux boot | 29,442 Unity frames, 330.721 s |
| Doom frames 2 / 3 | 936 / 942 Unity frames; 10.414 / 10.492 s |
| Framebuffer regression | 560 GPU cells matched; 1/2/63/64-byte bursts and overflow/sentinel cases |
| Nix/fb shader warnings/errors | 0 / 0 |
| Destroyed-proxy SDK callback exceptions | 0 (original SDK produced 2) |
| License board | 19/19 original texts and byte hashes matched; 124 pages; missing/fallback/blank/truncated count 0 |
| SDK local world build | Success; no upload |

PR packaging preserves the verified runtime scripts, station/board prefab,
materials and shader cores; the shared scene clears the personal blueprint ID
and temporary Editor camera target. `DoomWorldTextures` only binds regenerated
inputs. Whole-world serialization edits and unrelated recompiled program assets
are excluded. Source checkout/overlay and unit tests are checked again in the PR
workspace; frozen driver regression sources are exported with original commit
and SHA-256 so tests do not depend on private git history.

PR-workspace checks on 2026-10-05:

- Fresh pinned source checkout/overlay applied; WSL unittest **92/92** passed.
- Staged original notice hashes **19/19** matched; no forbidden artifacts staged.
- Unity CLI `DoomWorldErrorTests.Run`: **560 GPU cells** matched, callbacks/import
  errors/Nix-fb shader warnings and errors **0**. The cold initial shader import
  was restarted with the matching managed shader cache; tests executed on GPU.
- `DoomLicenseBoardBuild.ValidateRun`: **19 documents / 124 pages**, all originals
  matched; missing/fallback/blank/truncated **0**.
- `DoomWorldTextures.Run`: all 17 local textures imported/bound successfully.
- `DoomMulhProbe.Run`: **8 × 10,400** matched C/C# references, zero failures;
  intentional corruption detected in **10,400/10,400** cases. Optional shader
  generation now precedes import, avoiding a fresh-clone missing-include error.
- `DoomBuild.Run`: UdonSharp compilation and SDK local bundle build succeeded.
- `NewWorldPackageValidation.Validate`: **30 shaders / 243 asset references / 19
  Udon programs**, LightVolumes/LTCGI bridge available, errors **0**.

Large upstream sample payload/state PNGs are omitted. Their 13 unused sample-VM
texture bindings are cleared; the Doom VM has independent locally generated
textures. Neither the managed Editor's files nor its published world ID changed
in this PR-preparation step. Automatic import/recompile churn is not committed.

The remaining 15 Nix/rvc CPU shader warnings were not modified. The earlier
client UI.Text exception was in another world; two logged visits to this Doom
world had no Udon exception. These observations do not replace the human
VR/desktop Build & Test described in README.
