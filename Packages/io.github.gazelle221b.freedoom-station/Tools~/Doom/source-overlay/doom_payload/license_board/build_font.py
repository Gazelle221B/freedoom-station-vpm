#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
build_font.py - Derive the single static board font "Rvc License Sans" from two
pinned OFL-1.1 sources, reproducibly.

Sources (google/fonts, pin-qualified raw URLs, SHA-256 verified):
  1. Noto Sans JP [wght].ttf  - Japanese coverage (doc set + Japanese guide).
     commit 295d98a7a0c17c68f1341eaeea354e7960ea70d3 (2022-11-03)
  2. Noto Sans [wdth,wght].ttf - adds the exact U+0160 (S WITH CARON) glyph that
     Noto Sans JP lacks (and its glyph dependencies), from the official Latin font.
     commit 2984c575fdce412ee02b2baaba67672b9a9434d8 (2024-11-20)

Both are variable fonts; this script instantiates them to static wght-400 (and
wdth-100 for Noto Sans), then copies the U+0160 glyph (composite dependencies
included, prefixed-renamed) from Noto Sans into the Noto Sans JP static font via
fontTools glyf deepcopy, and renames the family to "Rvc License Sans".
One TMP_FontAsset with one static SDF atlas, no runtime fallback.

License: result is OFL-1.1 (both sources are OFL-1.1); both original OFL texts
and copyright notices are preserved in --out (OFL-NotoSansJP.txt,
OFL-NotoSans.txt) and the name table.

Usage:
    python3 build_font.py --out <output_dir> [--cache <cache_dir>] [--charset <utf8_file>]

Requires: fonttools. Outputs: RvcLicenseSans-Regular.ttf (+ FONTNOTES).
"""
import argparse
import hashlib
import json
import os
import re
import sys
import tempfile
import urllib.request

try:
    from fontTools.ttLib import TTFont
    from fontTools.varLib import instancer
except ImportError as exc:  # pragma: no cover
    sys.exit("fontTools is required: " + str(exc))

PIN = {
    "notosansjp": {
        "role": "Japanese base (all doc chars except U+0160)",
        "url": "https://raw.githubusercontent.com/google/fonts/295d98a7a0c17c68f1341eaeea354e7960ea70d3/ofl/notosansjp/NotoSansJP%5Bwght%5D.ttf",
        "sha256": "c2f3b4d463500a2ddcd3849cded1fceeb9fd6d1c32e6cbecd568453ba50fc68f",
        "commit": "295d98a7a0c17c68f1341eaeea354e7960ea70d3",
        "commit_date": "2022-11-03",
        "file": "NotoSansJP-wght-loop.ttf",
        "axes": {"wght": 400},
    },
    "notosans": {
        "role": "Latin source for the exact U+0160 glyph (and dependencies)",
        "url": "https://raw.githubusercontent.com/google/fonts/2984c575fdce412ee02b2baaba67672b9a9434d8/ofl/notosans/NotoSans%5Bwdth%2Cwght%5D.ttf",
        "sha256": "bfb7bb691513f12e734dc346c03a03f784912432d7e3fa8e56efcf906fe86b3d",
        "commit": "2984c575fdce412ee02b2baaba67672b9a9434d8",
        "commit_date": "2024-11-20",
        "file": "NotoSans-wdth-wght.ttf",
        "axes": {"wdth": 100, "wght": 400},
    },
}

OFL_FILES = {
    "OFL-NotoSansJP.txt": (
        "https://raw.githubusercontent.com/google/fonts/295d98a7a0c17c68f1341eaeea354e7960ea70d3/ofl/notosansjp/OFL.txt",
        "1c05c68c34f9708415aada51f17e1b0092d2cea709bf4a94cd38114f9e73d7d9",
    ),
    "OFL-NotoSans.txt": (
        "https://raw.githubusercontent.com/google/fonts/c92b209a838649c205a1565b5f28c1973e049659/ofl/notosans/OFL.txt",
        "cee9892f9f0cc8fe882c9e9537ee6a89621d86ee7ceaf70b02e2b2b1c25c061a",
    ),
}

PREFIX = "rvc"
DERIVED_NAME = "Rvc License Sans"
DERIVED_PS = "RvcLicenseSans-Regular"
DERIVED_FILE = "RvcLicenseSans-Regular.ttf"
VERSION = "Version 1.000"


def sha256_file(path):
    h = hashlib.sha256()
    with open(path, "rb") as fh:
        for chunk in iter(lambda: fh.read(1 << 20), b""):
            h.update(chunk)
    return h.hexdigest()


def download(url, dest, expected_sha):
    print(" * download %s" % url)
    urllib.request.urlretrieve(url, dest)
    actual = sha256_file(dest)
    if actual != expected_sha:
        raise RuntimeError("SHA-256 mismatch for %s: got %s expected %s" % (dest, actual, expected_sha))
    print("   sha256 OK %s (%d bytes)" % (actual, os.path.getsize(dest)))


def drop_tables(font, names):
    for t in names:
        if t in font:
            del font[t]


def instantiate(path, axes, out_path):
    font = TTFont(path, recalcTimestamp=False)
    # Apply variation deltas before dropping their tables; otherwise wght 400
    # merely relabels the default JP wght-100 outlines as Regular.
    drop_tables(font, ["DSIG"])
    static = instancer.instantiateVariableFont(font, axes, inplace=False)
    drop_tables(static, ["fvar", "gvar", "avar", "STAT", "HVAR", "MVAR", "cvar", "DSIG"])
    static.save(out_path)
    return out_path


def collect_deps(glyf, glyph_name, deps):
    glyph = glyf[glyph_name]
    if hasattr(glyph, "components") and glyph.components:
        for comp in glyph.components:
            dep = comp.glyphName
            if dep in deps:
                continue
            deps.add(dep)
            collect_deps(glyf, dep, deps)


def copy_glyph_with_deps(la, jp, unicode_value):
    """Copy la's glyph for unicode_value (and composite deps) into jp under prefixed names.

    Returns the new glyph name for unicode_value.
    """
    la_cmap = la.getBestCmap()
    if unicode_value not in la_cmap:
        raise RuntimeError("U+%04X not present in source Latin font" % unicode_value)
    main_name = la_cmap[unicode_value]
    deps = set()
    collect_deps(la["glyf"], main_name, deps)
    deps.add(main_name)

    jp_glyf = jp["glyf"]
    jp_hmtx = jp["hmtx"]
    jp_vmtx = jp.get("vmtx")
    order = list(jp.getGlyphOrder())
    order_set = set(order)

    rename = {}
    for original in sorted(deps):
        new_name = PREFIX + original
        rename[original] = new_name
        if new_name in order_set:
            continue
        jp_glyf[new_name] = la["glyf"][original].__deepcopy__(None) if hasattr(la["glyf"][original], "__deepcopy__") else la["glyf"][original].__class__.__new__(la["glyf"][original].__class__)
        import copy as _copy
        jp_glyf[new_name] = _copy.deepcopy(la["glyf"][original])
        advance = la["hmtx"].metrics.get(original) or la["hmtx"].metrics[la.getGlyphOrder()[0]]
        jp_hmtx[new_name] = advance
        if jp_vmtx is not None:
            jp_vmtx[new_name] = (0, 0)
        order.append(new_name)
        order_set.add(new_name)

    # Rewrite composite component references to the prefixed names.
    for original in deps:
        if hasattr(jp_glyf[rename[original]], "components") and jp_glyf[rename[original]].components:
            for comp in jp_glyf[rename[original]].components:
                comp.glyphName = rename.get(comp.glyphName, comp.glyphName)

    jp.setGlyphOrder(order)
    jp["maxp"].numGlyphs = len(order)

    # Add the cmap mapping to every unicodemap table (format 4 and 12).
    main_new = rename[main_name]
    for table in jp["cmap"].tables:
        if getattr(table, "cmap", None) is not None and main_new not in table.cmap.get(unicode_value, ""):
            table.cmap[unicode_value] = main_new
    return main_new


def rename_family(font, copyright_text):
    name = font["name"]
    ids = {0, 1, 2, 3, 4, 5, 6, 13, 14, 16, 17, 25}
    for rec in list(name.names):
        if rec.nameID in ids:
            name.removeNames(nameID=rec.nameID, platformID=rec.platformID, platEncID=rec.platEncID, langID=rec.langID)

    def setn(nid, value):
        name.setName(value, nid, 3, 1, 0x409)
        name.setName(value, nid, 1, 0, 0)

    setn(0, copyright_text)
    setn(1, DERIVED_NAME)
    setn(2, "Regular")
    setn(3, "1.000;RVC;" + DERIVED_PS)
    setn(4, DERIVED_NAME)
    setn(5, VERSION)
    setn(6, DERIVED_PS)
    setn(13, "This Font Software is licensed under the SIL Open Font License, Version 1.1. "
             "This Font Software is distributed on an \"AS IS\" BASIS, WITHOUT WARRANTIES OR "
             "CONDITIONS OF ANY KIND, either express or implied. See the SIL Open Font License "
             "for the specific language, permissions and limitations governing your use of this "
             "Font Software. This font is a derived work of Noto Sans JP (OFL-1.1, copyright "
             "Adobe; Reserved Font Name 'Source') and Noto Sans (OFL-1.1, The Noto Project "
             "Authors); the original OFL texts are shipped alongside this font.")
    setn(14, "https://scripts.sil.org/OFL")
    setn(16, DERIVED_NAME)
    setn(17, "Regular")
    setn(25, "RvcLicenseSans")
    # Fixed 2026-01-01 timestamp in the TrueType (1904) epoch.
    font["head"].created = font["head"].modified = 2082844800 + 1767225600


def repo_doc_union(script_dir, charset_file=None):
    if charset_file:
        with open(charset_file, "r", encoding="utf-8-sig") as fh:
            return set(ord(c) for c in fh.read() if ord(c) > 0x20 and ord(c) != 0x7F)
    root = os.path.abspath(os.path.join(script_dir, "..", ".."))
    docs_dir = os.path.join(root, "doom_payload", "license_board", "Documents")
    json_path = os.path.join(root, "doom_payload", "license_board", "documents.json")
    game_path = os.path.join(root, "doom_payload", "licenses", "GAME-STRINGS.md")

    def read_bytes(p):
        with open(p, "rb") as fh:
            return fh.read()

    def decode(b):
        for enc in ("utf-8-sig", "utf-8"):
            try:
                return b.decode(enc)
            except UnicodeDecodeError:
                continue
        return b.decode("latin-1", "replace")

    def cps(s):
        return set(ord(c) for c in s if not (ord(c) in (0xFEFF, 0x0D) or ord(c) < 0x20 or ord(c) == 0x7F))

    union = set()
    for fn in sorted(os.listdir(docs_dir)):
        if fn.endswith(".txt"):
            union |= cps(decode(read_bytes(os.path.join(docs_dir, fn))))
    with open(json_path, "r", encoding="utf-8") as fh:
        meta = json.load(fh)
    for d in meta.get("documents", []):
        union |= cps(d.get("title") or "")
    lines = decode(read_bytes(game_path)).splitlines()
    s = e = None
    for i, l in enumerate(lines):
        if s is None and l.startswith("### 10.5 Board text"):
            s = i
        elif s is not None and l.startswith("## 11."):
            e = i
            break
    union |= cps("\n".join(lines[s:e]))
    return union


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--out", required=True)
    ap.add_argument("--cache", default=None)
    ap.add_argument("--charset", default=None)
    args = ap.parse_args()

    out = os.path.abspath(args.out)
    cache = os.path.abspath(args.cache) if args.cache else os.path.join(tempfile.gettempdir(), "rvc-font-build")
    os.makedirs(out, exist_ok=True)
    os.makedirs(cache, exist_ok=True)

    sources = {}
    for key, info in PIN.items():
        path = os.path.join(cache, info["file"])
        download(info["url"], path, info["sha256"])
        sources[key] = path

    for oln, (url, sha) in OFL_FILES.items():
        dst = os.path.join(out, oln)
        download(url, dst, sha)

    jp_static = os.path.join(cache, "jp-static-w400.ttf")
    la_static = os.path.join(cache, "la-static-w400.ttf")
    instantiate(sources["notosansjp"], PIN["notosansjp"]["axes"], jp_static)
    instantiate(sources["notosans"], PIN["notosans"]["axes"], la_static)
    jp = TTFont(jp_static, recalcTimestamp=False)
    la = TTFont(la_static, recalcTimestamp=False)

    # Sanity: base JP font must cover everything except the glyph we are about to add.
    union = repo_doc_union(os.path.dirname(os.path.abspath(__file__)), args.charset)
    jp_cmap = set()
    for t in jp["cmap"].tables:
        jp_cmap.update(t.cmap)
    missing_before = sorted(union - jp_cmap)
    if missing_before != [0x160]:
        raise RuntimeError("Unexpected pre-merge gaps: " + " ".join("U+%04X" % c for c in missing_before))

    add = copy_glyph_with_deps(la, jp, 0x160)

    jp_name = jp["name"]
    copyright_jp = jp_name.getDebugName(0) or ""
    copyright_la = la["name"].getDebugName(0) or ""
    rename_family(jp, "%s %s" % (copyright_jp.rstrip("."), copyright_la))

    derived = os.path.join(out, DERIVED_FILE)
    jp.save(derived)
    derived_sha = sha256_file(derived)

    # Verify the derived font.
    d = TTFont(derived)
    d_cmap = set()
    for t in d["cmap"].tables:
        d_cmap.update(t.cmap)
    missing = sorted(union - d_cmap)
    best = d.getBestCmap(); added_name = best.get(0x160); added_outline = d["glyf"][added_name] if added_name else None
    note_lines = [
        "# Rvc License Sans - font provenance and generation notes",
        "",
        "Family: " + DERIVED_NAME,
        "PostScript: " + DERIVED_PS,
        "Version: " + VERSION,
        "File: " + DERIVED_FILE,
        "SHA-256: " + derived_sha,
        "Bytes: %d" % os.path.getsize(derived),
        "Format: static TrueType (glyf), no variable axes",
        "Coverage: %d/%d codepoints of the board document set; missing: %s" % (
            len(union), len(union), "none" if not missing else " ".join("U+%04X" % c for c in missing)),
        "U+0160 glyph: %s (copied from Noto Sans with dependencies, prefix '%s')" % (add, PREFIX),
        "",
        "Sources (OFL-1.1, pin-qualified):",
    ]
    for key, info in PIN.items():
        note_lines.append("- %s: %s (commit %s, %s)" % (key, info["role"], info["commit"], info["commit_date"]))
        note_lines.append("  URL: %s" % info["url"])
        note_lines.append("  SHA-256: %s" % info["sha256"])
        note_lines.append("  axes pinned for static derivation: %s" % info["axes"])
    note_lines += [
        "OFL texts shipped in this directory: OFL-NotoSansJP.txt, OFL-NotoSans.txt (both SIL OFL 1.1).",
        "Generation: python3 doom_payload/license_board/build_font.py --out " + out,
        "Method: fontTools instancer (wght 400) on both sources, then glyf deepcopy of the U+0160",
        "glyph (and its composite dependencies, prefixed '%s') from Noto Sans into Noto Sans JP," % PREFIX,
        "then family rename to 'Rvc License Sans' (no RFN conflict: no 'Noto'/'Source' name is kept).",
        "fontTools version used: " + sys.modules["fontTools"].version,
    ]
    with open(os.path.join(out, "RvcLicenseSans-FONTNOTES.md"), "w", encoding="utf-8") as fh:
        fh.write("\n".join(note_lines) + "\n")

    print("DERIVED %s" % derived)
    print("DERIVED_SHA256 %s" % derived_sha)
    print("DERIVED_BYTES %d" % os.path.getsize(derived))
    print("U+0160 glyph name (post round-trip): %s outline-nonempty: %s contours=%s" % (added_name, added_outline is not None and added_outline.numberOfContours != 0, added_outline.numberOfContours if added_outline is not None else -1))
    print("UNION %d MISSING %d %s" % (len(union), len(missing), " ".join("U+%04X" % c for c in missing)))
    print("FAMILY %s | PS %s | VERSION %s" % (d["name"].getDebugName(1), d["name"].getDebugName(6), d["name"].getDebugName(5)))
    print("COPYRIGHT %s" % d["name"].getDebugName(0))
    print("LICENSE_URL %s" % d["name"].getDebugName(14))
    print("FVAR %s" % ("fvar" in d))
    print("CMAP %d" % len(d_cmap))


if __name__ == "__main__":
    main()
