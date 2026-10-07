#!/usr/bin/env python3
"""prepare_isa_tests.py -- Convert riscv-tests ISA ELFs to RVC-DOOM RAM textures."""

import argparse, hashlib, json, os, re, struct, sys, zlib


def write_png(path, width, height, pixels):
    """Write RGBA PNG from flat list of (r,g,b,a) tuples."""
    def chunk(ctype, data):
        c = ctype + data
        crc = struct.pack(">I", zlib.crc32(c) & 0xFFFFFFFF)
        return struct.pack(">I", len(data)) + c + crc
    raw_rows = []
    for y in range(height):
        row_pixels = pixels[y * width:(y + 1) * width]
        row_bytes = bytes([0])
        for px in row_pixels:
            row_bytes += bytes(px)
        raw_rows.append(row_bytes)
    raw = b"".join(raw_rows)
    header = bytes([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A])
    ihdr = chunk(b"IHDR", struct.pack(">IIBBBBB", width, height, 8, 6, 0, 0, 0))
    idat = chunk(b"IDAT", zlib.compress(raw))
    iend = chunk(b"IEND", b"")
    with open(path, "wb") as f:
        f.write(header + ihdr + idat + iend)


PT_LOAD = 1
ELF_MAGIC = bytes([0x7F, 0x45, 0x4C, 0x46])


def parse_elf(path):
    with open(path, "rb") as f:
        elf = f.read()
    if len(elf) < 52 or elf[:4] != ELF_MAGIC or elf[4:6] != b'\x01\x01':
        raise ValueError("Expected a 32-bit little-endian ELF")
    if struct.unpack_from("<H", elf, 18)[0] != 243:
        raise ValueError("Expected RISC-V ELF")
    e_entry = struct.unpack_from("<I", elf, 24)[0]
    e_phoff = struct.unpack_from("<I", elf, 28)[0]
    e_phentsize = struct.unpack_from("<H", elf, 42)[0]
    e_phnum = struct.unpack_from("<H", elf, 44)[0]
    if e_phentsize != 32 or e_phoff + e_phentsize * e_phnum > len(elf):
        raise ValueError("Invalid ELF program header table")
    segs = []
    for i in range(e_phnum):
        off = e_phoff + i * e_phentsize
        t, o, va, pa, fsz, msz, fl, al = struct.unpack_from("<IIIIIIII", elf, off)
        if t == PT_LOAD:
            if fsz > msz or o + fsz > len(elf):
                raise ValueError("Invalid PT_LOAD size")
            segs.append({"offset": o, "vaddr": va, "filesz": fsz, "memsz": msz, "flags": fl})
    if not segs:
        raise ValueError("No ELF load segments")
    return {"entry": e_entry, "segments": segs, "path": path}


TEX_W = 2048
BPP = 16


def elf_to_textures(info, base=0x80000000):
    """Build RAM textures. The Nix/rvc commit pass reads RAM with
    pos2.y = ram_dim.y - pos2.y - 1, so we write in natural row order."""
    segs = info["segments"]
    max_end = max((s["vaddr"] + s["memsz"]) for s in segs)
    ram_sz = max_end - base
    rows = (ram_sz + TEX_W * BPP - 1) // (TEX_W * BPP)
    buf = bytearray(rows * TEX_W * BPP)
    for s in segs:
        off = s["vaddr"] - base
        with open(info["path"], "rb") as f:
            f.seek(s["offset"])
            data = f.read(s["filesz"])
        buf[off:off + len(data)] = data
    pixels = []
    for py in range(rows):
        for px in range(TEX_W):
            b = (py * TEX_W + px) * BPP
            pixels.append((
                (buf[b+0], buf[b+1], buf[b+2], buf[b+3]),
                (buf[b+4], buf[b+5], buf[b+6], buf[b+7]),
                (buf[b+8], buf[b+9], buf[b+10], buf[b+11]),
                (buf[b+12], buf[b+13], buf[b+14], buf[b+15]),
            ))
    return TEX_W, rows, pixels


def test_inventory(path=None):
    """Use the exact C suite, including only its intentionally selected mi/si tests."""
    path = path or os.path.join(os.path.dirname(__file__), '..', 'test.sh')
    with open(path, 'rb') as stream:
        source = stream.read()
    match = re.search(r'^\s*TESTS="([^"]*)"', source.decode('utf-8'), re.MULTILINE)
    if not match:
        raise ValueError('test.sh all inventory not found')
    names = [row.strip() for row in match[1].splitlines()
             if row.strip() and not row.strip().startswith('#')]
    if not names or len(names) != len(set(names)):
        raise ValueError('Empty or duplicate C test inventory')
    if any(not re.fullmatch(r'rv32[a-z0-9]+-p-[a-z0-9_]+', name) for name in names):
        raise ValueError('Unsupported C test inventory entry')
    return names, hashlib.sha256(source).hexdigest()


def validate_elf(info, name):
    if info["entry"] != 0x80000000:
        return "bad entry 0x%08x" % info["entry"]
    for s in info["segments"]:
        if s["vaddr"] < 0x80000000:
            return "vaddr 0x%08x below 0x80000000" % s["vaddr"]
        if s["vaddr"] + s["memsz"] > 0x80000000 + 2048 * (4096 - 64) * 16:
            return "ELF load segment exceeds shader RAM"
    return None


def main():
    p = argparse.ArgumentParser()
    p.add_argument("--data-dir", required=True)
    p.add_argument("--out-dir", default=os.path.join(os.path.dirname(__file__), "build", "isa-textures"))
    p.add_argument("--list", action="store_true")
    p.add_argument("--test")
    p.add_argument("--dump", action="store_true")
    p.add_argument("--max-tests", type=int, default=0)
    p.add_argument("--manifest", help="Common C/GPU inventory (for env/v fixtures)")
    args = p.parse_args()
    dd = os.path.expanduser(args.data_dir)
    if not os.path.isdir(dd):
        print("ERROR: %s not found" % dd, file=sys.stderr); sys.exit(1)
    tests, inventory_hash = test_inventory()
    supplied_manifest = None
    if args.manifest:
        with open(args.manifest, encoding='utf-8') as stream:
            supplied_manifest = json.load(stream)
        if not supplied_manifest.get('complete') or supplied_manifest.get('testShSha256') != inventory_hash:
            raise ValueError('Incomplete or stale shared inventory')
        tests = supplied_manifest['expected']
        if not tests or len(tests) != len(set(tests)) or any(
                not re.fullmatch(r'rv32(?:ui|um|ua)-v-[a-z0-9_]+', n) for n in tests):
            raise ValueError('Invalid virtual inventory')
    if args.list:
        print("%d tests (test.sh all inventory)" % len(tests))
        for t in tests: print("  " + t)
        return
    targets = [args.test] if args.test else tests
    if args.max_tests > 0: targets = targets[:args.max_tests]
    od = os.path.expanduser(args.out_dir)
    os.makedirs(od, exist_ok=True)
    ok = fail = skip = 0
    prepared = []
    for name in targets:
        fp = os.path.join(dd, name)
        if not os.path.isfile(fp):
            print("SKIP: " + name); skip += 1; continue
        if supplied_manifest:
            expected_hash = next(r['sha256'] for r in supplied_manifest['records'] if r['test'] == name)
            with open(fp, 'rb') as stream:
                if hashlib.sha256(stream.read()).hexdigest() != expected_hash:
                    raise ValueError('C/GPU ELF hash mismatch: '+name)
        try:
            info = parse_elf(fp)
        except Exception as e:
            print("FAIL: %s parse: %s" % (name, e)); fail += 1; continue
        err = validate_elf(info, name)
        if err:
            print("SKIP: %s %s" % (name, err)); skip += 1; continue
        if args.dump:
            print("=== %s ===" % name)
            print("  entry: 0x%08x" % info["entry"])
            for si, s in enumerate(info["segments"]):
                rwx = ("R" if s["flags"]&4 else "-") + ("W" if s["flags"]&2 else "-") + ("X" if s["flags"]&1 else "-")
                print("  LOAD%d: vaddr=0x%08x filesz=0x%x memsz=0x%x %s" % (si, s["vaddr"], s["filesz"], s["memsz"], rwx))
            continue
        try:
            w, h, px = elf_to_textures(info)
        except Exception as e:
            print("FAIL: %s texture: %s" % (name, e)); fail += 1; continue
        for ci, cn in enumerate(["r","g","b","a"]):
            write_png(os.path.join(od, "%s.%s.png" % (name, cn)), w, h, [p[ci] for p in px])
        print("OK: %s (%dB) -> %dx%d" % (name, os.path.getsize(fp), w, h))
        ok += 1
        prepared.append(name)
    if args.dump:
        sys.exit(0 if fail == 0 and skip == 0 else 1)
    suite = ('explicit test' if args.test else
             'test.sh subset' if len(targets) != len(tests) else 'test.sh all')
    # Keep requested cases even when absent: a partial corpus must not look complete.
    manifest = dict(suite=suite,
                    testShSha256=inventory_hash, expected=targets, prepared=prepared,
                    complete=fail == 0 and skip == 0 and len(prepared) == len(targets))
    if supplied_manifest:
        manifest['suite'] = supplied_manifest['suite']
        manifest['records'] = supplied_manifest['records']
    with open(os.path.join(od, 'manifest.json'), 'w', encoding='utf-8') as stream:
        json.dump(manifest, stream, indent=2)
        stream.write('\n')
    print("Done: %d prepared, %d failed, %d skipped" % (ok, fail, skip))
    sys.exit(0 if manifest['complete'] else 1)


if __name__ == "__main__":
    main()
