#!/usr/bin/env python3
"""Boot C rvc, launch Doom from the guest shell, save UART and host counters."""
import argparse
import json
import os
from pathlib import Path
import re
import subprocess
import time

root = Path(__file__).resolve().parent.parent
parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument("--timeout", type=int, default=900)
parser.add_argument("--frames", type=int, default=3)
parser.add_argument("--plain", action="store_true")
parser.add_argument("--ascii-trace", action="store_true", help="Marker-only guest sample/CDF diagnostics")
parser.add_argument("--ascii-mode", choices=("legacy", "A", "B", "C"), default="legacy",
                    help="Experimental ASCII style; manual --plain startup only")
parser.add_argument("--stock", action="store_true", help="Run original rvc instead of host measurement wrapper")
parser.add_argument("--auto", action="store_true", help="Boot doomauto; do not send a shell command")
parser.add_argument("--world", action="store_true", help="Use marker-free world DTB; count complete 79-column ASCII rows")
args = parser.parse_args()
if args.world and not (args.auto and args.plain):
    parser.error("--world requires --auto --plain")
if args.frames < 1:
    parser.error("--frames must be positive")
if args.ascii_mode != "legacy" and (not args.plain or args.auto):
    parser.error("--ascii-mode A/B/C requires --plain without --auto")
if args.ascii_trace and (not args.plain or args.auto or args.ascii_mode != "C"):
    parser.error("--ascii-trace requires manual --plain --ascii-mode C")
tag = "plain" if args.plain else "ansi"
tag += "-stock" if args.stock else ""
tag += "-auto" if args.auto else ""
tag += "-world" if args.world else ""
tag += "-" + args.ascii_mode if args.ascii_mode != "legacy" else ""
capture = root / f"doom_payload/build/{tag}-uart.bin"
metrics = root / f"doom_payload/build/{tag}-metrics.txt"
binary = root / "rvc" if args.stock else root / "doom_payload/build/rvc-probe"
started = time.monotonic()
command_sent = False
end_marker = f"RVC_DOOM_FRAME_END n={args.frames}".encode()
def finished(data):
    if not args.world:
        return end_marker in data
    game = data.partition(b"RVC_DOOM_AUTOSTART_ASCII")[2]
    rows = re.findall(rb"(?m)^[ .,:;\-=+*o#%@]{79}\r*\n", game)
    return (len(rows) >= 24 * args.frames and b"Z_Init" in game and
            b"RVC_DOOM_FRAME_" not in game and b"RVC_FRAME=" not in game)

with capture.open("wb", buffering=0) as output, metrics.open("wb", buffering=0) as diagnostic:
    dtb = "doom_payload/build/doom-auto.dtb" if args.world else "doom_payload/build/doom-auto-verify.dtb" if args.auto else "doom_payload/build/doom.dtb"
    process = subprocess.Popen([str(binary), "-b", "linux_payload.bin", "-d", dtb], cwd=root,
                               stdin=subprocess.PIPE, stdout=output, stderr=diagnostic)
    last_report = 0
    data = b""
    try:
        while time.monotonic() - started < args.timeout:
            data = capture.read_bytes()
            if not args.auto and not command_sent and b"RVC_DOOM_READY" in data and re.search(rb"(?:/|\n|\r) # |# ", data[-2048:]):
                style = f" RVC_DOOM_ASCII_MODE={args.ascii_mode}" if args.ascii_mode != "legacy" else ""
                trace = " RVC_DOOM_ASCII_TRACE=1" if args.ascii_trace else ""
                command = f"RVC_DOOM_FRAMES={args.frames} RVC_DOOM_TERMINAL={int(args.plain)}{style}{trace} ./emdoom -warp 1 1 -singletics\n"
                process.stdin.write(command.encode())
                process.stdin.flush()
                command_sent = True
                print("Guest shell command sent", flush=True)
            if finished(data):
                break
            if process.poll() is not None:
                raise RuntimeError(f"rvc exited early: {process.returncode}; inspect {capture} and {metrics}")
            elapsed = time.monotonic() - started
            if elapsed - last_report >= 30:
                print(f"elapsed={elapsed:.0f}s uart_bytes={len(data)} guest_shell_command={command_sent}", flush=True)
                last_report = elapsed
            time.sleep(0.2)
    finally:
        if process.poll() is None:
            process.terminate()
            try:
                process.wait(timeout=5)
            except subprocess.TimeoutExpired:
                process.kill()
                process.wait()
    data = capture.read_bytes()
result = dict(passed=finished(data), frames=args.frames, commandSent=command_sent,
              capture=str(capture), metrics=str(metrics), seconds=time.monotonic()-started,
              uartBytes=len(data), stock=args.stock, terminal="plain" if args.plain else "ansi", markers=not args.world,
              asciiMode="C" if args.auto else args.ascii_mode)
(root / f"doom_payload/build/{tag}-result.json").write_text(json.dumps(result, indent=2)+"\n")
print(json.dumps(result), flush=True)
if not result["passed"]:
    raise SystemExit("Doom frame output not reached; inspect saved UART/traces")
