# VI Mod Manager Package Format

VI Mod Manager treats package payloads as data. It does not execute them or inspect proprietary binary containers. An optional plain-JSON manifest named **`vmm-manifest.json`** at the root of a `.zip` archive (or unpacked folder) provides metadata, typed settings, and explicit file mappings. The older `vi-mod.json` name remains supported for existing packages; if both names are present, `vmm-manifest.json` takes precedence.

> **Responsible use:** Packages are supplied by you. Only import and deploy content you have the rights to use, and follow the terms that apply to the target software. VI Mod Manager does not host, download, or bundle third-party packages; the only bundled package is the original demo described below. It does not bypass DRM, license checks, or anti-cheat systems. See [DISCLAIMER.md](../DISCLAIMER.md).

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
  "author": "VI Mod Manager",
  "description": "A harmless demo package for testing the values editor, load order, and reversible deploy/unload flow. Writes only demo text files and a generated JSON config.",
  "kind": "Config",
  "settings": [
    {
      "key": "scale",
      "label": "Scale",
      "type": "float",
      "default": 1.0,
      "min": 0.1,
      "max": 10.0,
      "step": 0.1,
      "description": "A numeric value with a bounded range."
    },
    {
      "key": "preset",
      "label": "Preset",
      "type": "choice",
      "options": ["relaxed", "standard", "focused"],
      "default": "standard",
      "description": "Preset stored in the generated config."
    },
    {
      "key": "show_debug",
      "label": "Show debug note",
      "type": "bool",
      "default": false
    },
    {
      "key": "item_count",
      "label": "Item count",
      "type": "int",
      "default": 25,
      "min": 1,
      "max": 200,
      "step": 5
    },
    {
      "key": "label",
      "label": "Save label",
      "type": "text",
      "default": "Example Run"
    }
  ],
  "config": { "file": "packages/example-package-demo/settings.json" },
  "files": [
    { "source": "payload/install-note.txt", "destination": "packages/example-package-demo/install-note.txt" },
    { "source": "payload/demo-info.json", "destination": "packages/example-package-demo/demo-info.json" }
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
| `bool` | Toggle switch | `default`, `description` |
| `int` | Integer stepper | `default`, `min`, `max`, `step`, `description` |
| `float` | Decimal stepper | `default`, `min`, `max`, `step`, `description` |
| `choice` | Dropdown | `options`, `default`, `description` |
| `text` | Text box | `default`, `description` |

Custom key/value string entries can also be added to any package directly in the Library UI.

---

## What Manifests Cannot Do

A manifest is plain data. It cannot execute code, install services, create scheduled tasks, request elevation, or access the network. Declared paths are validated before any copy, and payload files are treated as opaque bytes.

---

## Path Safety & Backups

Every relative path (`files[].source`, `files[].destination`, `config.file`, and auto-detected ZIP entries) is validated against the target folder and rejected if it:

- Is empty, absolute, or begins with `/` or a drive letter (`C:`);
- Contains `.` or `..` traversal segments, Windows-reserved device names, or invalid filename characters;
- Resolves outside the chosen target directory after normalization;
- Passes through an existing symbolic link or junction inside the target folder;
- Duplicates another destination in the same package, including the generated `config.file` destination (compared case-insensitively, as Windows does).

The selected target is resolved through a generic local-folder adapter. The manager never identifies a game or application, never matches executable names, and never opens proprietary containers; it only describes and writes into an ordinary directory.

ZIP archives are checked for structural ambiguity: duplicate entry names are rejected because they would make a destination plan ambiguous. There are no artificial compressed-size, expanded-size, per-entry, entry-count, or compression-ratio caps. Payload bytes stream directly to disk, so a 70 GB asset is handled the same way as a small file, subject only to available disk space and the filesystem. Manifest text is bounded during inspection so a malformed archive cannot exhaust memory; payload sizes are never capped. Manifests are read as UTF-8 JSON; a manifest that is not valid JSON is rejected without executing anything.

Before replacing any existing file in the target folder, VI Mod Manager copies the original into `backups/<install-id>/` and records both the original backup hash and the deployed file's SHA-256 hash. The stored package hash is verified before any file is written; a changed archive is refused until it is re-imported. On unload, every file is preflighted first. If the deployed file was modified outside the manager, or a required backup is missing or damaged, the unload is refused and the deployment record is kept so no files are stranded.

Every destructive step is recorded in a local `deployment-journal.json` before a destination is touched. On the next launch, an interrupted install is rolled back (before commit) or completed (after commit), and interrupted unloads or config writes are finished. If recovery cannot verify a safe state, the journal is kept and the UI shows a warning instead of risking a partial deployment.

The manifest above is the exact manifest used by [`examples/example-package-demo.zip`](../examples/example-package-demo.zip), a harmless text-and-JSON reference package.
