# Freedoom Components: License Inventory & Source Distribution Draft

_Investigation report. Does not claim legal clearance; for review only._

Branch feat/doom-freedoom, HEAD dba194ef. Linux payload only (baremetal out of scope).
Components that would be embedded in the VRChat world artifact. No publication
has been performed for this task.
Build-only tools and Editor-only probes excluded (section 4).

---

## 1.  Freedoom Phase 1 WAD -- v0.13.0 (BSD-3-Clause)

https://github.com/freedoom/freedoom/releases/tag/v0.13.0

Archive SHA-256: 3f9b264f3e3ce503b4fb7f6bdcb1f419d93c7b546f4df3e874dd878db9688f59
WAD SHA-256:   7323bcc168c5a45ff10749b339960e98314740a734c30d4b9f3337001f9e703d (28,795,076 bytes)

License: Modified 3-clause BSD. Copyright (c) 2001-2024 Freedoom contributors.
Local: cache/freedoom-0.13.0/COPYING.txt

Embedded in /emdoom as compiled C array (rawwad.c_resource). WAD data is BSD-licensed;
embedding does not change its license. Notice files in initramfs:
- /usr/share/licenses/freedoom/{COPYING,CREDITS,CREDITS-MUSIC}.txt
- /usr/share/licenses/emdoom/IWAD.json

embeddeddoom has no DEHACKED parser; original Doom text strings persist. E1M1 only.

---

## 2.  Distributed Components (in VRChat world)

### rvc GPU Compute Shader -- MIT

_Nix/rvc/src/ (header.p, console.shader, generated .h/.cginc).
PiMaker/rvc, base cnlohr/mini-rv32ima. MIT (repo root LICENSE).
Local: doom_payload/licenses/mini-rv32ima-LICENSE.

### Linux Kernel -- GPL-2.0

v5.17.11, PiMaker fork at submodule 0bd94b14. GPL-2.0 (linux/COPYING).
PiMaker fork includes Sv32 MMU. SUM/MPRV/SRET commits in rvc-doom repo are
C emulator fixes (src/), not kernel modifications.

### OpenSBI -- BSD-2-Clause

RISC-V OpenSBI, submodule 7aa6c9aa. BSD-2-Clause (opensbi/COPYING.BSD).
Contains libfdt (GPL-2.0+ OR BSD-2-Clause); OpenSBI elects BSD-2-Clause.

### embeddeddoom -- GPL-2.0+ (permissive overlay)

cnlohr/embeddeddoom at b52f80968. id Software Doom source GPL-2.0+;
cnlohr modifications additionally BSD/MIT/GPL/AFL.
Local: embeddeddoom-LICENSE.md, DOOM-GPL-2.0.txt.

### Buildroot Userland (in initramfs cpio)

Buildroot 2022.02.1 (GPL-2.0, build tool). Verified from
/var/tmp/rvc-doom-20261001:

| Component | Version | License | Rootfs path |
|---|---|---|---|
| BusyBox | 1.35.0 | GPL-2.0 | /bin/busybox (all applets) |
| glibc + support libs | 2.34-109 | LGPL-2.1+ | /lib/libc.so.6, ld-linux, libm, libdl, librt, libpthread, libcrypt, libresolv, libnss_*, libutil, libanl |
| libgcc_s | gcc 11.2.0 | GPL-3.0+ w/ RLE | /lib/libgcc_s.so.1 |
| libstdc++ | gcc 11.2.0 | GPL-3.0+ w/ RLE | /usr/lib/libstdc++.so.6.0.29 |
| libatomic | gcc 11.2.0 | GPL-3.0+ w/ RLE | /lib/libatomic.so.1.2.0 |
| tzdata | 2021e | Public domain | /usr/share/zoneinfo |

RLE = GCC Runtime Library Exception (permits proprietary linking).

### Custom Payload -- GPL-2.0-or-later

Per LICENSE-NOTES.md. Executed in the distributed artifact:
- video_console.c (compiled into /emdoom)
- doominit, doomauto (init scripts in initramfs)
- unity/Runtime/*.cs (DoomControl.cs, DoomInputAck.cs, DoomKeyButton.cs)

Corresponding-source build scripts (provided with source, not executed in world):
build_emdoom.py, build_linux.py, install_rootfs.sh.

Editor/*.cs and Editor/*.shader are Unity Editor-only probe infrastructure,
NOT embedded in the world artifact.

### VRChat Worlds SDK -- PROPRIETARY EULA

SDK 3.10.3, Unity 2022.3.22f1. https://hello.vrchat.com/legal/sdk.
SDK License: SDK materials may not be transferred, distributed, disclosed,
or made available to third parties; may not be used to develop software for
use outside VRChat; may not be placed under an open-source license.
Independent aggregation analysis in section 5.

### UdonSharp -- MIT

MerlinVR/UdonSharp, MIT. Included in VRChat SDK Worlds package.

---

## 3.  GPL Corresponding Source Distribution Proposal

linux_payload.bin (OpenSBI + Linux Image + cpio initramfs) is uploaded as
texture data in the world asset bundle and downloaded by each VRChat client.

GPL-2.0 section 3 requires one of: (a) source accompanying the executable,
(b) a written offer valid >=3 years to any third party for source at cost of
physical distribution, or (c) passing through a received 3(b) offer. The final
paragraph adds: if executable distribution is made by offering access from a
designated place, equivalent access to source from the same place counts.

**Proposal (subject to review, not a compliance declaration):** Provide source
archive at a stable URL containing all GPL-covered source at pinned revisions
plus build configs. Whether a separate source site satisfies the "equivalent
access from the same place" standard depends on interpretation of the VRChat
CDN binary delivery model. A conservative approach would additionally provide
a written 3(b) offer within the world. This proposal requires review.

| Binary | Source to provide |
|---|---|
| Linux Image | linux-rvc at 0bd94b14 + generated .config |
| emdoom | embeddeddoom at b52f80968 + wadder + stripchoice + build_emdoom.py |
| BusyBox 1.35.0 | Source + Buildroot config |
| glibc 2.34-109 | Source (LGPL; include for completeness) |
| libgcc_s, libstdc++, libatomic | GCC 11.2.0 source (RLE; include for completeness) |
| Custom payload | video_console.c, doominit, doomauto, unity/Runtime/*.cs; build scripts provided with source |
| Build config | linux.config, buildroot-config, Makefile.inc, prep scripts |

Freedoom WAD (BSD) and OpenSBI (BSD) can be included for reproducibility with
their notices preserved. Exclude the proprietary VRChat SDK from source archives;
have recipients obtain it through its official package distribution.

---

## 4.  Build-Only and Dev-Only (NOT Distributed)

| Component | Reason excluded |
|---|---|
| Buildroot, GCC, binutils (host) | Build tools, not world binaries; preserve required build scripts/configs in the source package |
| C host rvc binary | Capture/replay; dev tool, not in world |
| doom_payload/unity/Editor/* | Unity Editor probes; stripped from build |
| doomgeneric/baremetal | Dev-only, out of scope; not verified with Freedoom |
| shareware doom1.wad | Legacy IWAD, not in freedoom build |
| riscv-tests, riscv-opcodes, perlpp, Cargo, Python, GNU Make | Test infra / build orchestration |

---

## 5.  Licensing Boundary and Unresolved Items

**Primary shader (MIT) + GPL binary in texture.** The rvc GPU compute shader
in the world is MIT. The GPL linux_payload.bin uploaded as texture data carries
its own corresponding-source obligations (section 3). An MIT interpreter running
GPL code does not by itself make the shader or world GPL-licensed.

**VRChat SDK EULA boundary (UNRESOLVED).** The SDK is a build dependency; this
task does not redistribute its package. Doom runtime C# compiles to Udon
bytecode using VRChat/SDK externs. The guest Linux/emdoom program has its own
address space, which supports treating it as a separate program, but this is
not a licensing clearance for every script or SDK asset in a world. Before
publication, review that boundary and any applicable asset-specific licenses.

**Freedoom data license.** BSD-3-Clause. Compilation as C array does not change
data copyright status; BSD permits redistribution under same terms.

**Not covered:** VRChat TOS, Unity Editor EULA, jurisdictional GPL distribution
questions regarding VRChat CDN model, VRChat content guidelines.

Before publication, assemble the complete component notices (including OpenSBI
COPYING.BSD and the actual userland package notices) alongside the source
archive. Buildroot `make legal-info` helps collect its packages' sources and
licenses; separately include Linux/OpenSBI/custom payload sources and retain
any warnings or omissions from that collection. No such archive is published
by this task.

---

## 6.  References

- rvc-doom LICENSE: MIT (PiMaker 2021)
- Freedoom: https://freedoom.github.io/ | COPYING: cache/freedoom-0.13.0/COPYING.txt
- embeddeddoom: https://github.com/cnlohr/embeddeddoom (b52f80968)
- Doom GPL: https://github.com/id-Software/DOOM/blob/master/README.TXT
- Linux: https://github.com/PiMaker/linux-rvc (0bd94b14)
- OpenSBI: https://github.com/riscv-software-src/opensbi (7aa6c9aa)
- UdonSharp: https://github.com/MerlinVR/UdonSharp (MIT)
- VRChat SDK: https://hello.vrchat.com/legal/sdk
- Buildroot: buildroot-2022.02.1/COPYING (GPL-2.0)
- Build cache: /var/tmp/rvc-doom-20261001/ (WSL)
- IWAD manifest: doom_payload/build/iwad-manifest.json
- Local licenses: doom_payload/licenses/*
