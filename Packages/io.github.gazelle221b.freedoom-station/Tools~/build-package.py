#!/usr/bin/env python3
"""Build a deterministic local VPM ZIP; this command never uploads anything."""
import argparse
import hashlib
import json
from pathlib import Path
import zipfile


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--output', required=True, type=Path)
    parser.add_argument('--release', action='store_true', help='Require confirmed publication fields and source URL')
    args = parser.parse_args()
    package = Path(__file__).resolve().parent.parent
    manifest = json.loads((package / 'package.json').read_text(encoding='utf-8'))
    if args.release:
        if not manifest.get('url', '').startswith('https://') or not manifest['author'].get('email'):
            raise SystemExit('Confirm distribution URL and author contact before release')
        for file in (package / 'Templates~/Doom/LicenseBoard/documents.json',
                     package / 'Tools~/Doom/source-overlay/doom_payload/license_board/documents.json'):
            data = json.loads(file.read_text(encoding='utf-8'))
            if data['sourceUrl'] == 'SOURCE_URL_PLACEHOLDER':
                raise SystemExit('Corresponding-source URL and regenerated board are required before release')
        if b'SOURCE_URL_PLACEHOLDER' in (package / 'Templates~/Doom/DoomStation.prefab').read_bytes():
            raise SystemExit('Regenerate and validate the station license board before release')
    args.output.parent.mkdir(parents=True, exist_ok=True)
    with zipfile.ZipFile(args.output, 'w', zipfile.ZIP_DEFLATED, compresslevel=9) as archive:
        for path in sorted(package.rglob('*')):
            if not path.is_file():
                continue
            relative = path.relative_to(package)
            if any(part in ('__pycache__', 'cache', 'build', '.git') for part in relative.parts) or path.suffix == '.pyc':
                continue
            if path.suffix.lower() == '.wad':
                raise SystemExit('WAD cannot be distributed in this package: ' + str(relative))
            info = zipfile.ZipInfo(relative.as_posix(), (2026, 10, 7, 0, 0, 0))
            info.compress_type = zipfile.ZIP_DEFLATED
            info.external_attr = 0o100644 << 16
            archive.writestr(info, path.read_bytes())
    digest = hashlib.sha256(args.output.read_bytes()).hexdigest()
    args.output.with_suffix(args.output.suffix + '.sha256').write_text(digest + '  ' + args.output.name + '\n', encoding='utf-8')
    print(args.output, digest)


if __name__ == '__main__':
    main()
