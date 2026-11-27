# VI Mod Manager — v0.2 (Experimental)

Status: v0.2 source prepared; no release tag or GitHub Release has been published yet.

## Overview

**VI Mod Manager v0.2 (experimental)** is an open-source, local-first package library and reversible file-deployment workbench for Windows, built with **.NET 10** and **Avalonia 11**. The project is designed around a future focus on Grand Theft Auto VI modding workflows, but this release deliberately ships only the generic deployment foundation; no game-specific features are included. This version removes artificial payload-size limits and adds crash recovery, fail-closed safety checks, single-instance data-root locking, pre-deploy integrity checks, and a clearer safety-focused UI.

> **Independent tool.** VI Mod Manager is not a Rockstar Games or Take-Two Interactive product and is not affiliated with, authorized by, endorsed by, or sponsored by Rockstar Games, Take-Two Interactive, or any other game publisher or developer. References to Grand Theft Auto VI describe project direction only and do not imply compatibility, support, or endorsement. "Grand Theft Auto" and "GTA" are trademarks of their respective owners. This version and its releases contain no mods, game files, game assets, or links to third-party mod downloads, and it does not bypass DRM, license checks, or anti-cheat systems. The original source is MIT licensed; bundled third-party libraries are acknowledged separately in [THIRD-PARTY-NOTICES.md](https://github.com/frame18studios/VI-Mod-Manager/blob/main/THIRD-PARTY-NOTICES.md). See also [DISCLAIMER.md](https://github.com/frame18studios/VI-Mod-Manager/blob/main/DISCLAIMER.md).

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

- **No artificial payload limits** — archives, folders, and loose files stream to disk without compressed-size, expanded-size, per-entry, entry-count, or compression-ratio caps. Manifest text has a generous inspection bound so a malformed archive cannot exhaust memory.
- **Crash-aware deployment** — a local journal records every destructive step. On the next launch, interrupted installs are rolled back or completed, and interrupted unloads/config writes are finished safely. An unreadable journal is preserved and blocks destructive work until acknowledged.
- **Package integrity before deploy** — the stored SHA-256 is verified before files are written. A package that changed after import is refused with a clear re-import message.
- **Serialized operations** — library changes and deployments are gated so overlapping actions cannot interleave, and a named lock keyed to the data root refuses a second app instance using the same files.
- **Stronger backup safety** — original files are hashed before and after copying; unload preflights every deployed file and backup, and tolerates retry after a partial interruption.
- **Improved cache fingerprint** — length, timestamps, and cheap head/tail samples detect same-size rewrites without hashing a multi-gigabyte archive on every UI refresh.
- **Local audit log** — deployment, recovery, and error events are written to `data/logs/vimm.log`, rotating to three bounded archives. Nothing is transmitted and no telemetry is collected.
- **Redesigned safety surface** — the overview explains the v0.2 guarantees, long operations show live progress, hash verification no longer blocks the UI, and Settings exposes the build version and log location.
- **Failure-injection coverage** — tests now exercise corrupt journals, locked journals, committed installs with tampered files, missing or damaged backups, committed config recovery, simultaneous installs to one destination, wide directories, large manifests, high entry counts, staging collisions, symlinked staging paths, failed generated-config updates, interrupted unloads with external edits, shared data roots, and uncapped large payloads.
- **Safer recovery paths** — a corrupt or temporarily locked journal is preserved instead of discarded, unload recovery refuses externally changed files and re-verifies backup hashes, and generated-config updates keep a separate transaction snapshot so a failed write restores the previous generated config without disturbing the original backup.
- **Layered core seam** — dedicated library, deployment, configuration, and profile services plus a UI-agnostic main ViewModel route every UI core operation through one testable boundary.
- **Format-neutral target adapters in v0.2** — a single local-folder adapter resolves deployment targets. This release has no game detection, executable-name matching, proprietary-format handling, mod-link fetching, or game-specific asset editing; those capabilities are future work only.
- **Uncapped directory preview** — the target folder scan counts every top-level file and folder instead of truncating at arbitrary display limits.

## Known limits

- The `v0.2` source is experimental and no public release tag has been published yet. The app is designed for local file management only: no network downloads, no auto-updater, and no telemetry.
- Grand Theft Auto VI is mentioned only as a project direction. v0.2 does not claim or provide compatibility, support, or endorsement for that title, and it ships no game-specific modding features.
- Published binaries target Windows x64. Runtime data (`data/`, `mods/`, `backups/`) is created beside the EXE on first launch, or under `VIMM_DATA_ROOT` / `VMM_DATA_ROOT` if set. Only one instance can use a data root at a time.
- The current EXEs are unsigned while the SignPath Foundation application is pending. Verify `SHA256SUMS.txt` and use Windows SmartScreen's "More info" option only if you trust the source.
- The app validates paths and hashes, not intent. It does not scan packages for malware; only import content you have the rights to use.

## License and legal scope

The original source code is released under the [MIT License](https://github.com/frame18studios/VI-Mod-Manager/blob/main/LICENSE). Third-party components retain their own terms; see [THIRD-PARTY-NOTICES.md](https://github.com/frame18studios/VI-Mod-Manager/blob/main/THIRD-PARTY-NOTICES.md). The MIT License does not grant rights to any third-party mods, assets, or game content. See [DISCLAIMER.md](https://github.com/frame18studios/VI-Mod-Manager/blob/main/DISCLAIMER.md) for the project's scope, user responsibilities, and notice-and-takedown process.
