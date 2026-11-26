using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;

namespace ViModManager.Core;

/// <summary>
/// Labels a package by the assets it contains. Classification is informational only:
/// every file is deployed as opaque bytes and nothing in a package is ever executed.
/// </summary>
public static class ModClassifier
{
    public static readonly string[] AllKinds = { "All", "Texture", "Model", "Script", "Audio", "Archive", "Config", "Other" };

    private static readonly (string Kind, string[] Extensions)[]
        Rules =
        {
            ("Script", new[] { ".dll", ".lua", ".py", ".js", ".ts", ".cs", ".vb", ".ps1", ".bat", ".cmd", ".sh" }),
            ("Model", new[] { ".obj", ".fbx", ".glb", ".gltf", ".dae", ".blend", ".mesh", ".skel" }),
            ("Texture", new[] { ".png", ".jpg", ".jpeg", ".dds", ".tga", ".bmp", ".webp", ".ktx", ".hdr" }),
            ("Audio", new[] { ".wav", ".ogg", ".mp3", ".flac", ".aac", ".snd", ".bank" }),
            ("Archive", new[] { ".zip", ".7z", ".rar", ".tar", ".gz", ".pak", ".bundle", ".cab", ".dat", ".bin" }),
            ("Config", new[] { ".json", ".ini", ".xml", ".toml", ".yaml", ".yml", ".cfg", ".txt" }),
        };

    public static string Classify(JsonObject? manifest, IEnumerable<string> paths)
    {
        if (manifest is not null)
        {
            string declared = (ReadText(manifest["kind"]) ?? ReadText(manifest["category"]) ?? ReadText(manifest["type"]) ?? "").Trim();
            string known = AllKinds.FirstOrDefault(kind =>
                string.Equals(kind, declared, StringComparison.OrdinalIgnoreCase) && kind != "All") ?? "";
            if (known.Length > 0)
            {
                return known;
            }
        }

        return Classify(paths);
    }

    public static string Classify(IEnumerable<string> paths)
    {
        var scores = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (string path in paths)
        {
            string extension = Path.GetExtension(path);
            if (extension.Length == 0)
            {
                continue;
            }

            foreach (var (kind, extensions) in Rules)
            {
                if (extensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
                {
                    scores[kind] = scores.TryGetValue(kind, out int count) ? count + 1 : 1;
                    break;
                }
            }
        }

        if (scores.Count == 0)
        {
            return "Other";
        }

        return Rules.Select(rule => rule.Kind)
            .OrderByDescending(kind => scores.GetValueOrDefault(kind))
            .First();
    }

    public static string Hint(string kind) => kind switch
    {
        "Texture" => "Image, texture dictionary or material files.",
        "Model" => "Meshes, skeletons, maps or collision files.",
        "Script" => "Script or plugin files, deployed but never executed by the manager.",
        "Audio" => "Sound banks, music or voice files.",
        "Archive" => "Prebuilt archives deployed as file replacements.",
        "Config" => "Settings, manifests or text data.",
        _ => "Files that do not match a known asset category.",
    };

    private static string? ReadText(JsonNode? node) => node switch
    {
        JsonValue value when value.GetValueKind() == System.Text.Json.JsonValueKind.String => value.GetValue<string>(),
        JsonValue value => value.ToJsonString(),
        _ => null,
    };
}
