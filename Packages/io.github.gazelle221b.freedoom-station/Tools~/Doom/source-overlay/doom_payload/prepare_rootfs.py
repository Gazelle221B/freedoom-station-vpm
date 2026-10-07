#!/usr/bin/env python3
"""Select a small Doom rootfs without changing the rvc toolchain ABI."""
from pathlib import Path
import re
import subprocess
from reproducible import environment, prefix_flags, BUILDROOT_VERSION

root = Path(__file__).resolve().parent.parent
buildroot = root / "buildroot-2022.02.1"
config = (root / "buildroot-config").read_text()
keep = ("BR2_PACKAGE_HOST_", "BR2_PACKAGE_GLIBC", "BR2_PACKAGE_LINUX_HEADERS",
        "BR2_PACKAGE_BUSYBOX", "BR2_PACKAGE_SKELETON", "BR2_PACKAGE_HAS_SKELETON")
config = "\n".join(
    "# " + line.split("=", 1)[0] + " is not set"
    if re.match(r"BR2_PACKAGE_\w+=y$", line) and not line.startswith(keep)
    else line for line in config.splitlines()
)
settings = {
    "BR2_REPRODUCIBLE": "y",
    "BR2_TARGET_OPTIMIZATION": '"-Os ' + prefix_flags(root) + '"',
    "BR2_ROOTFS_POST_BUILD_SCRIPT": f'"{root / "doom_payload/install_rootfs.sh"}"',
    "BR2_GNU_MIRROR": '"https://ftp.gnu.org/gnu"',
    "BR2_BACKUP_SITE": '"https://sources.buildroot.net"',
}
for key, value in settings.items():
    config = re.sub(r"^(?:# )?" + key + r"(?:=.*| is not set)$", "", config, flags=re.M)
    config += "\n" + key + "=" + value
(buildroot / ".config").write_text(config + "\n")
subprocess.run(["make", "-C", str(buildroot), "BR2_VERSION_FULL=" + BUILDROOT_VERSION, "olddefconfig"], env=environment(), check=True)
# Reject unexpected changes to the target ISA/libc, rather than silently trying them.
actual = (buildroot / ".config").read_text()
for required in ("BR2_RISCV_32=y", "BR2_RISCV_ABI_ILP32=y", "BR2_TOOLCHAIN_BUILDROOT_GLIBC=y"):
    if required not in actual:
        raise SystemExit("Required rvc toolchain setting missing: " + required)
for forbidden in ("BR2_RISCV_ISA_CUSTOM_RVC=y", "BR2_RISCV_ISA_CUSTOM_RVF=y"):
    if forbidden in actual:
        raise SystemExit("Unsupported target extension: " + forbidden)
