#!/usr/bin/env python3
"""Build the production payload from a pinned source tree (Linux host)."""
import argparse
from pathlib import Path
import subprocess
from reproducible import BUILDROOT_VERSION, environment


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--engine-source", type=Path, required=True)
    parser.add_argument("--jobs", type=int, default=16)
    args = parser.parse_args()
    root = Path(__file__).resolve().parent.parent
    env = environment()
    env["RVC_DOOM_ENGINE_SRC"] = str(args.engine_source.resolve())
    env["PATH"] = "/usr/local/sbin:/usr/local/bin:/usr/sbin:/usr/bin:/sbin:/bin"
    commands = [
        ["python3", "doom_payload/prepare_rootfs.py"],
        ["make", "-C", "buildroot-2022.02.1", "BR2_VERSION_FULL="+BUILDROOT_VERSION, f"-j{args.jobs}", "toolchain"],
        ["python3", "doom_payload/build_emdoom.py", "--iwad", "freedoom", "--engine-source", env["RVC_DOOM_ENGINE_SRC"]],
        ["make", "-C", "buildroot-2022.02.1", "BR2_VERSION_FULL="+BUILDROOT_VERSION, f"-j{args.jobs}"],
        ["python3", "doom_payload/build_linux.py", "--jobs", str(args.jobs)],
    ]
    for command in commands:
        subprocess.run(command, cwd=root, env=env, check=True)
    for flags in ([], ["--auto"], ["--auto", "--verify"], ["--auto", "--verify", "--ascii-trace"]):
        subprocess.run(["python3", "doom_payload/make_dts.py", "--memory", "0x07b00000", *flags], cwd=root, env=env, check=True)


if __name__ == "__main__":
    main()
