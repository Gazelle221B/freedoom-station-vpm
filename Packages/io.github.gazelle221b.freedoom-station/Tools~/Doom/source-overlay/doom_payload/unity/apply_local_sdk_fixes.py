#!/usr/bin/env python3
"""Backport the invalid-object serialization guard into the local SDK only.

SDK 3.10.3 is pinned by its source hash. Originals are backed up, updates
fail closed, and an already-applied patch is a no-op. No payload/core files.
Reference: TeamSirenix/odin-serializer commit 8b774c26d30b0119224deab59e6858d7b5b50e51.
"""
import argparse
import hashlib
import json
from pathlib import Path

SDK = Path('Packages/com.vrchat.worlds/Integrations/UdonSharp/Runtime/UdonSharpBehaviour.cs')
SDK_SHA = 'e83dbbad1ca212d2df07286b70afcffe5fda6065881017e50a6c6b9c4391e895'


def sdk_patch(original: bytes) -> bytes:
    newline = b'\r\n' if b'\r\n' in original else b'\n'
    data = original.replace(b'using VRC.Udon.Serialization.OdinSerializer;',
                            b'using VRC.Udon.Serialization.OdinSerializer;' + newline + b'using VRC.Udon.Serialization.OdinSerializer.Utilities;')
    for name in ('OnBeforeSerialize', 'OnAfterDeserialize'):
        old = ('void ISerializationCallbackReceiver.' + name + '()').encode() + newline + b'        {' + newline
        if data.count(old) != 1:
            raise ValueError('Unexpected SDK callback layout: ' + name)
        data = data.replace(old, old + b'            if (this.SafeIsUnityNull()) return;' + newline)
    return data


def replace(project, backup, rel, updated, records):
    target = project / rel
    original = target.read_bytes()
    if original != updated:
        saved = backup / rel
        if saved.exists():
            raise ValueError('Backup already exists: ' + str(saved))
        saved.parent.mkdir(parents=True, exist_ok=True)
        saved.write_bytes(original)
        target.write_bytes(updated)
    if target.read_bytes() != updated:
        raise ValueError('Write verification failed: ' + str(target))
    records.append({'file': rel.as_posix(), 'sha256': hashlib.sha256(updated).hexdigest(),
                    'changed': original != updated})


def apply(project, backup, door_camera=False):
    records = []
    original = (project / SDK).read_bytes()
    if hashlib.sha256(original).hexdigest() == SDK_SHA:
        updated = sdk_patch(original)
    elif original.count(b'if (this.SafeIsUnityNull()) return;') == 2:
        # Reconstruct the original to verify that no other package edits slipped in.
        newline = b'\r\n' if b'\r\n' in original else b'\n'
        restored = original.replace(newline + b'using VRC.Udon.Serialization.OdinSerializer.Utilities;', b'')
        restored = restored.replace(b'            if (this.SafeIsUnityNull()) return;' + newline, b'')
        if hashlib.sha256(restored).hexdigest() != SDK_SHA:
            raise ValueError('Patched SDK differs from the pinned original')
        updated = original
    else:
        raise ValueError('SDK source differs from pinned 3.10.3; review before applying')
    replace(project, backup, SDK, updated, records)
    if door_camera:
        rel = Path('Assets/UdonScripts/DoorCamera.cs')
        original = (project / rel).read_bytes()
        old = b'[SerializeField] private Transform camera;'
        new = b'[SerializeField] private new Transform camera;'
        if original.count(old) == 1:
            updated = original.replace(old, new)
        elif original.count(new) == 1:
            updated = original
        else:
            raise ValueError('Unexpected DoorCamera field layout')
        # Preserve the serialized name and all field references.
        replace(project, backup, rel, updated, records)
    return records


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--project', required=True, type=Path)
    parser.add_argument('--backup', required=True, type=Path)
    parser.add_argument('--manifest', required=True, type=Path)
    parser.add_argument('--door-camera', action='store_true')
    args = parser.parse_args()
    project = args.project.resolve()
    if not (project / 'ProjectSettings/ProjectVersion.txt').is_file():
        raise ValueError('Target is not a Unity project')
    records = apply(project, args.backup.resolve(), args.door_camera)
    args.manifest.parent.mkdir(parents=True, exist_ok=True)
    args.manifest.write_text(json.dumps({'files': records}, indent=2) + '\n', encoding='utf-8')
    print('Verified local fixes:', len(records))


if __name__ == '__main__':
    main()
