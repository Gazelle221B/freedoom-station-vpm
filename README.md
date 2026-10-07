# Freedoom Station VPM package

Package extracted from gdm-world PR #4 at
`ef17f8d6fd69be46f393918768b35b513a8192c1` in response to fog-zs's request to
maintain the additions separately. Version 0.1.1 is distributed as a source/tools
package; generated guest payloads and VRChat world bundles are excluded.

`Packages/io.github.gazelle221b.freedoom-station` is the VCC User Package folder.
The package contains the Doom terminal scripts, RVC shader/core and Dial
dependency, original license viewer, source pins, patches and payload tests.
It requires Worlds SDK **3.10.3**, Unity **2022.3.22f1**, TMP **3.0.6**, uGUI
**1.0.0**, Built-in pipeline and Windows D3D11. VPM supplies the SDK; UPM supplies
TMP/uGUI. The SDK itself is not redistributed.
This follows the [VPM package format](https://vcc.docs.vrchat.com/vpm/packages/),
including `vpmDependencies` and local User Package development.

## VCC installation

Add this community repository URL to VCC Settings > Packages > Add Repository:

`https://raw.githubusercontent.com/Gazelle221B/freedoom-station-vpm/main/index.json`

Then select **Freedoom Station 0.1.1** in your Worlds project's package manager.
[Release ZIP](https://github.com/Gazelle221B/freedoom-station-vpm/releases/download/v0.1.1/freedoom-station-0.1.1.zip) / [tagged source](https://github.com/Gazelle221B/freedoom-station-vpm/tree/v0.1.1).
For local development instead, use the User Package procedure below.

## Station setup / local development

1. Use VCC to restore the SDK and add the package folder in Settings > Packages
   > User Packages, then add Freedoom Station to a disposable Worlds project.
   Import TMP Essential Resources if absent. Use an embedded VPM installation;
   UdonSharp recompilation writes its program assets, so a read-only registry
   cache installation is not supported.
2. Choose **Tools > Doom > Install editable station assets**. The installer
   preflights file bytes and existing GUID owners before creating `Assets/Doom`.
   It refuses differing assets on first installation. On repeat runs, a local
   record under `UserSettings/Doom` lets it preserve installed files and user
   edits without rewriting them; GUID changes still cause an error. Package
   upgrades update shared code but do not overwrite editable scene templates.
   Back up and review a previous loose installation
   separately; automatic deletion/migration is deliberately not configured.
3. Follow [payload setup](Packages/io.github.gazelle221b.freedoom-station/Tools~/Doom/README.md).
   Build inputs and outputs stay local. Pass the Unity project explicitly to
   `import-textures.py --project <project>` and `run-doom.ps1 -Project <project>`.
4. Open `Assets/Doom/DoomWorld.unity` or add the station through its Tools menu.
   Template assets are editable per project; package scripts/programs keep the
   original GUIDs. Add `Assets/Doom/`, `Assets/Doom.meta`, generated probes,
   captures and ClientSim state to the consuming project's ignore rules.

The package does not install LightVolumes/LTCGI or change an existing scene,
project setting or package manifest. The original PR's unrelated NewWorld and
lighting additions are retained under `Samples~/OriginalWorldAdditions` as an
archive, not advertised as a working installable sample. Their original tools
and notices remain under `Tools~`; do not run them as part of station setup.
Payload overlay `unity/` scripts and legacy loose-project installers are source
history/build tooling, not an alternative VPM installation. Mixing them with
the package causes duplicate scripts/GUIDs. `RVC_PACKAGE_DEVELOPMENT=1` is the
explicit opt-in for editing/regenerating package shader sources with PerlPP.

The unused upstream Dial prefab (including its stale legacy label-font
references) and ten unrelated serialized programs are retained under `Samples~`.
They are excluded from the active package; the terminal uses the 14 program
assets in its own Doom/RVC/Dial dependency closure.

## Validation and licenses

Author contact: `contact@tik-choco.com`. Version 0.1.1 updates contact metadata
and the tagged source link; runtime, payload tooling and upstream notices are
unchanged. See [contact update verification](CONTACT-UPDATE.md). Version 0.1.0
validation reports remain historical records.

See [release validation](RELEASE-VALIDATION.md) for the executed checks and their
limits. Full Linux payload rebuild, C/GPU parity, VRChat bundle/client checks and
VCC UI installation/upgrade/uninstall were not rerun for this package release.
Historical validation in Tools~/Doom/VALIDATION.md predates this package layout.

Component licenses are preserved; see [Third Party Notices](Packages/io.github.gazelle221b.freedoom-station/Third%20Party%20Notices.md).
Dial is CC BY-NC-SA 2.0, including its noncommercial condition. GPL guest engines
are obtained separately from pinned upstream sources. This package does not
relicense the VRChat SDK or distribute game data. The board links to
[https://github.com/Gazelle221B/freedoom-station-vpm/tree/v0.1.1](https://github.com/Gazelle221B/freedoom-station-vpm/tree/v0.1.1); rebuild it after changing local payload/source inputs.

`source-map.json` records the extraction's original file paths and SHA-256 hashes;
`publication-changes.json` records approved license clarification and label edits.
`validation/` is local evidence excluded from source control and release ZIPs.
