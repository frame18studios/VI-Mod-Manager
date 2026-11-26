# VI Mod Manager — v0.1 (Experimental)

Release date: 2026-11-27

## Overview

**VI Mod Manager v0.1 (experimental)** is a general-purpose, local-first package library and reversible file-deployment workbench for Windows, built with **.NET 10** and **Avalonia 11**.

> **Independent tool.** VI Mod Manager is not affiliated with, authorized by, or endorsed by any game publisher or developer. This release contains no mods, game files, or game assets, and it does not bypass DRM, license checks, or anti-cheat systems. It bundles open-source libraries; the portable build also includes the .NET runtime. See [DISCLAIMER.md](https://github.com/frame18studios/VI-Mod-Manager/blob/main/DISCLAIMER.md) and [THIRD-PARTY-NOTICES.md](https://github.com/frame18studios/VI-Mod-Manager/blob/main/THIRD-PARTY-NOTICES.md) in the repository.

## Downloads

Two standalone Windows x64 executables. No ZIP archives and no installer.

| File | Type | Notes |
| --- | --- | --- |
| `ViModManager.exe` | Self-contained | Includes the .NET 10 runtime (~45 MB). Runs on Windows x64 without prerequisites. |
| `ViModManager-light.exe` | Framework-dependent | Compact (~25 MB). Requires the [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0). |

The same three files are committed in the repository's [`publish/` folder](https://github.com/frame18studios/VI-Mod-Manager/tree/main/publish) for direct download. `SHA256SUMS.txt` is published with the release; verify a download before running it. The executables are currently unsigned, so Windows SmartScreen may show an unknown-publisher prompt on first launch.

## Highlights

- **Modern Nocturne dark UI** — dark plum and hot-pink interface with a neutral modular mark, vector icons, and a clean workspace hierarchy.
- **Customization** — 7 curated theme presets, a circular palette-token color selector, corner styles, and a text-size scale.
- **Drag-and-drop and multi-source import** — import `.zip` archives, unpacked folders, or single loose files.
- **Safe, reversible deployment** — ordered writes, path-traversal validation, conflict detection, per-file backups, and SHA-256 verification before restoring on unload.
- **Typed configuration editor** — generates controls from `vmm-manifest.json` (`bool`, `int`, `float`, `choice`, `text`) plus custom entries, and exports typed JSON on deploy.
- **Package compatibility** — new packages use `vmm-manifest.json`; existing packages using the former `vi-mod.json` name remain supported.
- **Profiles and activity log** — instant workspace snapshots and a searchable, clearable local activity log.

## Known limits

- Experimental `v0.1` release. The app is designed for local file management only: no network downloads, no auto-updater, and no telemetry.
- Published binaries target Windows x64. Runtime data (`data/`, `mods/`, `backups/`) is created beside the EXE on first launch, or under `VIMM_DATA_ROOT` / `VMM_DATA_ROOT` if set.
- The executables are unsigned. Verify `SHA256SUMS.txt` and use Windows SmartScreen's "More info" option only if you trust the source.

## License and legal scope

The original source code is released under the [MIT License](https://github.com/frame18studios/VI-Mod-Manager/blob/main/LICENSE). Third-party components retain their own terms; see [THIRD-PARTY-NOTICES.md](https://github.com/frame18studios/VI-Mod-Manager/blob/main/THIRD-PARTY-NOTICES.md). The MIT License does not grant rights to any third-party mods, assets, or game content. See [DISCLAIMER.md](https://github.com/frame18studios/VI-Mod-Manager/blob/main/DISCLAIMER.md) for the project's scope, user responsibilities, and notice-and-takedown process.
