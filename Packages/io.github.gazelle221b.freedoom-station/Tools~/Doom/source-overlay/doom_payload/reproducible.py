"""Pinned build identity shared by the source release and payload builders."""
from datetime import datetime, timezone
import os
from pathlib import Path

SOURCE_DATE_EPOCH = 1791072000  # 2026-10-04 00:00:00 UTC
BUILDROOT_VERSION = "2022.02.1-rvc-repro-v1"


def environment():
    env = os.environ.copy()
    env.update(SOURCE_DATE_EPOCH=str(SOURCE_DATE_EPOCH), TZ="UTC", LC_ALL="C",
               KBUILD_BUILD_TIMESTAMP=datetime.fromtimestamp(
                   SOURCE_DATE_EPOCH, timezone.utc).strftime("%a %b %d %H:%M:%S UTC %Y"),
               KBUILD_BUILD_USER="rvc", KBUILD_BUILD_HOST="reproducible",
               KBUILD_BUILD_VERSION="1")
    return env


def prefix_flags(root):
    # Covers generated sources, the offline engine, and the private work path.
    root = Path(root).resolve()
    return f"-ffile-prefix-map={root.parent}=/usr/src/release -ffile-prefix-map={root}=/usr/src/rvc"
