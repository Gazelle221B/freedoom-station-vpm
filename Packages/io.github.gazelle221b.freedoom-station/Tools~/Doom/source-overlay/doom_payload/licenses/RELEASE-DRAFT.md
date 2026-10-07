# RELEASE-DRAFT.md

_Local release proposal. No publication, push, PR or upload. The locally implemented
board is documented in NOTES_DOOM_LICENSE_BOARD.md. Payload results are in NOTES_DOOM_REPRODUCIBLE.md; the
preferred source snapshot is pinned separately from release documents._

Component pins: Linux 0bd94b14, OpenSBI 7aa6c9aa, embeddeddoom
b52f80968a25a90b2ab0cf6c97703876b2d56e59, BusyBox 1.35.0, Buildroot
2022.02.1, glibc 2.34-109, GCC 11.2.0, Freedoom v0.13.0, rvc 0e2f610a.
Build source pin: 17b0a123 (bound host-baked data and pinned reproducible
payload build inputs).

Companion docs: doom_payload/licenses/FREEDOOM_COMPONENTS.md (component
inventory), doom_payload/licenses/GAME-STRINGS.md (string inventory and the
board content, including the verbatim ORIGINAL source-header/notice wording
for every listed component and the full-text viewer specification),
doom_payload/licenses/LICENSE-NOTES.md, doom_payload/licenses/
PAYLOAD-MODIFICATIONS.txt (modification log for the pinned engine).

## 1. What ships

The VRChat world uploads the RISC-V guest payload as texture data
(linux_payload.bin = OpenSBI firmware + Linux Image + cpio initramfs). The
initramfs contains emdoom (embeddeddoom E1M1ONLY build with the embedded
Freedoom-0.13.0 map subset), doominit/doomauto, and the notice files under
/usr/share/licenses/. The visual interpreter is the MIT rvc GPU compute
shader; the world UI is the VM/FB CRTs and the Interact buttons. Freedoom
0.13.0 (BSD-3-Clause) is the IWAD.

- OpenSBI is upstream RISC-V OpenSBI at pinned submodule 7aa6c9aa
  (https://github.com/riscv/opensbi per .gitmodules, maintained as
  riscv-software-src/opensbi). It is not a fork. The reproducible recipe
  builds the pinned original export; it does not apply the historical
  opensbi-don-t-zero-BSS.patch retained in the rvc source snapshot.
- The kernel is the PiMaker linux-rvc fork (mainline v5.17.11 base) at
  pinned submodule 0bd94b14.
- Neutral startup: the payload prints "Freedoom Phase 1 Startup v1.10"
  (template "Freedoom Phase 1 Startup v%i.%i", VERSION = 110, retail case)
  in place of the stock "The Ultimate DOOM Startup v%i.%i" title. It is
  implemented in doom_payload/patches/embeddeddoom-freedoom.patch (build
  source pin 17b0a123) and is confirmed in the rebuilt C/GPU UART output. Details and reachability: GAME-STRINGS.md section 3.

## 2. GPL-2.0 section 3 (source text, from the preserved DOOM-GPL-2.0.txt)

doom_payload/licenses/DOOM-GPL-2.0.txt is the complete GNU GPL version 2,
June 1991 text. Section 3 ("TERMS AND CONDITIONS FOR COPYING, DISTRIBUTION
AND MODIFICATION", preserved file lines ~138-176) requires, for any
distributed binary, one of:

> a) Accompany it with the complete corresponding machine-readable source
> code, which must be distributed under the terms of Sections 1 and 2 above
> on a medium customarily used for software interchange; or,
>
> b) Accompany it with a written offer, valid for at least three years, to
> give any third party, for a charge no more than your cost of physically
> performing source distribution, a complete machine-readable copy of the
> corresponding source code ...; or,
>
> c) Accompany it with the information you received as to the offer to
> distribute corresponding source code. (This alternative is allowed only
> for noncommercial distribution ...)

and:

> If distribution of executable or object code is made by offering access to
> copy from a designated place, then offering equivalent access to copy the
> source code from the same place counts as distribution of the source code
> ...

"Corresponding source" includes "all the source code for all modules it
contains, plus any associated interface definition files, plus the scripts
used to control compilation and installation of the executable."

The same section 3(a) text and the same-place paragraph were confirmed
against the id Software LICENSE.TXT
(https://raw.githubusercontent.com/id-Software/DOOM/master/LICENSE.TXT; the
official GNU URL timed out and is retained in section 6 as the canonical
reference). The same-place paragraph is the license text itself; quoting it
is not an assertion that the VRChat CDN delivery point satisfies it (see
section 3).
## 3. Proposal: corresponding source at the same designated place

The binary is distributed as world asset-bundle texture data downloaded by
each client from the VRChat CDN. The VRChat CDN and a self-hosted (or any
other) source URL are NOT automatically "the same place" for GPL-2.0 section
3 purposes; whether the CDN access point qualifies as "a designated place"
whose source must be offered in the same place is an interpretation that
must be argued explicitly and reviewed, not asserted.

**Proposal (subject to review), in order of conservativeness:**
1. At one versioned release location, offer both linux_payload.bin and the
   complete corresponding-source archive as adjacent downloads, with one
   manifest linking their SHA-256 values, component pins, notices and build
   instructions. Access to source must be equivalent to binary access,
   without an additional account/paywall. This is the concrete section 3(a)
   proposal for the separately downloadable GPL payload - it is NOT a claim
   that an external URL automatically accompanies the VRChat CDN download.
2. Link that same versioned release from the world description and a
   readable in-world board. Before releasing the world, establish how
   corresponding source accompanies its CDN-delivered payload. If equivalent
   same-place access cannot be established there, separately review a
   genuine section 3(b) written offer (>=3 years, any third party,
   source-distribution cost only). A bare URL is not that offer. No source
   or world is published in this task.
3. Keep the SDK/Editor and SDK-derived world bundle out of the GPL
   payload/source download. Do not declare the combined SDK/world licensed
   under GPL.

Board text states this plan only. RELEASE_URL remains a publication
decision; PAYLOAD_SHA256 is fixed by the production record below. It never
asserts that the CDN point is itself the designated place.

### Source inventory (pins and patch)

Local source archive assembled locally; archive URL and publication are
release tasks. Final sizes and hashes are added after the validated rebuild.

| Binary in payload | Corresponding source to bundle | Pin | License (as labeled on the board) |
|---|---|---|---|
| Linux Image (in linux_payload.bin) | linux/ (PiMaker linux-rvc fork) + generated .config (linux.config + CONFIG_BLK_DEV_INITRD/initramfs fragment from build_linux.py) | 0bd94b14 (submodule) | GPL-2.0-only (linux/COPYING: "version 2 only"; SPDX GPL-2.0 WITH Linux-syscall-note) |
| OpenSBI firmware | opensbi/ source incl. libfdt notice | 7aa6c9aa (submodule, upstream opensbi - not a fork) | BSD-2-Clause (opensbi/COPYING.BSD). Vendored libfdt is separately disjunctively dual-licensed GPL-2.0-or-later OR BSD-2-Clause (SPDX in libfdt.h/fdt.h); per OpenSBI's ThirdPartyNotices.md it is used under the BSD-2-Clause branch - BSD-2 is the branch selected for this distribution |
| emdoom engine | embeddeddoom b52f80968 + private patch set applied by build_emdoom.py from doom_payload/patches/: embeddeddoom-freedoom.patch (src/d_main.c: retail gamemode + RVC_IWAD printf, neutral startup title "Freedoom Phase 1 Startup v%i.%i", Freedoom banner, modification-date notice) and embeddeddoom-baker-bounds.patch (src/d_main.c, src/r_data.c: bounded vertex/COLORMAP/translation serialization and deterministic texture-header padding) + wadder/shrinkwad/stripchoice + baked-data generation | build source pin 17b0a123 (engine pin b52f80968 unchanged) | GPL-2.0-or-later (id Software DOOM relicensed; pinned tree preserves the 1997 Limited Use Software License Agreement and the id source headers verbatim; cnlohr overlay per LICENSE.md: "this license, BSD, MIT or any GPL or AFL licenses") |
| BusyBox | buildroot package source | 1.35.0 (Buildroot 2022.02.1) | GPL-2.0-only (busybox LICENSE: "Version 2 is the only version of this license which this version of BusyBox ... may be distributed under") |
| glibc + support libs | source (included for completeness) | 2.34-109 | LGPL-2.1+ |
| libgcc_s / libstdc++ / libatomic | GCC source + COPYING/COPYING.RUNTIME texts | 11.2.0 | GPL-3.0+ w/ GCC Runtime Library Exception |
| rvc emulator + shader (world side) | rvc repository code at 0e2f610a, incl. _Nix/rvc/ header.pp / console.shader and generated .h/.cginc | 0e2f610a | MIT (repo root LICENSE, "Copyright (c) 2021 PiMaker") |
| UdonSharp (world side, SDK-bundled) | SDK sources at Packages/com.vrchat.worlds/Integrations/UdonSharp (world project); excluded from the source archive | SDK 3.10.3 bundled integration | MIT (upstream vrchat-community/UdonSharp grant: "Copyright (c) 2020-2021 Merlin and UdonSharp Contributors / Copyright (c) 2022-Present VRChat Inc."); the SDK package ships no standalone UdonSharp license file - flagged in section 4 |
| Rootfs/userland build inputs | Buildroot 2022.02.1 legal-info output (package sources + licenses collected by Buildroot legal-info) + GCC license-text completion | Buildroot 2022.02.1 (in-tree) | per-package (GPL-2.0-only / LGPL-2.1+ / GPL-3.0+ w/ RLE / public domain) |
| Config / build scripts | Makefile, doom_payload/Makefile.inc, buildroot-config, linux.config, dts*.dts, build_emdoom.py, build_linux.py, build_reproducible.py, normalize_cpio.py, reproducible.py, prepare_rootfs.py, install_rootfs.sh, iwad.py, prepare_textures.py, capture/replay tooling | as committed (build source pin 17b0a123) | Per-file: existing rvc scripts retain MIT; new Doom platform adapters and Doom measurement/capture/replay tooling retain GPL-2.0-or-later per LICENSE-NOTES.md; engine-derived adaptations retain engine terms |
| Patches | opensbi-don-t-zero-BSS.patch, rust-target-rv32ima.patch (repo root); doom_payload/patches/embeddeddoom-freedoom.patch and embeddeddoom-baker-bounds.patch (committed at 17b0a123, applied by build_emdoom.py to the private engine tree) | - | - |
| Freedoom WAD (reproducibility, BSD) | freedoom-0.13.0 release archive + SHA-256 (see build/iwad-manifest.json: 7323bc...03d) | v0.13.0 | BSD-3-Clause - exact copyright line from release COPYING.txt: "Copyright © 2001-2024 Contributors to the Freedoom project.  All rights reserved." |
| perlpp / toimg (build-only) | perlpp 492babee; toimg with image=0.24.9 pinned build profile | as recorded in NOTES_DOOM_UNITY.md / prepare_textures.py | - |

## 4. VRChat SDK exclusion and separate installation

The VRChat Worlds SDK (3.10.3, Unity 2022.3.22f1) is proprietary. Its
package license.txt is the EULA pointer (https://hello.vrchat.com/legal/sdk)
and its terms prohibit transferring, disclosing, or placing SDK materials
under an open-source license. The SDK is therefore **excluded from the
source archive**; recipients install it separately via the official package
distribution (VRChat Creator Companion / VPM "VRChat Worlds" packages,
https://vcc.docs.vrchat.com), then open the world project with Unity
2022.3.22f1 per NOTES_DOOM_UNITY.md (prepare_unity_project.ps1,
RVC_SDK_SOURCE). The generated world project and SDK packages are never part
of the source bundle.

UdonSharp (MIT origin) is delivered inside the SDK's com.vrchat.worlds
integration and the world's converted Assets/UdonSharp copy; the SDK bundle
carries no standalone UdonSharp license text, so the board quotes the
upstream MIT grant (GAME-STRINGS.md section 10.2 C). SDK materials stay
under the SDK EULA; the MIT grant covers the upstream UdonSharp sources, not
the SDK package as a whole.

## 5. Notices in-world

- Freedoom COPYING/CREDITS/CREDITS-MUSIC are installed verbatim at
  /usr/share/licenses/freedoom/ in the initramfs, and emdoom prints their
  location at startup.
- The board/viewer shows the exact ORIGINAL source-header/notice wording for
  every listed component and a full-text viewer of every listed license
  (byte-identical, unmodified), per the board spec in GAME-STRINGS.md
  section 10: OpenSBI BSD-2 (COPYING.BSD), libfdt dual GPL-2.0-or-later OR
  BSD-2-Clause with the BSD branch elected per OpenSBI's original text, rvc
  MIT, UdonSharp MIT, Linux GPL-2.0-only, BusyBox GPL-2.0-only, Doom
  GPL-2.0-or-later, Freedoom BSD-3-Clause with the exact copyright line from
  the release COPYING.txt, plus the GPL-2.0 text (preserved verbatim as
  license_board/Documents/05.txt, the shipped 17,992-byte LF original).
- The board retains a future RELEASE_URL; PAYLOAD_SHA256 is fixed by the
  validated production record below.
- The paged TextMeshPro/Interact board is implemented locally; its byte-exact
  document manifest, static font provenance and tests are in
  NOTES_DOOM_LICENSE_BOARD.md. The source publication URL remains a placeholder.
## 6. Primary URLs

- GNU GPL v2 text (canonical): https://www.gnu.org/licenses/old-licenses/gpl-2.0.html
  (unreachable - timeout - when this draft was written; verify before publish)
- id Software DOOM LICENSE.TXT (verified; contains section 3(a) and the
  same-place paragraph): https://raw.githubusercontent.com/id-Software/DOOM/master/LICENSE.TXT
- id-Software/DOOM source: https://github.com/id-Software/DOOM
- embeddeddoom (pinned): https://github.com/cnlohr/embeddeddoom/tree/b52f80968a25a90b2ab0cf6c97703876b2d56e59
- Freedoom: https://freedoom.github.io | v0.13.0 release
  https://github.com/freedoom/freedoom/releases/tag/v0.13.0
- Linux fork v5.17.11: https://github.com/PiMaker/linux-rvc (Pin 0bd94b14)
- Upstream OpenSBI: https://github.com/riscv-software-src/opensbi
  (submodule URL riscv/opensbi, Pin 7aa6c9aa; upstream, not a fork)
- Buildroot: https://buildroot.org (in-tree 2022.02.1)
- rvc: https://github.com/PiMaker/rvc (code at 0e2f610a in archive)
- UdonSharp (MIT grant source): https://github.com/vrchat-community/UdonSharp
  (formerly MerlinVR/UdonSharp)
- VRChat SDK EULA: https://hello.vrchat.com/legal/sdk | VCC: https://vcc.docs.vrchat.com

## 7. Open items

1. VRChat CDN + an external source URL is not automatically same-place
   access; the adjacent-download proposal is concrete for the GPL payload
   release and needs the release review; review the section 3(b) fallback if
   equivalent same-place access cannot be established.
2. The neutral startup Freedoom Phase 1 Startup v1.10 is implemented in the
   committed patch set (build source pin 17b0a123) and is validated at runtime in C and GPU. Updated Freedoom captures and
   three-frame sample hashes are recorded in NOTES_DOOM_REPRODUCIBLE.md.
3. libfdt is disjunctively dual-licensed (GPL-2.0-or-later OR BSD-2-Clause);
   the BSD-2-Clause branch is selected exactly as OpenSBI's ThirdPartyNotices
   .md states. Do not print libfdt as plain "GPL-2.0+".
4. UdonSharp MIT text is not shipped inside the SDK package; the board
   quotes the upstream grant (GAME-STRINGS.md section 10.2 C).
5. emdoom is statically linked against LGPL glibc: retain the complete
   engine and libc sources plus the scripts that permit rebuilding/relinking
   with a modified libc, and review LGPL section 6 before distributing.
6. GPL section 2(a) modified-file notices: embeddeddoom-freedoom.patch adds a
   "Modified 2026-10-04" notice at the top of d_main.c. The pinned tree's
   1997 Limited Use Software License Agreement (embeddeddoom-LICENSE.md) and
   the pre-relicensing id headers remain preserved verbatim; no upstream
   license is replaced or re-licensed here. Contradictions between those
   preserved originals and the relicensing narrative are flagged rather than
   silently resolved.
7. Publication URL and payload SOURCE.txt installation remain future release
   tasks. The in-world board is implemented locally; publication is not authorized.


## Validated local production record (2026-10-04)

Build-source pin: 17b0a123. Two separate empty-work full builds and the
replacement production payload agree at SHA-256 `14f9859aafbeb8b1d8823b8ebe160d31c57e40c39ded87583e65a8815ca689d9`
(34,413,000 bytes). C/GPU p 61, v 57, MPRV 9 and Python unittest 89 passed;
E1M1 samples 1-3 match C and GPU at 8d6fc0f0 / 832bfd69 / 2c305ab8.
The .72 comparator was unchanged. Neutral startup was observed on UART;
SDK local build succeeded, and the managed textures were synchronized only
after all checks. The original license byte checks and complete measurements
are in NOTES_DOOM_REPRODUCIBLE.md and the local source-release manifest.
The source archive remains local. RELEASE_URL remains a future publication
choice; source distribution remains a proposal. The locally implemented board
is documented in NOTES_DOOM_LICENSE_BOARD.md. No publication, push, PR or upload.
