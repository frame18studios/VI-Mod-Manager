# VI Mod Manager Package Format

VI Mod Manager treats package payloads as data. It does not execute them or inspect proprietary binary containers. An optional plain-JSON manifest named **`vmm-manifest.json`** at the root of a `.zip` archive (or unpacked folder) provides metadata, typed settings, and explicit file mappings. The older `vi-mod.json` name remains supported for existing packages; if both names are present, `vmm-manifest.json` takes precedence.

> **Responsible use:** Packages are supplied by you. Only import and deploy content you have the rights to use, and follow the terms that apply to the target software. VI Mod Manager does not host, download, or bundle packages, and does not bypass DRM, license checks, or anti-cheat systems. See [DISCLAIMER.md](../DISCLAIMER.md).

---

## Accepted Inputs

| Input | Behavior |
| --- | --- |
| **`.zip` archive** | Imported directly into `mods/`. Uses root `vmm-manifest.json` if present; otherwise safe entries auto-deploy to matching relative paths. |
| **Folder** | Packed safely into a `.zip` inside `mods/` (ignoring symlinks/junctions). Uses root `vmm-manifest.json` if present. |
| **Single file** | Wrapped in a manifest-free `.zip` so any standalone asset can be tracked, enabled/disabled, and deployed reversibly. |

---

## `vmm-manifest.json` Example

```json
{
  "id": "example-package-demo",
  "name": "Example Package Demo",
  "version": "1.0.0",
  "author": "Package Author",
  "description": "Harmless demo package for exercising the editor and deploy flow.",
  "kind": "Config",
  "settings": [
    { "key": "scale", "label": "Scale", "type": "float", "default": 1.0, "min": 0.5, "max": 2.0, "step": 0.1 },
    { "key": "preset", "label": "Preset", "type": "choice", "options": ["relaxed", "standard", "focused"] },
    { "key": "show_debug", "label": "Show debug", "type": "bool", "default": false },
    { "key": "item_count", "label": "Item count", "type": "int", "default": 4, "min": 1, "max": 32 },
    { "key": "label", "label": "Label", "type": "text", "default": "demo" }
  ],
  "config": { "file": "packages/example-package-demo/settings.json" },
  "files": [
    { "source": "payload/install-note.txt", "destination": "packages/example-package-demo/install-note.txt" }
  ]
}
```

### Legacy manifest name

Packages created with the earlier `vi-mod.json` filename continue to import. New packages should use `vmm-manifest.json`. Both files use the same JSON schema.

### Top-Level Fields

| Field | Required | Description |
| --- | --- | --- |
| `id` | Recommended | Stable identifier. Derived from filename if omitted. |
| `name` | Recommended | Display name shown in the Library. |
| `version`, `author`, `description` | Optional | Metadata shown in package details. |
| `kind` (or `category`) | Optional | Asset category badge (`Texture`, `Model`, `Script`, `Audio`, `Archive`, `Config`, `Other`). Auto-detected if omitted. |
| `settings` (or `variables`) | Optional | Array of typed configuration fields rendered in the UI. |
| `config.file` | Optional | Relative destination inside the target folder where typed JSON values are written on deployment. |
| `files` | Optional | Array of `{ "source": "...", "destination": "..." }` copy mappings. |

---

## Supported Setting Types

| Type | Control | Supported Properties |
| --- | --- | --- |
| `bool` | Toggle switch | `default` |
| `int` | Integer stepper | `default`, `min`, `max`, `step` |
| `float` | Decimal stepper | `default`, `min`, `max`, `step` |
| `choice` | Dropdown | `options`, `default` |
| `text` | Text box | `default` |

Custom key/value string entries can also be added to any package directly in the Library UI.

---

## What Manifests Cannot Do

A manifest is plain data. It cannot execute code, install services, create scheduled tasks, request elevation, or access the network. Declared paths are validated before any copy, and payload files are treated as opaque bytes.

---

## Path Safety & Backups

Every relative path (`files[].source`, `files[].destination`, `config.file`, and auto-detected ZIP entries) is validated against the target folder and rejected if it:

- Is empty, absolute, or begins with `/` or a drive letter (`C:`);
- Contains `.` or `..` traversal segments;
- Resolves outside the chosen target directory after normalization.

Before replacing any existing file in the target folder, VI Mod Manager copies the original into `backups/<install-id>/` and records the deployed file's SHA-256 hash. On unload, backups are restored only if the deployed file has not been modified outside the manager.

See [`examples/example-package-demo.zip`](../examples/example-package-demo.zip) for a complete reference package.
