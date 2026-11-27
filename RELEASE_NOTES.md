# VI Mod Manager — v0.2 (Experimental)

Release date: 2026-11-27

## Overview

**VI Mod Manager v0.2 (experimental)** is a general-purpose, local-first package library and reversible file-deployment workbench for Windows, built with **.NET 10** and **Avalonia 11**. This version removes artificial package-size limits and adds crash recovery, operation serialization, pre-deploy integrity checks, and a clearer safety-focused UI.

> **Independent tool.** VI Mod Manager is not affiliated with, authorized by, or endorsed by any game publisher or developer. This release contains no mods, game files, or game assets, and it does not bypass DRM, license checks, or anti-cheat systems. It bundles open-source libraries; the portable build also includes the .NET runtime. See [DISCLAIMER.md](https://github.com/frame18studios/VI-Mod-Manager/blob/main/DISCLAIMER.md) and [THIRD-PARTY-NOTICES.md](https://github.com/frame18studios/VI-Mod-Manager/blob/main/THIRD-PARTY-NOTICES.md) in the repository.

## Downloads

Two standalone Windows x64 executables are produced by the release workflow; the source tree is versioned `0.2.0`. No distribution ZIP archive and no installer.

| File | Type | Notes |
| --- | --- | --- |
| `ViModManager.exe` | Self-contained | Includes the .NET 10 runtime (~45 MB). Runs on Windows x64 without prerequisites. |
| `ViModManager-light.exe` | Framework-dependent | Compact (~25 MB). Requires the [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0). |

The committed `publish/` folder still contains the older unsigned artifacts from the previous build; the CI workflow rebuilds both EXEs from this source on a `v*` tag and publishes `SHA256SUMS.txt` with them. Verify a download before running it.

## Code signing policy

Free code signing provided by SignPath.io, certificate by SignPath Foundation.

- **Committers and reviewers:** `frame18studios`
- **Approvers:** `frame18studios`

This program will not transfer any information to other networked systems unless specifically requested by the user or the person installing or operating it.

The SignPath Foundation application is still pending. Until it is approved, VI Mod Manager EXEs are unsigned and Windows SmartScreen may show an unknown-publisher prompt. Once approved, future release EXEs will be signed through SignPath.io, and `SHA256SUMS.txt` will be generated after signing.

## Highlights in v0.2

- **No artificial package limits** — archives, folders, and loose files stream to disk without compressed-size, expanded-size, per-entry, entry-count, manifest-size, or compression-ratio caps. Only structural checks that prevent ambiguous writes remain.
- **Crash-aware deployment** — a local journal records every destructive step. On the next launch, interrupted installs are rolled back or completed, and interrupted unloads/config writes are finished safely.
- **Package integrity before deploy** — the stored SHA-256 is verified before files are written. A package that changed after import is refused with a clear re-import message.
- **Serialized operations** — library changes and deployments are gated so overlapping actions cannot interleave and corrupt state.
- **Stronger backup safety** — original files are hashed before and after copying; unload preflights every deployed file and backup, and tolerates retry after a partial interruption.
- **Improved cache fingerprint** — length, timestamps, and cheap head/tail samples detect same-size rewrites without hashing a multi-gigabyte archive on every UI refresh.
- **Local audit log** — deployment, recovery, and error events are written to `data/logs/vimm.log`. Nothing is transmitted and no telemetry is collected.
- **Redesigned safety surface** — the overview explains the v0.2 guarantees, long operations show live progress, hash verification no longer blocks the UI, and Settings exposes the build version and log location.
- **Failure-injection coverage** — tests now exercise corrupt journals, locked journals, committed installs with tampered files, missing or damaged backups, committed config recovery, simultaneous installs to one destination, wide directories, large manifests, and high entry counts.
- **Safer recovery paths** — a temporarily locked journal is preserved instead of discarded, unload recovery re-verifies backup hashes, and committed config recovery checks the written file before updating state.
- **Layered core seam** — dedicated library, deployment, configuration, and profile services plus a UI-agnostic main ViewModel route every UI core operation through one testable boundary.
- **Format-neutral target adapters** — a single local-folder adapter resolves deployment targets. There is no game detection, executable-name matching, or proprietary-format handling.
- **Uncapped directory preview** — the target folder scan counts every top-level file and folder instead of truncating at arbitrary display limits.

## Known limits

- Experimental `v0.2` release. The app is designed for local file management only: no network downloads, no auto-updater, and no telemetry.
- Published binaries target Windows x64. Runtime data (`data/`, `mods/`, `backups/`) is created beside the EXE on first launch, or under `VIMM_DATA_ROOT` / `VMM_DATA_ROOT` if set.
- The current EXEs are unsigned while the SignPath Foundation application is pending. Verify `SHA256SUMS.txt` and use Windows SmartScreen's "More info" option only if you trust the source.
- The app validates paths and hashes, not intent. It does not scan packages for malware; only import content you have the rights to use.

## License and legal scope

The original source code is released under the [MIT License](https://github.com/frame18studios/VI-Mod-Manager/blob/main/LICENSE). Third-party components retain their own terms; see [THIRD-PARTY-NOTICES.md](https://github.com/frame18studios/VI-Mod-Manager/blob/main/THIRD-PARTY-NOTICES.md). The MIT License does not grant rights to any third-party mods, assets, or game content. See [DISCLAIMER.md](https://github.com/frame18studios/VI-Mod-Manager/blob/main/DISCLAIMER.md) for the project's scope, user responsibilities, and notice-and-takedown process.
