# Building VI Mod Manager

VI Mod Manager targets **.NET 10** with **Avalonia 11**.

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- Windows x64 for the release build (Avalonia desktop application)

## Build and test

From the repository root:

```bash
dotnet restore ViModManager.slnx
dotnet build src/ViModManager.csproj -c Release
dotnet test tests/ViModManager.Tests.csproj -c Release --nologo
```

## Publishing the two release executables

The release ships exactly two standalone EXEs: a self-contained portable build and a smaller framework-dependent build. No ZIP archives are produced.

> **Important:** Keep `<PublishTrimmed>false</PublishTrimmed>` enabled. Avalonia XAML discovery relies on reflection, so trimming can break UI controls. The project also excludes the Git commit hash from assembly metadata so rebuilds with the same SDK are reproducible across commits and tags.

### Portable build (self-contained, ~45 MB EXE)

Includes the .NET 10 runtime so end users need no prerequisites:

```powershell
dotnet publish src/ViModManager.csproj -c Release -r win-x64 `
  --self-contained true -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:EnableCompressionInSingleFile=true -p:PublishTrimmed=false -p:DebugType=None `
  -o "$env:TEMP/vimm-publish/portable"
```

### Light build (framework-dependent, ~25 MB EXE)

Smaller download for systems with the .NET 10 Desktop Runtime installed:

```powershell
dotnet publish src/ViModManager.csproj -c Release -r win-x64 `
  --self-contained false -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:EnableCompressionInSingleFile=false -p:PublishTrimmed=false -p:DebugType=None `
  -o "$env:TEMP/vimm-publish/light"
```

### Stage the flat release folder

`publish/` contains only the two EXEs and the checksum file. These three files are committed to the repository and are also uploaded as GitHub release assets.

```powershell
New-Item -ItemType Directory -Force publish | Out-Null

Copy-Item "$env:TEMP/vimm-publish/portable/ViModManager.exe" `
  "publish/ViModManager.exe" -Force
Copy-Item "$env:TEMP/vimm-publish/light/ViModManager.exe" `
  "publish/ViModManager-light.exe" -Force

Get-ChildItem publish/*.exe | ForEach-Object {
  $hash = (Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
  "$hash  $($_.Name)"
} | Set-Content publish/SHA256SUMS.txt -Encoding ascii
```

## Release workflow

The GitHub Actions workflow in `.github/workflows/windows-exe.yml`:

1. Restores dependencies and runs the test suite, which builds the projects.
2. Publishes both EXEs.
3. Names them `ViModManager.exe` (self-contained) and `ViModManager-light.exe` (framework-dependent).
4. Writes `publish/SHA256SUMS.txt`.
5. Uploads the two EXEs and the checksum file as build artifacts.
6. On a `v*` tag, creates a GitHub release with those files and the contents of `RELEASE_NOTES.md`.

`bin/` and `obj/` are generated build caches and are ignored by Git. The three release files in `publish/` are committed for direct download. When publishing a tagged release, the workflow builds fresh EXEs from source and uploads those assets; it does not push generated binaries back to the branch.

Release assets must contain only the two built EXEs and the checksum file. Do not add game files, mods, third-party assets, or user packages to a release.

## Runtime data isolation

During testing or development, set `VIMM_DATA_ROOT` (or `VMM_DATA_ROOT`) to a temporary folder so `data/`, `mods/`, and `backups/` are not written into your build directory:

```powershell
$env:VIMM_DATA_ROOT = "$env:TEMP\vimm-test"
.\publish\ViModManager.exe
```

## Project layout

```text
ViModManager.slnx
src/
  ViModManager.csproj
  Assets/                 Application icon (vimm-icon.ico) and SVG mark (vimm-logo.svg)
  Core/                   Deployment engine, manifest parser, path safety, package reader, JSON store
  Models/                 Settings, package records, config schema, profiles, history
  Views/                  Main window shell, vector icons, shared UI factory, dialogs
  Views/Pages/            Dashboard, Library, Load Order, Profiles, Target, History, Settings
  Views/ColorWheelControl.cs  Circular palette-token color selector
  App.axaml               Resource tokens and default Nocturne palette
  App.axaml.cs            Application startup
  AppServices.cs          Runtime paths and JSON stores
  Program.cs              Desktop entry point
  Theme.axaml             Control styles and dynamic radius/typography tokens
  ThemeManager.cs         Runtime preset, palette, corner style, and font scale engine
tests/
  ViModManager.Tests.csproj
  CoreTests.cs            Core engine and path-safety tests
  ExamplePackageTests.cs  Bundled demo package import/load/unload tests
  ExamplePackageContentTests.cs  Demo package content checks
examples/                 Harmless text/JSON demo package (example-package-demo.zip)
docs/                     Manifest and package-format documentation
.github/workflows/        Windows EXE build and release workflow
publish/                  Committed release files: ViModManager.exe, ViModManager-light.exe, SHA256SUMS.txt
THIRD-PARTY-NOTICES.md    Dependency versions and upstream license references
```

Review [DISCLAIMER.md](DISCLAIMER.md) and [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md) before publishing. The project's MIT license does not replace dependency licenses or grant rights to third-party content.
