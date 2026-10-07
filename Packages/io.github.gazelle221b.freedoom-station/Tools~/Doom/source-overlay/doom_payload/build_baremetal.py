#!/usr/bin/env python3
"""Optional RV32IMA/newlib Doomgeneric build; no WAD is tracked."""
import argparse
from concurrent.futures import ThreadPoolExecutor
from pathlib import Path
import re
import shutil
import subprocess
import build_emdoom

HERE = Path(__file__).resolve().parent
PIN = "3d6ff5d2f9af84fa7f9093335e5e6d0547d94f05"


def run(*args, cwd=None):
    subprocess.run(args, cwd=cwd, check=True)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--prefix", default="/var/tmp/rvc-doom-newlib/bin/riscv32-unknown-elf-")
    parser.add_argument("--jobs", type=int, default=16)
    args = parser.parse_args()
    # Reuse the shareware download and integrity check, and E1M1 source cache.
    build_emdoom.prepare(None, False, "shareware")
    checkout = HERE / "cache" / "doomgeneric"
    if not (checkout / ".git").exists():
        run("git", "clone", "--no-checkout", "https://github.com/lalitshankarch/doomgeneric.git", str(checkout))
    run("git", "-C", str(checkout), "checkout", "--detach", PIN)
    source = checkout / "doomgeneric"
    build = HERE / "build" / "baremetal"
    build.mkdir(parents=True, exist_ok=True)
    shutil.copyfile(HERE / "cache" / "doom1.wad", build / "wad")
    run(args.prefix + "objcopy", "-I", "binary", "-O", "elf32-littleriscv", "-B", "riscv",
        "--rename-section", ".data=.rodata,alloc,load,readonly,data,contents", "wad", "wad.o", cwd=build)
    objects = re.search(r"^SRC_DOOM\s*=\s*(.+)$", (source / "Makefile").read_text(), re.M).group(1).split()
    sources = [source / (obj[:-2] + ".c") for obj in objects
               if obj not in ("doomgeneric.o", "doomgeneric_rvdoom.o")]
    sources += [HERE / "baremetal" / name for name in ("start.S", "adapter.c", "syscalls.c")]
    flags = ["-march=rv32ima", "-mabi=ilp32", "-nostartfiles", "-O2", "-g",
             "-ffunction-sections", "-fdata-sections", "-DNORMALUNIX", "-DLINUX", "-DSNDSERV",
             "-D_DEFAULT_SOURCE", "-DDOOMGENERIC_RESX=320", "-DDOOMGENERIC_RESY=200", "-I" + str(source)]
    def compile_one(src):
        obj = build / (src.stem + ".o")
        run(args.prefix + "gcc", *flags, "-c", str(src), "-o", str(obj))
        return str(obj)
    with ThreadPoolExecutor(max_workers=args.jobs) as pool:
        compiled = list(pool.map(compile_one, sources))
    # Put our startup object first: rvc's ELF loader starts at 0x80000000.
    compiled.sort(key=lambda p: 0 if p.endswith("/start.o") else 1)
    binary = build / "doom.elf"
    run(args.prefix + "gcc", *flags, "-static", "-T", str(HERE / "baremetal" / "link.ld"),
        "-Wl,--gc-sections", "-Wl,-Map," + str(build / "doom.map"), *compiled, str(build / "wad.o"),
        "-Wl,--start-group", "-lc", "-lm", "-lgcc", "-Wl,--end-group", "-o", str(binary))
    header = subprocess.check_output([args.prefix + "readelf", "-h", str(binary)], text=True)
    print(header)
    if "RVC" in header or "float" in header or "ELF32" not in header:
        raise SystemExit("Expected RV32 soft-float without compressed instructions")
    run(args.prefix + "objcopy", "-O", "binary", str(binary), str(build / "doom.bin"))
    print("Built", binary)


if __name__ == "__main__":
    main()
