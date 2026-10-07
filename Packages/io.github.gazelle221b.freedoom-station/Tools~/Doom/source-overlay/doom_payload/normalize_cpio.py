#!/usr/bin/env python3
"""Canonicalize a newc initramfs without changing file data or hard links."""
import argparse
from pathlib import Path
import stat
from reproducible import SOURCE_DATE_EPOCH


def decode(data):
    entries = []
    pos = 0
    while pos + 110 <= len(data):
        if data[pos:pos+6] != b"070701":
            raise ValueError("Expected newc archive")
        fields = [int(data[pos+6+i*8:pos+14+i*8], 16) for i in range(13)]
        pos += 110
        name = data[pos:pos+fields[11]-1]
        if data[pos+fields[11]-1] != 0:
            raise ValueError("Unterminated cpio name")
        pos = (pos + fields[11] + 3) & ~3
        content = data[pos:pos+fields[6]]
        if len(content) != fields[6]:
            raise ValueError("Truncated cpio data")
        pos = (pos + fields[6] + 3) & ~3
        if name == b"TRAILER!!!":
            return entries
        if name.startswith(b"/") or b".." in name.split(b"/"):
            raise ValueError("Unsafe cpio path")
        entries.append((name, fields, content))
    raise ValueError("Missing cpio trailer")


def encode(entries):
    result = bytearray()
    for name, fields, content in entries:
        fields = list(fields)
        fields[6], fields[11] = len(content), len(name)+1
        result.extend(b"070701" + b"".join(f"{x:08x}".encode() for x in fields))
        result.extend(name+b"\0")
        result.extend(b"\0"*((-len(result)) & 3))
        result.extend(content)
        result.extend(b"\0"*((-len(result)) & 3))
    result.extend(b"\0"*((-len(result)) % 512))
    return bytes(result)


def normalize(data, epoch=SOURCE_DATE_EPOCH):
    entries = sorted(decode(data), key=lambda entry: entry[0])
    if len({e[0] for e in entries}) != len(entries):
        raise ValueError("Duplicate cpio path")
    groups = {}
    for i, (name, fields, content) in enumerate(entries):
        key = (fields[7], fields[8], fields[0]) if stat.S_ISREG(fields[1]) and fields[4]>1 else (name,)
        groups.setdefault(key, []).append(i)
    for inode, indices in enumerate(groups.values(), 1):
        bodies = {entries[i][2] for i in indices if entries[i][2]}
        if len(bodies)>1:
            raise ValueError("Conflicting hard link data")
        body = next(iter(bodies), b"")
        for i in indices:
            name, fields, content = entries[i]
            fields[0], fields[2], fields[3], fields[5] = inode, 0, 0, epoch
            fields[7], fields[8], fields[12] = 0, 0, 0
            if stat.S_ISREG(fields[1]):
                fields[4] = len(indices)
                content = body if i == indices[-1] else b""
            entries[i] = name, fields, content
    entries.append((b"TRAILER!!!", [0, 0, 0, 0, 1, epoch, 0, 0, 0, 0, 0, 11, 0], b""))
    return encode(entries)


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("archive", type=Path)
    args = parser.parse_args()
    args.archive.write_bytes(normalize(args.archive.read_bytes()))
