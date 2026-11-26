<p align="center">
  <img src="src/Assets/vimm-logo.svg" width="96" height="96" alt="VI Mod Manager logo" />
</p>

<h1 align="center">VI Mod Manager</h1>

<p align="center">
  <strong>A local-first, reversible file-deployment workbench for Windows.</strong><br />
  Import a package, choose a folder, deploy safely — and undo it later.
</p>

<p align="center">
  <img alt=".NET 10" src="https://img.shields.io/badge/.NET-10.0-512BD4" />
  <img alt="Avalonia 11" src="https://img.shields.io/badge/Avalonia-11-8B44AC" />
  <img alt="Windows x64" src="https://img.shields.io/badge/platform-Windows%20x64-0078D4" />
  <img alt="MIT License" src="https://img.shields.io/badge/license-MIT-FF5CA8" />
  <img alt="Status: experimental" src="https://img.shields.io/badge/status-experimental-FFB86C" />
</p>

> **Independent project.** VI Mod Manager is a general-purpose file manager. It is not affiliated with, endorsed by, sponsored by, authorized by, or connected to any game publisher, developer, platform, or other company.
>
> **No third-party game content is hosted or bundled.** This repository and its releases contain no mods, game files, or game assets. The only bundled package is an original text-and-JSON demo. You supply your own packages and are responsible for having the rights to use them.
>
> **No DRM or anti-cheat bypass.** The app copies files as data. It does not execute package code, decrypt content, inspect proprietary containers, or interact with online services.

---

## Download

Grab one of the two standalone Windows x64 executables from the [latest release](https://github.com/frame18studios/VI-Mod-Manager/releases/latest). No installer or archive is required — just run the EXE. They are also available directly in the [`publish/` folder](publish/) of this repository.

| File | Size | Requirements |
| --- | --- | --- |
| `ViModManager.exe` | ~45 MB | None — self-contained, includes the .NET 10 runtime. |
| `ViModManager-light.exe` | ~25 MB | [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0) must already be installed. |

The current `v0.1` files are unsigned, so Windows SmartScreen may show an "unknown publisher" prompt on first launch. Verify the download against `SHA256SUMS.txt` in the same release.

> **Where to put it:** Run the EXE from any writable folder. Avoid `Program Files` or other protected locations. On first launch the app creates `data/`, `mods/`, and `backups/` beside the EXE, or under `VIMM_DATA_ROOT` (or `VMM_DATA_ROOT`) if that variable is set.

---

## Code signing policy

Free code signing provided by SignPath.io, certificate by SignPath Foundation.

- **Committers and reviewers:** `frame18studios`
- **Approvers:** `frame18studios`

This program will not transfer any information to other networked systems unless specifically requested by the user or the person installing or operating it.

The SignPath Foundation application is pending; `v0.1` is unsigned. Once approved, future release EXEs will be signed through SignPath.io, and `SHA256SUMS.txt` will be generated after signing.

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
- **Path safety** — rejects empty, absolute, drive-prefixed, and `..`-traversal paths, and skips symlinks and junctions.
- **No package execution** — payload files are copied as opaque bytes; manifests are read as plain JSON.
- **Local only** — no runtime telemetry, no network calls, no auto-updater, and no account requirement. Build-time tooling is described in [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

---

## Safety model

| Control | Behavior |
| --- | --- |
| **Target scope** | Files are written only into the target folder you select. The app's own settings, library, and backups live in a separate data root. |
| **Path validation** | Every relative path is normalized and must resolve inside the target folder. Absolute paths, drive prefixes, `.`/`..` segments, and reparse points are rejected or skipped. |
| **Backups** | Before replacing an existing file, the original is copied to `backups/<install-id>/`. |
| **Integrity** | Deployed files are hashed with SHA-256. On unload, backups are restored only if the deployed file has not been modified externally. |
| **No execution** | The app does not run package code, scripts, installers, or other applications. |
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

Release builds place exactly two standalone EXEs and `SHA256SUMS.txt` in the flat `publish/` folder. Those files are committed here so you can also download them directly from the repository. Full instructions are in [BUILDING.md](BUILDING.md).

---

## Documentation

| Document | Purpose |
| --- | --- |
| [docs/PACKAGE_FORMAT.md](docs/PACKAGE_FORMAT.md) | `vmm-manifest.json` specification, value types, and path safety |
| [BUILDING.md](BUILDING.md) | Building from source, running tests, and publishing the two release EXEs |
| [RELEASE_NOTES.md](RELEASE_NOTES.md) | What is included in `v0.1 · experimental` |
| [DISCLAIMER.md](DISCLAIMER.md) | Independence notice, scope boundaries, user responsibility, and notice-and-takedown |
| [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md) | Open-source libraries and license references included in builds |
| [CONTRIBUTING.md](CONTRIBUTING.md) | Contribution guidelines, IP hygiene, and licensing of contributions |
| [SECURITY.md](SECURITY.md) | Private vulnerability reporting and takedown requests |

---

## Legal and responsible use

- **License:** The original source code is licensed under the [MIT License](LICENSE).
- **Third-party content:** The MIT License does not cover mods, assets, trademarks, or game content supplied by users or third parties. See [DISCLAIMER.md](DISCLAIMER.md).
- **No affiliation:** The project is independent and makes no compatibility or endorsement claims.
- **No hosted content:** This repository and its releases do not host user packages.
- **No circumvention:** The app does not bypass DRM, license checks, anti-cheat, or access controls.
- **User responsibility:** Only import and deploy content you have the rights to use, and follow the terms that apply to the target software.
- **Notice and takedown:** If you believe material in this repository or its release assets infringes your rights, use the private process in [SECURITY.md](SECURITY.md).

This section is a summary, not legal advice.

---

## License

The original source code is licensed under the [MIT License](LICENSE). Third-party components retain their own licenses — see [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md). The MIT License does not grant rights to any third-party mods, assets, trademarks, or game content.
