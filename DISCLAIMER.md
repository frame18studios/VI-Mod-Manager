# Legal Notice, Disclaimer & Responsible Use

**Last updated: 2026-11-27**

This document describes the intended scope of VI Mod Manager. It is a plain-language notice, not legal advice. It does not create rights, grant permissions, or guarantee that a particular use is lawful.

## 1. Independent, open-source GTA VI mod manager

VI Mod Manager is an independent, open-source desktop mod manager intended for Grand Theft Auto VI workflows. The current `v0.2` release provides only generic package management and reversible file deployment to user-selected folders; game detection, RPF support, asset editing, mod discovery, and mod generation are future plans and are not implemented in this version.

- It is **not affiliated with, connected to, endorsed by, sponsored by, authorized by, or associated with** Rockstar Games, Take-Two Interactive, any other game publisher, game developer, platform holder, mod site, or other company. It is not a Rockstar Games or Take-Two Interactive product.
- It does not host, index, catalog, recommend, link to, or distribute third-party mods, game files, or game assets. The only bundled package is `examples/example-package-demo.zip`, an original text-and-JSON demo created for this project and covered by the repository's MIT License.
- The current release makes no compatibility, support, or endorsement claim for Grand Theft Auto VI or any other specific product. Any future game-specific support will be described in the release notes for the version that introduces it and will remain unaffiliated with the rights holder.
- References to "Grand Theft Auto VI", "GTA VI", "Grand Theft Auto", and "GTA" describe the project's intended focus. Those names and related marks belong to their respective owners. The project uses no third-party logos, art, or game assets and makes no claim to any third-party mark.
- All third-party product names, trademarks, service marks, and logos belong to their respective owners. The project name and logo are used only to identify this open-source project and do not claim any third-party mark.
- The word "mod" in the project name reflects the intended mod-management purpose. It does not imply affiliation with, endorsement by, or authorization from any modding community or rights holder.

## 2. What the software does — and does not do

The application:

- Reads ZIP archive structure and plain-JSON manifest metadata.
- Copies package payload files as opaque bytes into a folder the user selects.
- Optionally writes typed JSON configuration values declared by a package manifest.
- Backs up overwritten files and records SHA-256 hashes so deployment can be reversed.
- Stores its own settings, package library, and backups in a separate data directory.

The current `v0.2` release does **not**:

- Include, download, host, or link to game files, game assets, mods, third-party mod repositories, or other third-party content.
- Execute package code, scripts, installers, or payload content. Opening a local folder in File Explorer is a separate, user-initiated convenience action.
- Decrypt, unpack, or modify proprietary game containers. Any future format support would cover only formats the project has the rights to handle and would not bypass DRM, license checks, anti-cheat systems, or access controls.
- Inspect, patch, or interact with a game's memory, process, network traffic, or online services.
- Provide cheat, piracy, account, or access-control functionality.
- Send runtime telemetry or require an online account.
- Collect, transmit, or sell personal data.

This list describes v0.2. Future game-specific features, if implemented, will be documented in the release notes for the version that introduces them and will remain subject to applicable permissions and terms.

## 3. Third-party content and user responsibility

The [MIT License](LICENSE) applies only to the original source code of VI Mod Manager. It does not grant any license or permission to copy, modify, distribute, or deploy third-party files, mods, assets, trademarks, or game content.

If you import, deploy, or share a package, you are responsible for:

- Having the rights, licenses, and permissions required for that content.
- Complying with the terms of service, EULA, and community rules that apply to the target software or platform.
- Ensuring that your use does not infringe copyright, trademark, database, or other rights.
- Keeping backups and verifying that you are deploying to a folder you control.

The app validates paths and hashes, not intent. It does not scan packages for malware or verify that their content is lawful. Only import packages from sources you trust.

Do not use VI Mod Manager to infringe rights, bypass technical protection measures, distribute malware, or violate applicable law or platform terms.

## 4. No endorsement or compatibility claims

References to file formats, folders, game titles, or generic workflows are provided for interoperability and description only. Naming a third-party title to describe project direction is descriptive use; it does not grant permission to use associated trademarks, art, lore, or content, and it does not imply endorsement, partnership, sponsorship, compatibility, or authorization by any rights holder. The project does not authorize modding, reverse engineering, or any other activity that a target product's terms may prohibit.

## 5. Notice and takedown

This repository does not host user packages. If you believe that material in this repository or its release assets infringes your rights, including trademark or other intellectual-property concerns, use the private reporting channel described in [SECURITY.md](SECURITY.md). Do not post personal contact details in a public issue. Include:

- The material you believe is infringing and its location.
- The right you believe is infringed.
- A statement that you are authorized to act for the rights holder.
- Your contact details, sent privately through that channel.

Maintainers will review the request and remove or disable access to material where appropriate. Please only submit claims you are authorized to make.

## 6. No warranty

The software is provided "as is", without warranty of any kind, express or implied, including warranties of merchantability, fitness for a particular purpose, title, and non-infringement. To the maximum extent permitted by law, the authors and contributors are not liable for any claim, damage, or other liability arising from the software or its use. See [LICENSE](LICENSE).

## 7. Not legal advice

This notice describes the project's intended scope. It is not legal advice and does not guarantee that a use is lawful or that a rights holder will not raise a claim. A disclaimer cannot create permission to use third-party material. If you have a specific legal question, consult a qualified lawyer in your jurisdiction.
