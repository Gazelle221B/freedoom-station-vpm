# License provenance

The rvc emulator remains under its original MIT license; no emulator or shader
source is changed by this payload. The new Doom platform adapters and Doom
measurement/replay tooling are distributed under GPL-2.0-or-later.

The fetched Doom engines are GPL derivatives. The pinned embeddeddoom tree still
contains the original 1997 id Software Limited Use Software License Agreement.
That file is preserved verbatim as `embeddeddoom-LICENSE.md`. The official
id-Software/DOOM README states that Doom source was relicensed under GPL v2;
its GPL text is preserved verbatim as `DOOM-GPL-2.0.txt`. cnlohr's embeddeddoom
README also explicitly permits GPL use of its modifications. No upstream license
has been replaced. The pinned doomgeneric GPL text is `doomgeneric-LICENSE`.
The mini-rv32ima repository's MIT grant is retained as `mini-rv32ima-LICENSE`.

WAD game data has separate terms. `shareware-data-LICENSE.txt` is copied verbatim
from embeddeddoom/src/support/LICENSE; it is not a GPL grant for game data.
`DOOM_IWAD=freedoom` is the world default: pinned Freedoom Phase 1 0.13.0,
under its BSD-3-Clause license. Its unmodified COPYING, CREDITS and music credits
are installed in `/usr/share/licenses/freedoom/` inside the initramfs. The selected
IWAD version/hash is recorded in `/usr/share/licenses/emdoom/IWAD.json`.
`DOOM_IWAD=shareware` retains the private validation build with the separately
licensed shareware data. Neither WAD files nor generated WAD C arrays are
committed. See `FREEDOOM_COMPONENTS.md` for the component inventory and proposed
source distribution before any public world release.

References:
- https://github.com/id-Software/DOOM/blob/master/README.TXT
- https://github.com/id-Software/DOOM/blob/master/LICENSE.TXT
- https://github.com/cnlohr/embeddeddoom/blob/b52f80968a25a90b2ab0cf6c97703876b2d56e59/README.md
- https://github.com/lalitshankarch/doomgeneric/blob/3d6ff5d2f9af84fa7f9093335e5e6d0547d94f05/LICENSE
