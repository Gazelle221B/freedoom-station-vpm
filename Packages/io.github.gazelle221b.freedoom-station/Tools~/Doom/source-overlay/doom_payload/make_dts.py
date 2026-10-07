#!/usr/bin/env python3
from pathlib import Path
import argparse
import re
import subprocess

root = Path(__file__).resolve().parent.parent
parser = argparse.ArgumentParser()
parser.add_argument("--auto", action="store_true")
parser.add_argument("--verify", action="store_true", help="Auto-start with frame markers for capture/CLI verification")
parser.add_argument("--ascii-trace", action="store_true", help="Separate diagnostic DTB with ASCII sample/view trace")
parser.add_argument("--memory", type=lambda s: int(s, 0), help="RAM length in bytes, e.g. 0x07b00000")
args = parser.parse_args()
if args.verify and not args.auto:
    parser.error("--verify requires --auto")
if args.ascii_trace and not (args.auto and args.verify):
    parser.error("--ascii-trace requires --auto --verify")
source = (root / "dts.dts").read_text()
init = "doomauto" if args.auto else "doominit"
markers = " RVC_DOOM_MARKERS=1" if args.verify else ""
if args.ascii_trace: markers += " RVC_DOOM_ASCII_TRACE=1"
source, count = re.subn(r'bootargs = "[^"]*";', f'bootargs = "rdinit=/{init} console=hvc0 earlycon=sbi{markers}";', source)
if count != 1:
    raise SystemExit("Expected one bootargs entry")
if args.memory:
    source, n = re.subn(r'(reg = <0x0 0x80000000 0x0 )0x[0-9A-Fa-f]+(>;)',
                       lambda m: m[1] + hex(args.memory) + m[2], source)
    if n != 1:
        raise SystemExit("Expected one RAM memory node")
name = "doom-auto-trace" if args.ascii_trace else "doom-auto-verify" if args.verify else "doom-auto" if args.auto else "doom"
dts = root / f"doom_payload/build/{name}.dts"
dts.parent.mkdir(exist_ok=True)
dts.write_text(source)
subprocess.run(["dtc", "-o", str(dts.with_suffix(".dtb")), str(dts)], check=True)
