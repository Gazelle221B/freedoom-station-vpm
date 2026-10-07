# Game Text Inventory and Board License Text (DOOM / Freedoom payload)

_Read-only game-string inventory plus board source references. No engine or source file
is modified here. Branch: feat/doom-reproducible._

- Neutral startup for the reproducible payload: Freedoom Phase 1 Startup
  v1.10, implemented in the committed build-script patches (build source
  pin 17b0a123); the rebuilt C/GPU payload has been validated (three matching sample hashes).
- Inspected source (WSL): /var/tmp/rvc-doom-20261001
  - doom_payload/cache/embeddeddoom/src - pinned stock embeddeddoom
    b52f80968 (OLD source baseline)
  - doom_payload/build/freedoom/target/embeddeddoom/src - private Freedoom
    engine tree (WSL-only build output)
- Private adaptation patches (doom_payload/patches/, committed at 17b0a123),
  applied by build_emdoom.py to the private engine tree:
  embeddeddoom-freedoom.patch (src/d_main.c: gamemode = retail + RVC_IWAD
  printf, neutral startup notices, modification-date notice) and
  embeddeddoom-baker-bounds.patch (src/d_main.c and src/r_data.c: bounded
  vertex/COLORMAP/translation serialization, deterministic texture-header
  padding). These patch files replace the earlier inline string
  substitutions in build_emdoom.py.
- Modification log: doom_payload/licenses/PAYLOAD-MODIFICATIONS.txt.
- Companion: doom_payload/licenses/FREEDOOM_COMPONENTS.md,
  RELEASE-DRAFT.md, LICENSE-NOTES.md.

## 1. Build context used for reachability (E1M1ONLY / NORMALUNIX)

From doom_payload/build_emdoom.py (common, target flags) and
build/freedoom/target/embeddeddoom/src/.payload-build-flags:

    -march=rv32ima -mabi=ilp32 -fno-pie -flto -Os -g
    -DE1M1ONLY=1 -DNORMALUNIX -DLINUX -DMAXPLAYERS=1 -DDISABLE_NETWORK
    -DSET_MEMORY_DEBUG=0 -DRANGECHECK -fdata-sections -ffunction-sections
    -DFIXED_HEAP=393216   CC=riscv32-buildroot-linux-gnu-gcc
    CS=video_console.c (replaces i_video.c; i_video_console.c is NOT linked)

- FRENCH is not defined (dstrings.h #ifdef FRENCH ... #else d_englsh.h), so
  the English string table compiles and d_french.h is inert.
- IWAD: DOOM_IWAD=freedoom (world default). The private engine adaptation
  comes from the committed patch set (header above): retail rules + RVC_IWAD
  printf + neutral startup notices (embeddeddoom-freedoom.patch) and
  bounded baked-data serialization (embeddeddoom-baker-bounds.patch).
- WAD strip list applied by the payload build (freedoom path):
  -DEMO* -TITLEPIC -CREDIT -HELP* -M_* -WI* -D_* +D_E1M1 -GENMIDI.
  (cache/embeddeddoom/src/support/stripchoice-E1M1ONLY.txt is the
  shareware-only strip list; iwad.py:e1m1_only() keeps only the E1M1 map
  block.)
- Start flow: E1M1ONLY forces startepisode=1, startmap=1, autostart=true
  (d_main.c ~line 747); payload launchers run ./emdoom -warp 1 1
  (doom_payload/doominit interactive, doom_payload/doomauto auto ASCII
  mode). Console (stdout/UART) output is reachable on the guest
  terminal/serial channel; the in-world CRT shows the UART terminal
  framebuffer (ASCII output).

Legend: compiled = part of the selected source/compiler input (LTO can
remove unreachable constants; this is not a claim about every final ELF
string); reachable = actually displayed/printed in the shipped Freedoom
payload; line numbers are from the pinned trees (C = stock
cache/embeddeddoom/src, the OLD source baseline; F = private
build/freedoom/target/embeddeddoom/src; F is C+1 line in d_main.c in the
patched region); doomdef.h:34 defines enum { VERSION = 110 } (both trees).

## 2. Startup / console strings - OLD source (pinned trees, before the private patches)

| File:line | Macro / case | Exact text (OLD source) | Compiled | Reachable |
|---|---|---|---|---|
| F d_main.c:544 | IdentifyVersion (patch adds) | RVC_IWAD: Freedoom Phase 1 (retail)\n | yes | Yes - printed at startup on the guest console |
| C:577 / F:578 | retail case | The Ultimate DOOM Startup v%i.%i (VERSION=110 -> "The Ultimate DOOM Startup v1.10") | yes | Yes in OLD source; replaced in the payload by "Freedoom Phase 1 Startup v%i.%i" (section 3); printed via printf("%s\n",title) (d_main.c:627) |
| C:584 / F:585 | shareware case | DOOM Shareware Startup v%i.%i | yes | No - retail path taken |
| C:591 / F:592 | registered case | DOOM Registered Startup v%i.%i | yes | No |
| C:598 / F:599 | commercial case | DOOM 2: Hell on Earth v%i.%i | yes | No |
| C:606/F:607, C:613/F:614 | pack_plut / pack_tnt (inside /*FIXME*/) | DOOM 2: Plutonia Experiment ... / DOOM 2: TNT - Evilution ... | no (in comment) | No |
| C:621 / F:622 | default case | Public DOOM - v%i.%i | yes | No |
| C:810 / F:811 | modifiedgame banner | ATTENTION:  This version of DOOM has been modified. ... call 1-800-IDGAMES ... press enter to continue | yes | No - requires modifiedgame (no -file/pwad in this build) |
| C:833-836 | banner switch, registered/retail/commercial (stock) | Commercial product - do not distribute! / Please report software piracy to the SPA: 1-800-388-PIR8 | stock: yes; freedoom engine: replaced | No in shipped build - replaced by the Freedoom banner |
| F:837-838 | banner switch, retail (patched) | Freedoom Phase 1 - freely redistributable data / See /usr/share/licenses/freedoom for license and credits | yes | Yes - printed at startup (retail branch) |
| C:850 / F:851 | R_Init | R_Init: Init DOOM refresh daemon -  | yes | Yes - printed at startup |

Note: the table records the OLD source baseline (pinned trees before the
private patches). Existing golden/ANSI captures (build/golden-*, ansi-*,
ascii-*) were produced from this OLD source build and are superseded by the
validated replacement payload (section 3).
## 3. Validated neutral startup

The reproducible payload prints the neutral banner:

    Freedoom Phase 1 Startup v1.10

- Implemented in doom_payload/patches/embeddeddoom-freedoom.patch (build
  source pin 17b0a123), applied at build time to the retail-case startup
  title in src/d_main.c. Template "Freedoom Phase 1 Startup v%i.%i";
  VERSION = 110 -> "1.10" (doomdef.h:34, enum { VERSION = 110 }).
- Replaces the OLD retail title "The Ultimate DOOM Startup v%i.%i"
  (C:577 / F:578). No other startup title changes are made by the patch.
- Runtime confirmed in the C and GPU UART captures (see production record).
  Reachability: retail is forced, the title is printed via
  printf("%s\n", title) at startup on the guest console/serial channel; the
  other startup tokens (R_Init: Init DOOM refresh daemon - , the RVC_IWAD
  printf, the Freedoom banner) remain as the patch leaves them.

## 4. Menu strings (engine baseline; unchanged by the patches)

| File:line | Macro | Exact text | Reachability |
|---|---|---|---|
| m_menu.c:934 (both trees) | M_Episode guard | M_Episode: 4th episode requires UltimateDOOM\n (fprintf(stderr,...)) | Compiled; not reached: requires gamemode==registered and menu episode selection; shipped build is retail, autostart bypasses the menu, and M_* lumps are stripped so the menu cannot render even if opened with Escape. Menu item labels are WAD graphics, not source strings. |
| d_englsh.h:57 | SWSTRING | this is the shareware version of doom.\n\nyou need to order the entire trilogy.\n\npress a key. | Compiled (English table); used at m_menu.c:924 only when gamemode==shareware && episode choice != 0; not reached in shipped build. French variant (d_french.h:50) not compiled. |
| d_englsh.h:34 | D_CDROM | CD-ROM Version: default.cfg from c:\doomdata\n | Defined, but no C references - not emitted to output. |
| m_menu.c:858 | M_DOOM | M_DOOM (menu background lump name) | Data reference only; lump stripped; not user-visible text. |

## 5. HUD strings (engine baseline; unchanged by the patches)

No string constant in st_stuff.c, st_lib.c, hu_stuff.c, hu_lib.c, or the HUD
section of d_englsh.h (STSTR_*, HUSTR_*) contains "DOOM" or "id Software".
The status-bar art (STBAR etc.) is WAD data: Freedoom supplies its own
artwork; the shareware WAD's id-branded status-bar art exists only in the
private shareware validation build.
## 6. Exit / finale text (engine baseline; unchanged by the patches)

Finale text macros are compiled into the binary via f_finale.c (e1text =
E1TEXT etc.), but the display path (GS_FINALE) is entered only after the
last map of an episode, and the E1M1ONLY payload contains only E1M1 (no
E1M8); the D_* lumps are stripped except D_E1M1. Not reachable in the
shipped payload.

| File:line | Macro | Exact text (branded portion) | Reachability |
|---|---|---|---|
| d_englsh.h:372 | E1TEXT | To continue the DOOM experience, play\n | Compiled; not reachable (see above) |
| d_englsh.h:394 | E2TEXT | DOOM! -- Inferno. | Compiled; not reachable |
| d_englsh.h:397, :417 | E3TEXT, E4TEXT | (no "DOOM" literal) | Compiled; not reachable; E4TEXT is Ultimate-only |
| intermission | (graphics) | KILLS/ITEMS/SECRETS/PAR/TIME are WAD patches, not source strings | WI* lumps stripped; intermission after an E1M1 exit cannot render |

## 7. Other compiled strings containing "DOOM" (not user-visible)

Compiled into the binary but never displayed in this build: d_net.c:496
("Different DOOM versions cannot play a net game!"), d_net.c:570 ("Doomcom
buffer invalid!") - net play disabled (DISABLE_NETWORK, MAXPLAYERS=1);
m_misc.c:515 ("DOOM00.pcx" screenshot file-name template); sounds.c:78/86
("doom"/"doom2" sound resource base names); i_sound.c:746/748 ("DOOMWADDIR"
env var); g_game.c:1279 ("c:\doomdata\" save path); dstrings.h:41
("doomsav" savegame name). i_video.c:202 ("EmbeddedDoom" window title) and
i_video_console.c:178/320 ("Doom" window title) are NOT linked into the
payload (CS=video_console.c; i_video_console.c absent from the Makefile CS
list). Generated support/rawwad.c contains the baked WAD lump-name strings
"ENDOOM" and "M_DOOM" (data references; ENDOOM is not rendered - the engine
has no DOS-exit screen).

## 8. Preserved source copyright headers (verbatim)

Doom-derived source files carry the preserved header below (unchanged, from
the pinned trees). These are comments, not displayed text; they are the
pre-relicensing id Software notices and must remain in any distributed
source:

    // Emacs style mode select   -*- C++ -*-
    //-----------------------------------------------------------------------------
    //
    // $Id:$
    //
    // Copyright (C) 1993-1996 by id Software, Inc.
    //
    // This source is available for distribution and/or modification
    // only under the terms of the DOOM Source Code License as
    // published by id Software. All rights reserved.
    //
    // The source is distributed in the hope that it will be useful,
    // but WITHOUT ANY WARRANTY; without even the implied warranty of
    // FITNESS FOR A PARTICULAR PURPOSE. See the DOOM Source Code License
    // for more details.
    //
    // $Log:$
    //
    // DESCRIPTION:
    //	DOOM main program (D_DoomMain) and game loop (D_DoomLoop),
    //	plus functions to determine game mode (shareware, registered),
    //	parse command line parameters, configure game parameters (turbo),
    //	and call the startup functions.

The pinned embeddeddoom tree also preserves the 1997 Limited Use Software
License Agreement in its LICENSE.md (copied verbatim to
doom_payload/licenses/embeddeddoom-LICENSE.md) together with the cnlohr
overlay statement:

    BELOW is the ORIGINAL license DOOM was released under.

    You may use that or whatever they do for their own code.

    All changes I (cnlohr) made can be licensed under this license, BSD,
    MIT or any GPL or AFL licenses.

Board label used: GPL-2.0-or-later (id Software DOOM relicensed; the GPL-2.0
text is preserved verbatim as doom_payload/licenses/DOOM-GPL-2.0.txt, and
the id-Software/DOOM LICENSE.TXT was verified). The pinned tree's own
headers still reference the DOOM Source Code License (the 1997 agreement);
that agreement and the relicensing narrative both remain in the bundle, and
nothing is relicensed here. For GPL section 2(a), embeddeddoom-freedoom.patch
adds the modification-date notice "// Modified 2026-10-04 for the rvc
Freedoom payload: retail rules and neutral startup notices." at the top of
d_main.c; the original notices above are retained unchanged.

## 9. Summary

OLD source baseline (pinned trees): the startup console strings containing
"DOOM" are "The Ultimate DOOM Startup v1.10", "R_Init: Init DOOM refresh
daemon - " and "RVC_IWAD: Freedoom Phase 1 (retail)". Reproducible payload
(build source pin 17b0a123): the retail title is replaced by the neutral
"Freedoom Phase 1 Startup v1.10" via embeddeddoom-freedoom.patch; runtime
validation of the rebuilt payload passed in C and GPU. No literal "id Software" was
found in the HUD/menu/exit text; original source copyright headers remain
intact. Existing golden/ANSI captures were produced from the OLD source
build and are superseded. id-branded artwork (TITLEPIC, CREDIT, M_DOOM,
STBAR, ENDOOM) exists only in the private shareware validation data, not in
the Freedoom world payload.
## 10. Board content and full-text license viewer (proposal)

### 10.1 Requirements

1. The board shows the exact ORIGINAL source-header/notice wording for every
   listed component (verbatim, unmodified; sections 10.2 A-G reproduce the
   primary-source text).
2. A full-text viewer shows every listed license document in full,
   byte-identical to the shipped notice files (no summary, no truncation, no
   re-licensed text). Nothing here relicenses any source; contradictions in
   the preserved originals are flagged, not resolved.
3. Preservation requirements are printed on the board (section 10.3).
4. The SDK exclusion statement is printed (section 10.4).
5. RELEASE_URL remains a publication decision; PAYLOAD_SHA256 is fixed by
   the validated production record below.
6. No assertion that the VRChat CDN and an external source URL are "the same
   place" for GPL-2.0 section 3; the board states the plan (one release page
   with adjacent binary + source) and lists the review and the section 3(b)
   fallback as open items.

### 10.2 Verbatim original notices (primary-source wording)

A. OpenSBI - BSD-2-Clause (opensbi/COPYING.BSD, full text):

    The 2-Clause BSD License
    SPDX short identifier: BSD-2-Clause

    Copyright (c) 2019 Western Digital Corporation or its affiliates and other
    contributors.

    Redistribution and use in source and binary forms, with or without
    modification, are permitted provided that the following conditions are met:

    1. Redistributions of source code must retain the above copyright notice, this
       list of conditions and the following disclaimer.
    2. Redistributions in binary form must reproduce the above copyright notice,
       this list of conditions and the following disclaimer in the documentation
       and/or other materials provided with the distribution.

    THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS" AND
    ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED
    WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
    DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT HOLDER OR CONTRIBUTORS BE LIABLE FOR
    ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES
    (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES;
    LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND
    ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT
    (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS
    SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.

Included libfdt - disjunctively dual licensed; BSD branch elected exactly per
OpenSBI's own original text:

    /* SPDX-License-Identifier: (GPL-2.0-or-later OR BSD-2-Clause) */
        (opensbi/lib/utils/libfdt/libfdt.h and fdt.h, first line)

    libfdt
    ------
    Copyright (C) 2016 Free Electrons
    Copyright (C) 2016 NextThing Co.

    The libfdt source code is disjunctively dual licensed (GPL-2.0+ or
    BSD-2-Clause). Some of this project code is used in OpenSBI under the terms of
    the BSD 2-Clause license. The full text of this license can be found in the
    file COPYING.BSD.
        (opensbi/ThirdPartyNotices.md)

Per that original text the libfdt branch selected for this distribution is
BSD-2-Clause. Do not print libfdt as plain "GPL-2.0+".

B. rvc - MIT (repo root <rvc-source>/LICENSE; the world project
carries the same text at Assets/ThirdParty/PiMaker/rvc/LICENSE):

    MIT License

    Copyright (c) 2021 PiMaker

    Permission is hereby granted, free of charge, to any person obtaining a copy
    of this software and associated documentation files (the "Software"), to deal
    in the Software without restriction, including without limitation the rights
    to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
    copies of the Software, and to permit persons to whom the Software is
    furnished to do so, subject to the following conditions:

    The above copyright notice and this permission notice shall be included in all
    copies or substantial portions of the Software.

    THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
    IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
    FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
    AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
    LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
    OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
    SOFTWARE.

C. UdonSharp - MIT (upstream grant, from
https://github.com/vrchat-community/UdonSharp LICENSE; the SDK bundle itself
ships no standalone UdonSharp license file - flagged in section 10.4 and
RELEASE-DRAFT.md section 4):

    MIT License

    Copyright (c) 2020-2021 Merlin and UdonSharp Contributors
    Copyright (c) 2022-Present VRChat Inc.

    [standard MIT body as in B: permission grant, inclusion of the copyright
    and permission notice in all copies or substantial portions, and the
    disclaimer in the exact upstream wording]
D. Linux - GPL-2.0-only (linux/COPYING, verbatim opening):

    The Linux Kernel is provided under:

        SPDX-License-Identifier: GPL-2.0 WITH Linux-syscall-note

    Being under the terms of the GNU General Public License version 2 only,
    according with:

        LICENSES/preferred/GPL-2.0

    With an explicit syscall exception, as stated at:

        LICENSES/exceptions/Linux-syscall-note

    In addition, other licenses may also apply. Please see:

        Documentation/process/license-rules.rst

    for more details.

    All contributions to the Linux Kernel are subject to this COPYING file.

(Linux COPYING is a 20-line notice. The viewer separately shows it, the
complete LICENSES/preferred/GPL-2.0 and LICENSES/exceptions/Linux-syscall-note.)

E. BusyBox - GPL-2.0-only
(buildroot-2022.02.1/output/build/busybox-1.35.0/LICENSE, verbatim opening):

    --- A note on GPL versions

    BusyBox is distributed under version 2 of the General Public License (included
    in its entirety, below).  Version 2 is the only version of this license which
    this version of BusyBox (or modified versions derived from this one) may be
    distributed under.

(The LICENSE file then contains the complete GPL-2.0 text; the viewer shows
the file in full.)

F. Doom / embeddeddoom - GPL-2.0-or-later label; verbatim preserved header
and overlay in section 8. GPL-2.0 full text preserved as
license_board/Documents/05.txt (shipped LF original, 17,992 bytes, shown
in full by the viewer; the older repository CRLF copy is 18,332 bytes). The 1997 Limited Use Software License Agreement and the
pre-relicensing id headers remain in the pinned tree and are preserved; the
flag in section 8 applies.

G. Freedoom - BSD-3-Clause
(doom_payload/cache/freedoom-0.13.0/COPYING.txt, full text; the shipped file
starts with a UTF-8 BOM and uses the copyright sign and curly quotes - the
viewer must reproduce the shipped file byte-for-byte, not this
transcription):

    Copyright © 2001-2024
    Contributors to the Freedoom project.  All rights reserved.

    Redistribution and use in source and binary forms, with or without
    modification, are permitted provided that the following conditions are
    met:

      * Redistributions of source code must retain the above copyright
        notice, this list of conditions and the following disclaimer.
      * Redistributions in binary form must reproduce the above copyright
        notice, this list of conditions and the following disclaimer in the
        documentation and/or other materials provided with the distribution.
      * Neither the name of the Freedoom project nor the names of its
        contributors may be used to endorse or promote products derived from
        this software without specific prior written permission.

    THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS
    IS" AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED
    TO, THE IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A
    PARTICULAR PURPOSE ARE DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT OWNER
    OR CONTRIBUTORS BE LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL,
    EXEMPLARY, OR CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT LIMITED TO,
    PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES; LOSS OF USE, DATA, OR
    PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON ANY THEORY OF
    LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT (INCLUDING
    NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS
    SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.

    For a list of contributors to the Freedoom project, see the file
    CREDITS.

### 10.3 Preservation requirements (printed on the board)

- Do not remove, alter, or relicense any original copyright notice, license
  grant, or disclaimer. Do not relicense the sources yourself.
- GPL-2.0 section 1: preserve copyright notices in modified copies; section
  2(a): mark modified files with a prominent notice stating the changes and
  the date (the committed patch adds such a notice to d_main.c); distribute
  the complete corresponding source (RELEASE-DRAFT.md section 2).
- BSD-2-Clause / BSD-3-Clause condition 1: retain the copyright notice, the
  condition list and the disclaimer in source redistributions; condition 2:
  reproduce them in binary redistributions; BSD-3 condition 3: no name
  endorsement without prior written permission.
- MIT: include the above copyright notice and permission notice in all
  copies or substantial portions.
- Keep every notice file byte-identical in the initramfs
  (/usr/share/licenses/...), in doom_payload/cache/freedoom-0.13.0/, in the
  production-notices/ folder of the source package, and in the in-world
  viewer (no truncation; the font must cover the copyright sign, curly
  quotes, and the contributor names).
### 10.4 SDK exclusion and viewer content set (full text, verbatim)

VRChat Worlds SDK (3.10.3, Unity 2022.3.22f1) is proprietary: its package
license.txt is the EULA pointer (https://hello.vrchat.com/legal/sdk) and its
materials may not be transferred, disclosed, or placed under an open-source
license. The SDK is excluded from the source archive; recipients install it
separately via VCC/VPM (https://vcc.docs.vrchat.com). UdonSharp inside the
SDK (Packages/com.vrchat.worlds/Integrations/UdonSharp and the world's
converted Assets/UdonSharp copy) is MIT upstream, but the SDK bundle ships no
standalone UdonSharp license file; the board quotes the upstream grant
(section 10.2 C) and flags this gap.

The viewer displays, in full and unmodified:
1. Freedoom COPYING.txt (30 lines, BSD-3-Clause) -
   /usr/share/licenses/freedoom/COPYING.txt
2. Freedoom CREDITS.txt (1,030 lines)
3. Freedoom CREDITS-MUSIC.txt (205 lines)
4. GNU GPL v2 text (shipped original, license_board/Documents/05.txt, 17,992 bytes)
5. GNU GPL v2 notice line plus primary URLs (GNU GPL-2.0 page,
   id-Software/DOOM, embeddeddoom pin, Freedoom release)
6. OpenSBI COPYING.BSD (BSD-2-Clause) plus the libfdt dual notice
7. rvc LICENSE (MIT)
8. UdonSharp MIT grant text (upstream; provenance note)
9. Linux COPYING (GPL-2.0-only with Linux-syscall-note)
10. BusyBox LICENSE (GPL-2.0-only)
11. Source-availability statement (plan wording, section 10.5) with
    future RELEASE_URL and the validated PAYLOAD_SHA256 below
12. /usr/share/licenses/emdoom/IWAD.json (IWAD version/hash evidence)

Acceptance criteria: all items fully readable without truncation; text
byte-identical to the shipped notice files (BOM and punctuation included);
board shows the Japanese attribution plus the plan-form source statement.

Rendering options (the world-side viewer is implemented; see NOTES_DOOM_LICENSE_BOARD.md):
- (1) In-guest terminal viewer: the interactive doominit shell already runs
  on the ASCII terminal; cat /usr/share/licenses/freedoom/CREDITS.txt
  displays the full text today, no new code. Sufficient as a stop-gap; not
  visitor-friendly.
- (2) Recommended: world-side paged TextMeshPro board/panel, separate from
  the Nix ASCII font/CRT. Use a font asset containing the Unicode characters
  in the original notices (copyright sign, curly quotes, contributor names)
  and the Japanese guide. Preserve the original UTF-8 files unchanged and
  paginate their text without deletion. Add large Interact
  next/previous/document buttons, usable in VR and desktop. The existing
  8-bit terminal font cannot display the full Unicode notices faithfully.

The user authorized and the local project implements option (2): a static
SDF font and four Interact buttons. Original document bytes and the single
source URL placeholder are pinned in license_board/documents.json.
The remaining draft prose in 10.5 is background, not the displayed notice
source; the actual Japanese guide is Documents/00.txt.

### 10.5 Board text (Japanese draft; English originals always govern)

    このワールドでは、オープンソースの Doom 互換ゲーム「Freedoom」の
    ゲームデータを、GPL v2 系の Doom エンジン（embeddeddoom）上で動作させています。

    ゲームデータ: Freedoom Phase 1 v0.13.0
      Copyright © 2001-2024 Contributors to the Freedoom project. All rights reserved.
      （修正3条項BSDライセンス / BSD-3-Clause）
        - ソース形式で再配布する場合は、上記著作権表示・条件一覧・免責事項を保持すること
        - バイナリ形式で再配布する場合は、配布物の文書等に同文言を複製すること
        - Freedoom プロジェクトの名称・貢献者名を、事前の書面許可なく製品の推奨・宣伝に使用しないこと

    エンジン: embeddeddoom / DOOM（id Software 由来、GPL-2.0-or-later として扱う）
      GNU GPL v2 全文: doom_payload/licenses/DOOM-GPL-2.0.txt（改変なしで表示）
      https://www.gnu.org/licenses/old-licenses/gpl-2.0.html

    起動表示: Freedoom Phase 1 Startup v1.10（C/GPU で検証済み）

    ファームウェア: RISC-V OpenSBI（BSD-2-Clause、opensbi/COPYING.BSD）
      内包 libfdt は GPL-2.0-or-later または BSD-2-Clause の二重ライセンス。
      OpenSBI 自身の原文（ThirdPartyNotices.md）に従い、BSD-2-Clause として配布する。

    カーネル: Linux（PiMaker linux-rvc、GPL-2.0-only、Linux-syscall-note）
    ユーザーランド: BusyBox（GPL-2.0-only）

    ワールド側: rvc GPU シェーダー（MIT、Copyright (c) 2021 PiMaker）
      UdonSharp（MIT、上流 vrchat-community/UdonSharp の条文を参照）

    ソースコードの入手（予定）: 公開時に、バイナリと同じ配布ページから対応する
    ソースコードを取得できるように提供します（GPL v2 第3条への対応方針）。
    VRChat CDN 経由のバイナリ配布と外部ソース URL が自動的に「同じ場所」になる
    とは主張しません。公開前にこの点をレビューし、必要に応じて第3条(b)の書面
    オファーを併記します。確定値:
    [RELEASE_URL は公開時に確定。PAYLOAD_SHA256 = 14f9859aafbeb8b1d8823b8ebe160d31c57e40c39ded87583e65a8815ca689d9]

    VRChat SDK（Worlds SDK 3.10.3 / Unity 2022.3.22f1）はプロプライエタリであり、
    このソースアーカイブには含まれません。SDK 素材は譲渡・開示・オープンソース化
    できません（EULA: https://hello.vrchat.com/legal/sdk）。UdonSharp は SDK に
    同梱されていますが、SDK パッケージ内に独立したライセンスファイルは無く、
    MIT 条文は上流リポジトリ（vrchat-community/UdonSharp）のものを引用しています。

    ライセンス原文とクレジットは、このワールド内の表示パネルで省略なく
    全ページ閲覧できます（原文のまま・改変なし表示）。
    Freedoom COPYING.txt（30行）、CREDITS.txt（1,030行）、CREDITS-MUSIC.txt（205行）、
    GPL v2 条文（DOOM-GPL-2.0.txt、18,332バイト）、OpenSBI COPYING.BSD、
    rvc LICENSE、UdonSharp LICENSE（上流）、Linux COPYING、BusyBox LICENSE。

補足（日本語）: 閲覧対象は短い要約ではなく完全な文書であること。上記各文書を
全文・改変なしで表示することを要件とする。日本語文は参考訳であり、正式な
条項は常に英語の原文が優先される。

## 11. References and provenance

- Engines and licenses inspected (WSL, pinned snapshot):
  /var/tmp/rvc-doom-20261001/{linux,opensbi}/ (Linux COPYING; OpenSBI
  COPYING.BSD, ThirdPartyNotices.md, lib/utils/libfdt/*),
  /var/tmp/rvc-doom-20261001/doom_payload/cache/embeddeddoom/ (LICENSE.md,
  src/d_main.c header),
  /var/tmp/rvc-doom-20261001/doom_payload/build/freedoom/target/embeddeddoom/src/
  (d_main.c, doomdef.h - private engine tree),
  /var/tmp/rvc-doom-20261001/buildroot-2022.02.1/output/build/busybox-1.35.0/LICENSE,
  /var/tmp/rvc-doom-20261001/doom_payload/cache/freedoom-0.13.0/COPYING.txt,
  /var/tmp/rvc-doom-20261001/doom_payload/build_emdoom.py and
  build/iwad-manifest.json.
- Private patch set (Windows repo, build source pin 17b0a123):
  doom_payload/patches/embeddeddoom-freedoom.patch (src/d_main.c),
  doom_payload/patches/embeddeddoom-baker-bounds.patch (src/d_main.c,
  src/r_data.c); modification log doom_payload/licenses/
  PAYLOAD-MODIFICATIONS.txt.
- rvc MIT text: <rvc-source>/LICENSE (and the world copy
  Assets/ThirdParty/PiMaker/rvc/LICENSE in the gdm-world project).
- UdonSharp: <world-project>/
  Packages/com.vrchat.worlds/Integrations/UdonSharp (SDK 3.10.3 sources;
  package.json lists legacyPackages "com.vrchat.udonsharp"; no standalone
  license file in the integration folder; com.vrchat.worlds/license.txt is
  the SDK EULA pointer).
- Network verification: id-Software/DOOM LICENSE.TXT (GPL-2.0 text;
  contains section 3(a) and the same-place paragraph);
  vrchat-community/UdonSharp LICENSE (MIT grant text). The GNU GPL-2.0 page
  timed out and is retained as the canonical URL.
- Notes: line numbers are from the pinned snapshot; reachability describes
  the OLD source baseline; the neutral startup was subsequently observed in
  the rebuilt C/GPU UART captures. Runtime results and E1M1 hashes are in
  NOTES_DOOM_REPRODUCIBLE.md.


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
