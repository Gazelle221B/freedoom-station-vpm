#!/usr/bin/env python3
"""Build pinned embeddedDOOM; WAD and all derived data stay ignored."""
import argparse
import hashlib
import os
from pathlib import Path
import shutil
import subprocess
import re
from iwad import acquire, e1m1_only, manifest, read_lumps
import json
from reproducible import environment, prefix_flags
from check_baked import check_baked

HERE = Path(__file__).resolve().parent
PIN = "b52f80968a25a90b2ab0cf6c97703876b2d56e59"
WAD_SHA = "1d7d43be501e67d927e415e0b8f3e29c3bf33075e859721816f652a526cac771"


def run(*args, cwd=None, env=None):
    subprocess.run(args, cwd=cwd, env=env, check=True)


def prepare(wad, host_only, iwad="freedoom", engine_source=None):
    cache = HERE / "cache"
    cache.mkdir(exist_ok=True)
    checkout = engine_source or cache / "embeddeddoom"
    if engine_source is not None:
        # Offline source releases carry a hash-verified export and its source pin.
        if (checkout / ".source-pin").read_text().strip() != PIN:
            raise SystemExit("Offline engine source pin does not match")
    else:
        if not (checkout / ".git").exists():
            run("git", "clone", "--no-checkout", "https://github.com/cnlohr/embeddeddoom.git", str(checkout))
        run("git", "-C", str(checkout), "checkout", "--detach", PIN)
    wad = acquire(iwad, wad)
    destination = HERE / "build" / iwad / ("host" if host_only else "target") / "embeddeddoom"
    stamp = destination / ".source-pin"
    if not stamp.exists():
        destination.parent.mkdir(parents=True, exist_ok=True)
        shutil.copytree(checkout, destination, ignore=shutil.ignore_patterns(".git", "*.wad", "rawwad.c"))
        stamp.write_text(PIN + "\n")
    if stamp.read_text().strip() != PIN:
        raise SystemExit("Different source pin in build directory; use a fresh build directory")
    patches = [HERE / "patches/embeddeddoom-baker-bounds.patch"]
    if iwad == "freedoom":
        patches.append(HERE / "patches/embeddeddoom-freedoom.patch")
    patch_hash = hashlib.sha256(b"".join(p.read_bytes() for p in patches)).hexdigest()
    patch_stamp = destination / ".private-patches"
    if not patch_stamp.exists() or patch_stamp.read_text().strip() != patch_hash:
        for name in ("d_main.c", "r_data.c"):
            shutil.copyfile(checkout / "src" / name, destination / "src" / name)
        for patch in patches:
            run("patch", "--batch", "--fuzz=0", "-p1", "-i", str(patch), cwd=destination)
        # Baking depends on these sources, but upstream's coarse rules do not
        # invalidate every generated array when a private patch changes.
        run("make", "clean", cwd=destination / "src")
        patch_stamp.write_text(patch_hash + "\n")
    support = destination / "src" / "support"
    data = wad.read_bytes()
    if iwad == "freedoom":
        data = e1m1_only(data)
    (support / "doom1.wad").write_bytes(data)
    for choice in ("stripchoice.txt", "stripchoicebegin.txt"):
        if iwad == "shareware":
            shutil.copyfile(support / "stripchoice-E1M1ONLY.txt", support / choice)
        else:
            # Retain all E1M1 actor/texture dependencies. The host baker records
            # actually required sprites; do not reuse Doom's map-specific blacklist.
            (support / choice).write_text("-DEMO*\n-TITLEPIC\n-CREDIT\n-HELP*\n-M_*\n-WI*\n-D_*\n+D_E1M1\n-GENMIDI\n")
    info = manifest(iwad, wad)
    info.update(mapSubsetBytes=len(data), subsetLumps=len(read_lumps(data)))
    (destination.parent / "iwad.json").write_text(json.dumps(info, indent=2)+'\n')
    return destination / "src"


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--cc", default=str(HERE.parent / "buildroot-2022.02.1/output/host/bin/riscv32-buildroot-linux-gnu-gcc"))
    parser.add_argument("--wad", type=Path)
    parser.add_argument("--host-only", action="store_true")
    parser.add_argument("--engine-source", type=Path, help="Hash-verified offline source export with .source-pin")
    parser.add_argument("--iwad", choices=("freedoom", "shareware"), default=os.environ.get("DOOM_IWAD", "freedoom"))
    parser.add_argument("--heap-bytes", type=int, default=384*1024)
    args = parser.parse_args()
    source = prepare(args.wad.resolve() if args.wad else None, args.host_only, args.iwad, args.engine_source.resolve() if args.engine_source else None)
    common = "-Os -g -DE1M1ONLY=1 -DNORMALUNIX -DLINUX -DMAXPLAYERS=1 -DDISABLE_NETWORK -DSET_MEMORY_DEBUG=0 -DRANGECHECK -fdata-sections -ffunction-sections"
    common += " " + prefix_flags(HERE.parent)
    include = f"-I{source.parent.parent}"
    host = "-m32 " + common + " -DIS_ON_DESKTOP_NOT_RV_EMULATOR " + include
    target = (host if args.host_only else "-march=rv32ima -mabi=ilp32 -fno-pie -flto " + common + " " + include + f" -DFIXED_HEAP={args.heap_bytes}")
    env = environment()
    env.update(CC="gcc" if args.host_only else args.cc, HOSTGCC="gcc", CC_HOST="gcc",
               CFLAGS=target, CFLAGS_FINAL=target, CFLAGS_HOST=host,
               LDFLAGS_HOST="-m32 -Wl,--gc-sections", LIBS_HOST="-lm -lpthread",
               LDFLAGS="-Wl,--gc-sections" if args.host_only else "-static -no-pie -flto -Wl,--gc-sections,--build-id=none",
               LIBS="-lm -lpthread", CS=str(HERE / "video_console.c"), EXTRA_CFLAGS="-DE1M1ONLY=1")
    build_flags = source / '.payload-build-flags'
    signature = target + '\n' + env['LDFLAGS'] + '\n' + hashlib.sha256((HERE / 'video_console.c').read_bytes()).hexdigest()
    if build_flags.exists() and build_flags.read_text() != signature:
        run('make', 'clean', cwd=source, env=env)
    run("make", "support/rawwad.c", cwd=source, env=env)
    # Upstream wadder writes .c_resource although its Makefile expects .c.
    # Keep that filename adaptation in the payload build, outside the engine.
    resource = source / "support/rawwad.c_resource"
    if resource.exists():
        shutil.copyfile(resource, source / "support/rawwad.c")
    run("make", "emdoom", cwd=source, env=env)
    print("Baker bounds verified:", check_baked(source / "support"))
    build_flags.write_text(signature)
    binary = HERE / "build" / ("emdoom-host" if args.host_only else "emdoom")
    shutil.copyfile(source / "emdoom", binary)
    if not args.host_only:
        # The firmware carries executable data, not toolchain debug-path tables.
        # Retain the unstripped engine and a separate debug file locally.
        objcopy = args.cc.removesuffix("gcc") + "objcopy"
        run(objcopy, "--only-keep-debug", str(binary), str(binary)+".debug")
        run(objcopy, "--strip-debug", str(binary))
    binary.chmod(0o755)
    profile = HERE / "build" / args.iwad
    shutil.copyfile(binary, profile / binary.name)
    info = json.loads((source.parent.parent / "iwad.json").read_text())
    info.update(elfBytes=binary.stat().st_size, heapBytes=args.heap_bytes)
    size = re.search(r'unsigned char rawwad\[(\d+)\]', (source / 'support/rawwad_use.h').read_text())
    if not size:
        raise SystemExit('Pinned final WAD declaration does not match')
    info['embeddedWadBytes'] = int(size[1])
    if not args.host_only:
        (HERE / "build/iwad-selection.txt").write_text(args.iwad+'\n')
        (HERE / "build/iwad-manifest.json").write_text(json.dumps(info, indent=2)+'\n')
        (profile / "iwad-manifest.json").write_text(json.dumps(info, indent=2)+'\n')
    if not args.host_only:
        readelf = args.cc.removesuffix("gcc") + "readelf"
        header = subprocess.check_output([readelf, "-h", str(binary)], text=True)
        print(header)
        if "ELF32" not in header or "EXEC" not in header or "RISC-V" not in header:
            raise SystemExit("Expected a regular ELF32 static RISC-V executable")
        if "RVC" in header or "double-float" in header or "single-float" in header:
            raise SystemExit("Compressed or hardware floating-point ABI is forbidden")
        program_headers = subprocess.check_output([readelf, "-l", str(binary)], text=True)
        if "INTERP" in program_headers:
            raise SystemExit("Doom must be statically linked")
    print("Built", binary)


if __name__ == "__main__":
    main()
