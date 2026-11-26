using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json.Nodes;

namespace ViModManager.Core;

public sealed class PackageException : Exception
{
    public PackageException(string message) : base(message) { }
}

public sealed record PackageFile(string Source, string Destination);

public sealed class PackageContents
{
    public List<string> Entries { get; } = new();
    public JsonObject? Manifest { get; set; }
    public string? ManifestError { get; set; }
    public bool HasManifest => Manifest is not null;
}

public static class Packages
{
    public const string ManifestFileName = "vmm-manifest.json";
    public const string LegacyManifestFileName = "vi-mod.json";

    public static string? FindManifestFile(string directory)
    {
        string current = Path.Combine(directory, ManifestFileName);
        if (File.Exists(current))
        {
            return current;
        }

        string legacy = Path.Combine(directory, LegacyManifestFileName);
        return File.Exists(legacy) ? legacy : null;
    }

    public static PackageContents Inspect(string path)
    {
        if (!File.Exists(path))
        {
            throw new PackageException("Package file not found.");
        }

        if (!IsZip(path))
        {
            throw new PackageException("Only ZIP packages are supported for package installation.");
        }

        var contents = new PackageContents();
        try
        {
            using var archive = ZipFile.OpenRead(path);
            foreach (var entry in archive.Entries)
            {
                contents.Entries.Add(entry.FullName.Replace('\\', '/'));
            }

            var manifestEntry = archive.Entries.FirstOrDefault(entry =>
                string.Equals(entry.FullName.Replace('\\', '/'), ManifestFileName, StringComparison.OrdinalIgnoreCase))
                ?? archive.Entries.FirstOrDefault(entry =>
                    string.Equals(entry.FullName.Replace('\\', '/'), LegacyManifestFileName, StringComparison.OrdinalIgnoreCase));
            if (manifestEntry is null)
            {
                return contents;
            }

            string manifestName = manifestEntry.FullName;
            using var stream = manifestEntry.Open();
            using var reader = new StreamReader(stream);
            string text = reader.ReadToEnd();
            try
            {
                contents.Manifest = JsonNode.Parse(text) as JsonObject
                    ?? throw new PackageException($"{manifestName} must contain a JSON object.");
            }
            catch (System.Text.Json.JsonException exception)
            {
                contents.ManifestError = exception.Message;
                throw new PackageException($"Invalid {manifestName}: {exception.Message}");
            }
        }
        catch (InvalidDataException)
        {
            throw new PackageException("The package is not a readable ZIP archive.");
        }

        return contents;
    }

    public static JsonObject? ReadManifest(string path)
    {
        try
        {
            return Inspect(path).Manifest;
        }
        catch (PackageException)
        {
            return null;
        }
    }

    /// <summary>Reads the current or legacy manifest from an unpacked package folder.</summary>
    public static JsonObject? ReadManifestFile(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            using var reader = new StreamReader(stream);
            return JsonNode.Parse(reader.ReadToEnd()) as JsonObject
                ?? throw new PackageException($"{ManifestFileName} must contain a JSON object.");
        }
        catch (System.Text.Json.JsonException exception)
        {
            throw new PackageException($"Invalid {ManifestFileName}: {exception.Message}");
        }
    }

    public static bool IsZip(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            Span<byte> signature = stackalloc byte[4];
            return stream.Read(signature) == 4
                && signature[0] == (byte)'P' && signature[1] == (byte)'K'
                && signature[2] is 3 or 5 or 7 && signature[3] is 4 or 6 or 8;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    public static List<PackageFile> Files(JsonObject manifest)
    {
        var files = new List<PackageFile>();
        if (manifest["files"] is not JsonArray array)
        {
            return files;
        }

        foreach (var item in array)
        {
            if (item is not JsonObject file)
            {
                throw new PackageException($"{ManifestFileName} files entries must be objects.");
            }

            string source = ReadText(file["source"]).Trim();
            string destination = ReadText(file["destination"]).Trim();
            if (source.Length == 0 || destination.Length == 0)
            {
                throw new PackageException($"Each {ManifestFileName} files entry needs a source and destination.");
            }

            files.Add(new PackageFile(Paths.SafeRelative(source), Paths.SafeRelative(destination)));
        }

        return files;
    }

    private static string ReadText(JsonNode? node) => node switch
    {
        JsonValue value when value.GetValueKind() == System.Text.Json.JsonValueKind.String => value.GetValue<string>(),
        JsonValue value => value.ToJsonString(),
        _ => "",
    };

    /// <summary>
    /// Builds deployable pairs for a ZIP without a manifest. Unsafe and metadata entries
    /// are skipped rather than allowed to fail the whole import.
    /// </summary>
    public static List<PackageFile> AutoFiles(string path)
    {
        var result = new List<PackageFile>();
        using var archive = ZipFile.OpenRead(path);
        foreach (var entry in archive.Entries)
        {
            string name = entry.FullName.Replace('\\', '/').Trim();
            if (name.Length == 0 || name.EndsWith('/') || IsMetadata(name))
            {
                continue;
            }

            try
            {
                string safe = Paths.SafeRelative(name);
                result.Add(new PackageFile(safe, safe));
            }
            catch (PackageException)
            {
                // A bad path in one entry must not poison the rest of the package.
            }
        }

        return result;
    }

    /// <summary>
    /// Lists files inside a mod folder without following junctions or symbolic links,
    /// so a crafted folder cannot pull files from outside its own tree.
    /// </summary>
    public static List<string> SafeFolderFiles(string directory)
    {
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.ReparsePoint,
            ReturnSpecialDirectories = false,
        };

        var result = new List<string>();
        foreach (string file in Directory.EnumerateFiles(directory, "*", options))
        {
            string relative = Path.GetRelativePath(directory, file).Replace('\\', '/');
            if (relative.Contains("..", StringComparison.Ordinal))
            {
                continue;
            }

            try
            {
                result.Add(Paths.SafeRelative(relative));
            }
            catch (PackageException)
            {
                // Skip an unsafe file name instead of failing the whole import.
            }
        }

        return result;
    }

    /// <summary>Packs a loose mod folder into the library's ZIP representation.</summary>
    public static void PackDirectory(string directory, string destinationZip)
    {
        if (!Directory.Exists(directory))
        {
            throw new PackageException("Package folder not found.");
        }

        string temp = destinationZip + ".packing";
        if (File.Exists(temp))
        {
            File.Delete(temp);
        }

        // Build explicitly from the safe list: ZipFile.CreateFromDirectory would follow
        // reparse points and could include files outside the mod folder.
        using (var archive = ZipFile.Open(temp, ZipArchiveMode.Create))
        {
            foreach (string relative in SafeFolderFiles(directory))
            {
                string source = Path.Combine(directory, relative.Replace('/', Path.DirectorySeparatorChar));
                archive.CreateEntryFromFile(source, relative, CompressionLevel.Optimal);
            }
        }

        File.Move(temp, destinationZip, true);
    }

    /// <summary>Wraps a single asset (texture, model, script, archive, anything) as a loadable package.</summary>
    public static void WrapFile(string file, string destinationZip)
    {
        if (!File.Exists(file))
        {
            throw new PackageException("Package file not found.");
        }

        string name = Path.GetFileName(file);
        if (Paths.SafeRelative(name) != name)
        {
            throw new PackageException("The file name cannot be deployed safely.");
        }

        string temp = destinationZip + ".packing";
        if (File.Exists(temp))
        {
            File.Delete(temp);
        }

        using (var archive = ZipFile.Open(temp, ZipArchiveMode.Create))
        {
            using var input = File.OpenRead(file);
            using var output = archive.CreateEntry(name, CompressionLevel.Optimal).Open();
            input.CopyTo(output);
        }

        File.Move(temp, destinationZip, true);
    }

    private static bool IsMetadata(string name)
    {
        string lower = name.ToLowerInvariant();
        if (lower.StartsWith("__macosx/") || lower.EndsWith("/ds_store") || lower.EndsWith("/thumbs.db"))
        {
            return true;
        }

        string fileName = Path.GetFileName(name);
        return fileName.Length > 0 && fileName[0] == '.';
    }
}

public static class Paths
{
    public static string SafeRelative(string? value)
    {
        string normalized = (value ?? "").Replace('\\', '/').Trim();
        if (normalized.Length == 0)
        {
            throw new PackageException("An empty path is not allowed.");
        }

        if (normalized.StartsWith('/') || normalized.Contains(':'))
        {
            throw new PackageException($"Absolute paths are not allowed: {value}");
        }

        string[] parts = normalized.Split('/');
        if (parts.Any(part => part.Length == 0 || part == "." || part == ".."))
        {
            throw new PackageException($"Unsafe relative path: {value}");
        }

        return string.Join('/', parts);
    }

    public static string ResolveWithin(string root, string relative)
    {
        string rootFull = Path.GetFullPath(root);
        string combined = Path.GetFullPath(Path.Combine(rootFull, relative.Replace('/', Path.DirectorySeparatorChar)));
        StringComparison comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        if (!combined.Equals(rootFull, comparison)
            && !combined.StartsWith(rootFull + Path.DirectorySeparatorChar, comparison))
        {
            throw new PackageException("A path in this package escapes the selected folder.");
        }

        return combined;
    }
}

public static class Hashing
{
    public static string Sha256File(string path)
    {
        using var stream = File.OpenRead(path);
        using var sha = System.Security.Cryptography.SHA256.Create();
        return Convert.ToHexString(sha.ComputeHash(stream)).ToLowerInvariant();
    }
}
