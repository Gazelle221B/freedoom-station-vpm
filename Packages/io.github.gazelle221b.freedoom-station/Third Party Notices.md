# Component notices

Moving these files into a package does not relicense them. The original license
texts, credits, modifications and provenance are preserved byte-for-byte under
`Templates~/Doom/LicenseBoard/Documents`, `Tools~/Doom/source-overlay/doom_payload/licenses`
and the component directories. The self-authored Japanese viewer guide was
updated for publication; its previous bytes remain under LicenseBoard/History.

| Component | Existing notice | Source/provenance |
| --- | --- | --- |
| PiMaker RVC | MIT | `ThirdParty/PiMaker/rvc/LICENSE`, README and pinned source setup |
| ShaderEmu optimization ideas/adaptations | MIT, Copyright (c) 2026 Mykhailo Moroz | `Tools~/Doom/ShaderEmu-LICENSE.txt`; timer hoisting and aligned RAM stores adapted to this RVC core; [reference source](https://github.com/MichaelMoroz/ShaderEmu/tree/30e6b115ce54d4538498589b65257d9d30bc05a9) |
| PiMaker Dial | CC BY-NC-SA 2.0 | `ThirdParty/PiMaker/Dial/LICENSE-CC-BY-NC-SA-2.0.txt`, `PROVENANCE.md`, `Tools~/ThirdParty/Dial-source` |
| Doom/embeddeddoom engine and integration | GPL notices and component qualifications | `Tools~/Doom/source-overlay/doom_payload/licenses/LICENSE-NOTES.md`, `FREEDOOM_COMPONENTS.md`, upstream licenses and modification notices |
| Freedoom Phase 1 0.13.0 | BSD-3-Clause and original credits | Board documents 01–03 and component source records; WAD not included |
| Linux, Buildroot, BusyBox, OpenSBI, mini-rv32ima | Original component notices | Board documents and payload source/license records; obtained from pinned sources |
| Rvc License Sans 1.000, derived from Noto Sans JP/Noto Sans | SIL OFL 1.1 | Original OFL board documents, `Tools~/Doom/FONT.json` and font builder |

The author confirmed the existing GPL-2.0-or-later designation for new Doom
platform adapters and measurement/capture/replay tooling in LICENSE-NOTES.md.
The RELEASE-DRAFT.md inventory has been clarified to match; original rvc scripts
retain MIT and engine-derived changes retain their upstream terms. This does not
apply one license to the whole package. The manifest points here because a single
SPDX identifier would misrepresent this bundle. Retain PiMaker attribution and
Dial's CC BY-NC-SA 2.0 commercial-use restriction. See publication-changes.json
in the source repository for the approved changes relative to source-map.json.

SDK binaries/source, IWADs, generated payloads, baked C arrays, PNG payloads,
golden frames and captures are not part of this package. Pinned upstream sources, patches and rebuild instructions are in the tagged
source repository linked by the license board. No guest executable is distributed.
