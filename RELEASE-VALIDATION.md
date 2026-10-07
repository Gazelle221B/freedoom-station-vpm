# Version 0.1.0 validation

Validated on 2026-10-07 after configuring the public release and tagged-source
URLs and the author-approved contact information. This report distinguishes
packaging checks from the historical guest-engine validation in Tools~/Doom.

## Executed checks

- Pinned RVC source `da936a719b4254e91ba422361d7c1d1d0e775b8f` with the
  reviewed permission patch and packaged overlay: **92 unit tests passed** on
  WSL Ubuntu 24.04. The existing pinned embeddeddoom cache supplied engine headers
  explicitly through `RVC_DOOM_ENGINE_SRC`; no full guest payload was rebuilt.
- Unity **2022.3.22f1**, Worlds SDK **3.10.3**, Windows D3D11,
  RTX 4070 Ti SUPER, TMP **3.0.6**, uGUI **1.0.0**, Built-in renderer:
  C#/Udon compilation and station import passed in an isolated disposable project.
- License board regenerated against the `v0.1.0` source URL: **19 documents,
  124 pages**. Document text/bytes/SHA-256 matched the final manifest;
  missing, fallback, truncation and blank-glyph counts all **0**. Four serialized
  document/page Interact buttons passed. The original font asset/provenance and
  all 18 upstream license/credit documents remain unchanged. The self-authored
  Japanese guide was updated to describe the now-public source endpoint, with
  its original bytes retained under LicenseBoard/History/00-pre-publication.txt.
- Repeated installation preserved existing assets and deliberate local edits.
  An initial-install conflict was rejected before any other asset was written.
- Regression test passed: **560 actual GPU framebuffer cells**, invalid-object
  UdonSharp proxy callbacks, valid proxy handling, prefab import/save. The local
  SDK copy used the existing hash-guarded safe-null adapter described in payload
  setup; this is not a claim that an unpatched SDK passed those callback cases.
- Extraction audit: **209 original meta files** preserved; **164 visible-package
  and template GUIDs** unique; 17 texture import settings/GUID contracts checked;
  no SDK, WAD or generated guest payload included. `publication-changes.json`
  records the approved license-table clarification and source-label edits;
  `generated-release-assets.json` records regenerated serialization.
- The release ZIP was built twice with deterministic ordering/timestamps and
  identical SHA-256. The repository listing carries `zipSHA256`, outside the
  package manifest. Release checksum files are published alongside the ZIP.

## Not rerun / limitations

Full Linux payload/submodule rebuild; complete C/GPU M/Sv32/ISA/E1M1 comparisons;
SDK world bundle build; VR/desktop VRChat client testing; actual VCC UI community
repository install, upgrade and uninstall. The Editor test used an embedded local
package, not the VCC UI. Historical guest-engine validation is not presented as
new verification of this package release. Existing CPU shader warnings remain.

Before uploading a consuming world, build/import your own payload, restore SDK
dependencies through VCC, and perform VR/desktop Build & Test. Package upgrades
preserve editable templates rather than overwriting project-specific edits.
