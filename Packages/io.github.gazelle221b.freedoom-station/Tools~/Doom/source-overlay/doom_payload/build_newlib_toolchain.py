#!/usr/bin/env python3
"""Build the optional GNU RV32IMA/ilp32 newlib toolchain on Linux/ext4."""
import argparse
import hashlib
import os
from pathlib import Path
import shutil
import subprocess
import urllib.request

HERE = Path(__file__).resolve().parent
TOOLCHAIN_PIN = "d118e5335a33d4dc77fdc64e5a5223931ab422a0"
NEWLIB_PIN = "8ba4275b83ec27529f67e0d477611fa6d8d6e6bd"
ARCHIVES = (
    ("gcc", "11.2.0", "d53a0a966230895c54f01aea38696f818817b505f1e2bfa65e508753fcd01b2aedb4a61434f41f3a2ddbbd9f41384b96153c684ded3f0fa97c82758d9de5c7cf"),
    ("binutils", "2.37", "5c11aeef6935860a6819ed3a3c93371f052e52b4bdc5033da36037c1544d013b7f12cb8d561ec954fe7469a68f1b66f1a3cd53d5a3af7293635a90d69edd15e7"),
)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--work", type=Path, default=Path("/var/tmp/rvc-doom-gnu-newlib"))
    parser.add_argument("--prefix", type=Path, default=Path("/var/tmp/rvc-doom-newlib"))
    parser.add_argument("--jobs", type=int, default=16)
    args = parser.parse_args()
    work, prefix = args.work.resolve(), args.prefix.resolve()
    if any(" " in str(path) for path in (work, prefix)):
        raise SystemExit("GNU/Buildroot builds require paths without spaces")
    work.mkdir(parents=True, exist_ok=True)
    env = os.environ.copy()
    shim = work / "bin"
    shim.mkdir(exist_ok=True)
    if not (shim / "python").exists():
        (shim / "python").symlink_to(shutil.which("python3"))
    env["PATH"] = str(shim) + ":/usr/local/sbin:/usr/local/bin:/usr/sbin:/usr/bin:/sbin:/bin"
    def run(*command, cwd=None):
        subprocess.run(command, cwd=cwd, env=env, check=True)
    def checkout(url, dest, pin):
        if not (dest / ".git").exists():
            run("git", "init", str(dest))
            run("git", "-C", str(dest), "remote", "add", "origin", url)
        run("git", "-C", str(dest), "fetch", "--depth=1", "origin", pin)
        run("git", "-C", str(dest), "checkout", "--detach", pin)
    framework = work / "toolchain"
    checkout("https://github.com/riscv-collab/riscv-gnu-toolchain.git", framework, TOOLCHAIN_PIN)
    checkout("https://github.com/cygwin/cygwin.git", framework / "newlib", NEWLIB_PIN)
    sources = {}
    for name, version, expected in ARCHIVES:
        filename = f"{name}-{version}.tar.xz"
        archive = work / filename
        if not archive.exists():
            cached = HERE.parent / "buildroot-2022.02.1/dl" / name / filename
            if cached.exists():
                shutil.copyfile(cached, archive)
            else:
                suffix = f"gcc/gcc-{version}/{filename}" if name == "gcc" else f"binutils/{filename}"
                with urllib.request.urlopen("https://ftp.gnu.org/gnu/" + suffix, timeout=120) as response:
                    archive.write_bytes(response.read())
        if hashlib.sha512(archive.read_bytes()).hexdigest() != expected:
            raise SystemExit("GNU archive checksum mismatch: " + filename)
        source = work / f"{name}-{version}"
        if not source.exists():
            run("tar", "-xf", str(archive), "-C", str(work))
        sources[name] = source
    run("./configure", "--prefix=" + str(prefix), "--with-arch=rv32ima", "--with-abi=ilp32",
        "--with-multilib-generator=rv32ima-ilp32--", "--with-isa-spec=2.2",
        "--with-gcc-src=" + str(sources["gcc"]), "--with-binutils-src=" + str(sources["binutils"]),
        "--with-newlib-src=" + str(framework / "newlib"), "--with-languages=c",
        "--disable-gdb", "--enable-newlib", cwd=framework)
    run("make", f"-j{args.jobs}", cwd=framework)
    run(str(prefix / "bin/riscv32-unknown-elf-gcc"), "--print-multi-lib")


if __name__ == "__main__":
    main()
