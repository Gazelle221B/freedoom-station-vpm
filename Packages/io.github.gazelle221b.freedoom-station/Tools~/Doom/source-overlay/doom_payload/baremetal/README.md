# Optional bare-metal Doomgeneric comparison

This adapter boots the pinned lalitshankarch/doomgeneric fork in M-mode on rvc,
using newlib and `-march=rv32ima -mabi=ilp32`. It replaces the rvcore ecall file,
time and console services with payload implementations. Only exit ecall 93 is
handled by the host (`rvc -x`). It is a deterministic E1M1 smoke run without input.

From the rvc root on Linux/ext4:

```sh
make doom-newlib-toolchain             # optional, lengthy GCC/newlib build
make doom-baremetal                   # default /var/tmp/rvc-doom-newlib prefix
make doom-baremetal-run > bare-uart.txt 2> bare-metrics.txt
```

An existing GNU/newlib compiler can be selected with
`DOOM_NEWLIB_PREFIX=/absolute/path/bin/riscv32-unknown-elf-`. The required generator
is `--with-multilib-generator=rv32ima-ilp32--`; RV32G/ilp32d is unsupported.
Host elfy/rvc prerequisites are documented in `../../NOTES_DOOM.md`.

`build_baremetal.py` fetches the pinned engine into ignored cache, compiles its
Makefile source list excluding the two old platform files, turns an ignored file
named `wad` into `wad.o` with objcopy, and links `_binary_wad_start/end` into the
payload. Adding a section to an already linked ELF would not resolve those
symbols and is not the build method used here.

| Region | Address | Use |
|---|---|---|
| Entry | 0x80000000 | startup, code, read-only WAD, data and BSS |
| Heap limit | 0x86000000 | bounded newlib `_sbrk` |
| Framebuffer | 0x86000000 | 320 x 200 x 4 = 256,000 bytes, RGB in low 24 bits |
| Stack top | 0x87F00000 | downward growth |
| UART | 0x10000000 | byte THR +0; byte LSR +5, ready bit 0x20 |

The linker rejects overlap with the framebuffer. Startup initializes gp without
relaxation, sp, aligned BSS and mtvec. A fatal trap prints mcause, mepc and mtval
to UART before exiting with status 1. `_exit` propagates the status to ecall 93.

The read-only WAD is fd 3; stdout/stderr write to UART. Missing configuration,
save paths and other files are unavailable. Sound and input are disabled.
The software clock and `-singletics` keep progress deterministic. Each display
copies DG_ScreenBuffer to the fixed framebuffer, surrounded by numbered UART
markers. The default 90 presentations allow the initial melt wipe to finish;
presentations 61–89 are used for steady E1M1 cost, rather than the first three
mostly black transition images. `RVC_DOOM_BARE_FRAMES` is a compile-time limit.

The host-only `rvc-probe` wrapper optionally dumps `frame-N.ppm` when
`RVC_DOOM_FRAMEBUFFER=doom_payload/build/baremetal/frame` is set. It uses stock
CPU/MMU/memory/UART functions; `src/` remains unchanged. Without the wrapper,
`./rvc -x -e doom_payload/build/baremetal/doom.elf` also reaches all 90 markers
and exits successfully.

Doomgeneric's original GPL text is retained in `../licenses/doomgeneric-LICENSE`.
These adapter files are GPL-2.0-or-later. Shareware WAD data is separately
licensed and never committed. See `../licenses/LICENSE-NOTES.md`.
