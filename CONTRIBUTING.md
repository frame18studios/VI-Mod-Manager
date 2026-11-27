# Contributing to VI Mod Manager

Thank you for your interest in contributing to **VI Mod Manager**.

## Development setup

1. Install the **.NET 10 SDK**.
2. Clone the repository and run the test suite:
   ```bash
   dotnet test tests/ViModManager.Tests.csproj -c Release --nologo
   ```
3. Build and run the desktop application:
   ```bash
   dotnet run --project src/ViModManager.csproj
   ```

## Repository standards

To keep this project maintainable, portable, and rights-aware:

- **Respect ownership.** Do not submit game files, ripped assets, third-party mods, logos, trademarks, or other material without documented permission. Do not imply endorsement, partnership, or compatibility that the code does not provide.
- **Keep the current core generic; isolate future game-specific work.** The `v0.2` engine is intentionally generic. Proposals for GTA VI detection, RPF or other format readers, asset editing, mod discovery, or mod generation are welcome as future work, but must be opt-in, clearly documented, and subject to applicable permissions, licenses, and platform terms. Never add DRM, license-check, anti-cheat, or access-control bypasses.
- **Keep target resolution rights-aware.** Existing adapters are format-neutral and implement `ITargetAdapter` with a generic description. Future game-specific adapters may be proposed behind explicit opt-in, must not infer publishers or bypass access controls, and must document any third-party rights they rely on.
- **Preserve path safety.** All package operations must validate relative paths via `Paths.SafeRelative` and `Paths.ResolveWithin`, must avoid following symlinks or junctions, and must reject duplicate destinations. Payload streams may be arbitrarily large; do not add artificial size, entry-count, or compression-ratio caps. Only manifest text has a bounded inspection limit.
- **Preserve package compatibility.** Keep support for the legacy `vi-mod.json` manifest name unless a documented migration is added.
- **Do not add network or runtime telemetry features** without a documented privacy review. The current app makes no network calls at runtime and collects no runtime telemetry; see [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md) for build-time tooling.
- **Verify tests.** Run `dotnet test tests/ViModManager.Tests.csproj -c Release --nologo` and report any warnings or errors before opening a pull request.

## Licensing of contributions

By submitting a contribution, you confirm that:

- You have the right to submit the material.
- Your contribution is your original work, or any third-party material is clearly identified and properly licensed.
- You agree that your contribution is licensed under the project's [MIT License](LICENSE).

Do not submit confidential material or content you do not have permission to publish.

## Third-party source code

Contributions that incorporate third-party source code must:

- Preserve the upstream copyright notice and license text.
- Identify the upstream project, version, and the exact files or directories derived from it.
- Document why the license is compatible with MIT distribution, or provide the additional permission obtained.
- Update [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md) when third-party code is bundled in a release artifact.

Do not relabel third-party-derived source as original MIT-licensed work. This policy does not assume, endorse, or require any particular third-party project.

## Legal or content concerns

If you believe the repository contains material that infringes your rights, see the notice-and-takedown section in [DISCLAIMER.md](DISCLAIMER.md). For ordinary code questions, open an issue or pull request.
