#!/usr/bin/env python3
"""Embed the Doom cpio rootfs in the existing MMU Linux/OpenSBI payload."""
import argparse
import os
from pathlib import Path
import subprocess
import shutil
from normalize_cpio import normalize
from reproducible import environment, prefix_flags

root = Path(__file__).resolve().parent.parent
parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument("--jobs", type=int, default=16)
args = parser.parse_args()
prefix = str(root / "buildroot-2022.02.1/output/host/bin/riscv32-buildroot-linux-gnu-")
archive = root / "buildroot-2022.02.1/output/images/rootfs.cpio"
archive.write_bytes(normalize(archive.read_bytes()))
config = (root / "linux.config").read_text()
config += '\nCONFIG_BLK_DEV_INITRD=y\nCONFIG_INITRAMFS_SOURCE="' + str(root / "buildroot-2022.02.1/output/images/rootfs.cpio") + '"\nCONFIG_INITRAMFS_COMPRESSION_NONE=y\n'
fragment = root / "doom_payload/build/linux.config"
fragment.write_text(config)
env = environment()
env["KCFLAGS"] = prefix_flags(root)
env["KAFLAGS"] = prefix_flags(root) + f" -Wa,--debug-prefix-map={root}=/usr/src/rvc"
env.update(ARCH="riscv", CROSS_COMPILE=prefix, KCONFIG_ALLCONFIG=str(fragment))
subprocess.run(["make", "allnoconfig"], cwd=root / "linux", env=env, check=True)
subprocess.run(["make", f"-j{args.jobs}", "Image"], cwd=root / "linux", env=env, check=True)
env.update(PLATFORM="generic", PLATFORM_RISCV_XLEN="32", PLATFORM_RISCV_ISA="rv32ima",
           PLATFORM_RISCV_ABI="ilp32", FW_PAYLOAD_PATH=str(root / "linux/arch/riscv/boot/Image"), FW_PIC="n",
           GENFLAGS=prefix_flags(root))
subprocess.run(["make", "clean"], cwd=root / "opensbi", env=env, check=True)
subprocess.run(["make", f"-j{args.jobs}", "all"], cwd=root / "opensbi", env=env, check=True)
firmware = root / "opensbi/build/platform/generic/firmware"
for extension in ("bin", "elf"):
    shutil.copyfile(firmware / ("fw_payload." + extension), root / ("linux_payload." + extension))
print("Doom initramfs embedded in linux_payload.bin")
