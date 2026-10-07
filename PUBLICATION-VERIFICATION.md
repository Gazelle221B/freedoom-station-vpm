# Public distribution verification — 2026-10-07

The public listing, release ZIP, checksum file and `v0.1.0` source archive were
downloaded without authentication. The ZIP's SHA-256 matches both the listing's
`zipSHA256` and the release checksum file:

`94a5acf8af4e6e33649269b3b83776059d816e18f2c34d26df90b61d396147f2`

All **530 ZIP files** match the package files in the public tagged-source archive
byte-for-byte. Running the package's build script from that downloaded source
archive reproduces the released ZIP byte-for-byte. Author/version/dependencies/
download URL agree between the package manifest and the public listing. The
board's source URL points to the same public tag; all current document hashes
match the manifest and no source-URL placeholder remains in the prefab.

The downloaded distribution ZIP was then extracted as an embedded package into
the isolated Unity test project. The final regression run passed:

- C#/Udon refresh, repeated installation, deliberate user-edit preservation,
  and initial-conflict rejection before other writes.
- **19 documents / 124 pages / 231 glyphs**; missing, fallback, truncation and
  blank-glyph counts all **0**; document bytes/text/SHA-256 and four Interact
  buttons passed.
- **560 GPU framebuffer cells**; invalid-proxy callback exceptions, prefab import
  errors and Nix/fb shader warning/error counts all **0**.

This continues to use the existing local SDK safe-null adapter. Actual VCC UI
installation, full guest payload rebuild/parity, SDK world bundle build and
human VR/desktop client tests remain outside the executed verification scope;
see [release validation](RELEASE-VALIDATION.md).

The source tag is `084ba9e1ba2b0734c5d2ab5141db4b112bbadb9a`. The live listing
is maintained on `main`; use its advertised URL, rather than a historical listing
copy from a source tag. Neither this package repository nor gdm-world had a
configured GitHub Actions workflow or reported commit/PR check during verification.
