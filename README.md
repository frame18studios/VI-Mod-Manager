<p align="center">
  <img src="src/Assets/vimm-logo.svg" width="96" height="96" alt="VI Mod Manager logo" />
</p>

<h1 align="center">VI Mod Manager</h1>

<p align="center">
  <strong>Open source · MIT licensed · Independent</strong><br />
  An independent, open-source mod management project designed around a future focus on Grand Theft Auto VI modding workflows.
</p>

<p align="center">
  Today it provides a generic package library and reversible file-deployment foundation for Windows. Game-specific capabilities are planned for later releases and are not present in v0.2.
</p>

<p align="center">
  <img alt="Open source" src="https://img.shields.io/badge/open--source-yes-2ea44f" />
  <img alt=".NET 10" src="https://img.shields.io/badge/.NET-10.0-512BD4" />
  <img alt="Avalonia 11" src="https://img.shields.io/badge/Avalonia-11-8B44AC" />
  <img alt="Windows x64" src="https://img.shields.io/badge/platform-Windows%20x64-0078D4" />
  <img alt="MIT License" src="https://img.shields.io/badge/license-MIT-FF5CA8" />
  <img alt="v0.2" src="https://img.shields.io/badge/version-v0.2-FF5CA8" />
  <img alt="Status: experimental" src="https://img.shields.io/badge/status-experimental-FFB86C" />
</p>

> **Independent project.** VI Mod Manager is not a Rockstar Games or Take-Two Interactive product. It is not affiliated with, endorsed by, sponsored by, authorized by, or connected to Rockstar Games, Take-Two Interactive, or any other game publisher, developer, platform holder, or mod site. References to Grand Theft Auto VI are descriptive of project direction only and do not imply endorsement, partnership, compatibility, or support. "Grand Theft Auto" and "GTA" are trademarks of their respective owners.
>
> **No third-party game content is hosted, bundled, or linked.** This repository and its releases contain no mods, game files, game assets, or links to third-party mod downloads. The only bundled package is an original text-and-JSON demo. You supply your own packages and are responsible for having the rights to use them.
>
> **No DRM or anti-cheat bypass.** The app copies files as data. It does not execute package code, decrypt content, inspect proprietary game containers, or interact with online services.

---

## Download

Grab one of the two standalone Windows x64 executables from the [releases page](https://github.com/frame18studios/VI-Mod-Manager/releases/latest). No installer or archive is required — just run the EXE. The [`publish/` folder](publish/) in this repository holds older reference artifacts, not a published release.

| File | Size | Requirements |
| --- | --- | --- |
| `ViModManager.exe` | ~45 MB | None — self-contained, includes the .NET 10 runtime. |
| `ViModManager-light.exe` | ~25 MB | [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0) must already be installed. |

The `v0.2` source is described in [RELEASE_NOTES.md](RELEASE_NOTES.md); no `v0.2` tag or release has been published yet. The committed `publish/` EXEs are older unsigned artifacts and are not the current build. CI rebuilds both EXEs from source when a `v*` tag is pushed. All published EXEs are unsigned, so Windows SmartScreen may show an "unknown publisher" prompt on first launch. Verify any download against `SHA256SUMS.txt` from the same release.

> **Where to put it:** Run the EXE from any writable folder. Avoid `Program Files` or other protected locations. On first launch the app creates `data/`, `mods/`, and `backups/` beside the EXE, or under `VIMM_DATA_ROOT` (or `VMM_DATA_ROOT`) if that variable is set. Only one instance can use a given data root at a time; a second launch is refused until the first is closed.

---

## Code signing policy

Free code signing provided by SignPath.io, certificate by SignPath Foundation.

- **Committers and reviewers:** `frame18studios`
- **Approvers:** `frame18studios`

This program will not transfer any information to other networked systems unless specifically requested by the user or the person installing or operating it.

The SignPath Foundation application is still pending, so published EXEs are unsigned until it is approved. Once approved, future release EXEs will be signed through SignPath.io, and `SHA256SUMS.txt` will be generated after signing.

---

## Quick start

1. Download one of the two EXEs above.
2. Run it and choose a target folder you control — a test folder is a good place to start.
3. Drag and drop a package `.zip`, an unpacked folder, or a single file onto the window, or use **Import**.
4. Review the package, adjust any options, then click **Deploy**.
5. Use **Unload** to restore backups and remove deployed files. The app verifies SHA-256 hashes first and refuses to overwrite files that changed outside the app.

Try the workflow safely with [`examples/example-package-demo.zip`](examples/example-package-demo.zip), a small text-and-JSON demo created for this project.

---

## Features

- **Import anything local** — `.zip` archives, unpacked folders, or individual loose files, via the file picker or drag-and-drop.
- **Reversible by design** — per-file backups, SHA-256 hashes, and a safe unload path that detects external edits.
- **Typed configuration editor** — generates `bool`, `int`, `float`, `choice`, and `text` controls from `vmm-manifest.json`, plus custom key/value entries.
- **Load order and conflict checks** — reorder enabled packages and inspect destination-path overlaps before deploying.
- **Workspace profiles** — save and restore named snapshots of enabled packages and configuration values.
- **Customization** — 7 curated themes, a circular palette-token color selector, corner styles (`Sharp`, `Balanced`, `Rounded`, `Soft`), and a live text-size control.
- **Path safety** — rejects empty, absolute, drive-prefixed, `..`-traversal, reserved, and duplicate destinations, and skips symlinks and junctions.
- **Uncapped payloads** — large archives and assets stream to disk with no compressed-size, expanded-size, per-entry, entry-count, or ratio caps. Manifest text has a generous inspection bound so a malformed archive cannot exhaust memory.
- **Crash-aware operations** — a local journal resolves interrupted deploys on the next launch; a corrupt journal blocks destructive work until acknowledged, and external edits are never overwritten.
- **No package execution** — payload files are copied as opaque bytes; manifests are read as plain JSON.
- **Local only** — no runtime telemetry, no network calls, no auto-updater, and no account requirement. A local audit log is written under `data/logs`; build-time tooling is described in [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
- **Testable core seam** — library, deployment, configuration, and profile services sit behind a small ViewModel boundary, so core behavior can be tested without the UI.
- **Format-neutral targets in v0.2** — target folders are resolved through a generic local-folder adapter; this release does not detect games, match executable names, or inspect proprietary containers. Game-specific adapters are future work and are not included here.

---

## Safety model

| Control | Behavior |
| --- | --- |
| **Target scope** | Files are written only into the target folder you select, resolved through a generic folder adapter. The app's own settings, library, and backups live in a separate data root. |
| **Path validation** | Every relative path is normalized and must resolve inside the target folder. Absolute paths, drive prefixes, `.`/`..` segments, Windows-reserved names, and symlinks or junctions in existing path components are rejected. Duplicate destinations, including a collision with the generated config file, are rejected case-insensitively. |
| **Archive structure** | ZIPs are checked for structural ambiguity, such as duplicate entry names. Payload bytes stream to disk with no size, entry-count, or compression-ratio caps; only manifest text is bounded during inspection. |
| **Backups** | Before replacing an existing file, the original is copied to `backups/<install-id>/` and hashed. Unload preflights every file and refuses to clear deployment state if a required backup is missing or damaged. |
| **Integrity** | The package SHA-256 is verified before deployment. Deployed files are hashed with SHA-256; on unload, backups are restored only if the deployed file has not been modified externally. |
| **Crash recovery** | A local deployment journal records destructive steps. Interrupted installs are rolled back or completed and interrupted unloads/config writes are finished on the next launch. An unreadable journal is preserved and blocks destructive work until you acknowledge it; recovery refuses to overwrite files changed outside the app. |
| **Concurrency** | One operation at a time owns library and deployment state, and a named lock keyed to the data root allows only one app instance to use that root. Overlapping UI actions and a second process cannot interleave writes. |
| **Auditability** | Local-only events are appended to `data/logs/vimm.log`, rotating to three bounded archives. Nothing is uploaded or sent to any service. |
| **No execution** | The app does not run package code, scripts, installers, or payload content; opening a folder in File Explorer is a separate user-initiated action. |
| **No privilege escalation** | The app runs as the current user and does not request administrator rights. |
| **Content trust** | The app validates paths and hashes, not intent. It does not scan for malware. Only import packages from sources you trust. |

---

## Package format

A package is a `.zip` archive (or unpacked folder) that contains an optional root manifest named **`vmm-manifest.json`**. The legacy `vi-mod.json` name is still recognized; if both exist, `vmm-manifest.json` wins.

```json
{
  "id": "example-package",
  "name": "Example Package",
  "version": "1.0.0",
  "author": "Package Author",
  "description": "Example package with typed configuration values.",
  "settings": [
    { "key": "scale", "label": "Scale", "type": "float", "default": 1.0, "min": 0.1, "max": 5.0, "step": 0.1 },
    { "key": "preset", "type": "choice", "options": ["relaxed", "standard", "focused"] },
    { "key": "debug", "type": "bool", "default": false }
  ],
  "config": { "file": "packages/example-package/settings.json" },
  "files": [
    { "source": "payload/note.txt", "destination": "packages/example-package/note.txt" }
  ]
}
```

Archives without a manifest are supported too: every safe entry deploys to its matching relative path. See [docs/PACKAGE_FORMAT.md](docs/PACKAGE_FORMAT.md) for the full schema, value types, and path-safety rules.

---

## Build from source

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0).

```bash
dotnet restore ViModManager.slnx
dotnet build src/ViModManager.csproj -c Release
dotnet test tests/ViModManager.Tests.csproj -c Release --nologo
dotnet run --project src/ViModManager.csproj
```

CI stages exactly two standalone EXEs and `SHA256SUMS.txt` in a flat `publish/` folder and attaches them to a `v*` tag. The committed `publish/` folder holds older reference artifacts; it is not a current download source. Full instructions are in [BUILDING.md](BUILDING.md).

---

## Documentation

| Document | Purpose |
| --- | --- |
| [docs/PACKAGE_FORMAT.md](docs/PACKAGE_FORMAT.md) | `vmm-manifest.json` specification, value types, and path safety |
| [BUILDING.md](BUILDING.md) | Building from source, running tests, and publishing the two release EXEs |
| [RELEASE_NOTES.md](RELEASE_NOTES.md) | What is included in `v0.2 · experimental` |
| [DISCLAIMER.md](DISCLAIMER.md) | Independence notice, scope boundaries, user responsibility, and notice-and-takedown |
| [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md) | Open-source libraries and license references included in builds |
| [CONTRIBUTING.md](CONTRIBUTING.md) | Contribution guidelines, IP hygiene, and licensing of contributions |
| [SECURITY.md](SECURITY.md) | Private vulnerability reporting and takedown requests |

---

## Legal and responsible use

- **License:** The original source code is licensed under the [MIT License](LICENSE).
- **Third-party content:** The MIT License does not cover mods, assets, trademarks, or game content supplied by users or third parties. See [DISCLAIMER.md](DISCLAIMER.md).
- **No affiliation:** The project is independent of Rockstar Games and Take-Two Interactive and makes no compatibility, endorsement, or support claims about Grand Theft Auto VI or any other title.
- **No hosted content:** This repository and its releases do not host user packages, game assets, or mod downloads, and do not link to third-party mod repositories.
- **No circumvention:** The app does not bypass DRM, license checks, anti-cheat, or access controls.
- **User responsibility:** Only import and deploy content you have the rights to use, and follow the terms that apply to the target software.
- **Notice and takedown:** If you believe material in this repository or its release assets infringes your rights, use the private process in [SECURITY.md](SECURITY.md).

This section is a summary, not legal advice.

---

## License

The original source code is licensed under the [MIT License](LICENSE). Third-party components retain their own licenses — see [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md). The MIT License does not grant rights to any third-party mods, assets, trademarks, or game content.
