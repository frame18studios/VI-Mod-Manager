using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
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
            using var archive = OpenArchive(path);
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
            string text = ReadText(stream);
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

    /// <summary>Opens a ZIP and rejects structurally ambiguous archives before use.</summary>
    public static ZipArchive OpenArchive(string path)
    {
        ZipArchive archive;
        try
        {
            archive = ZipFile.OpenRead(path);
        }
        catch (InvalidDataException)
        {
            throw new PackageException("The package is not a readable ZIP archive.");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new PackageException("The package file could not be read.");
        }

        try
        {
            ValidateArchive(archive);
            return archive;
        }
        catch
        {
            archive.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Streams one ZIP entry to disk and returns the number of bytes written. No artificial size cap
    /// is applied: packages may contain arbitrarily large game assets.
    /// </summary>
    public static long ExtractEntryToFile(ZipArchiveEntry entry, string destination)
    {
        try
        {
            using var input = entry.Open();
            using var output = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None);
            var buffer = new byte[81920];
            long total = 0;
            int read;
            while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
            {
                output.Write(buffer, 0, read);
                total += read;
            }

            output.Flush(true);
            return total;
        }
        catch (InvalidDataException)
        {
            throw new PackageException($"The package entry is corrupt or unreadable: {entry.FullName}");
        }
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
            string text = ReadText(stream);
            return JsonNode.Parse(text) as JsonObject
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
            ValidateDestinations(files, ModConfig.ConfigDestination(manifest));
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

        ValidateDestinations(files, ModConfig.ConfigDestination(manifest));
        return files;
    }

    /// <summary>
    /// Rejects destination collisions before deployment. Windows treats paths
    /// case-insensitively, so the comparison is case-insensitive as well.
    /// </summary>
    public static void ValidateDestinations(IEnumerable<PackageFile> files, string? configDestination = null)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in files)
        {
            if (!seen.Add(file.Destination))
            {
                throw new PackageException($"Duplicate destination in package: {file.Destination}");
            }
        }

        if (!string.IsNullOrWhiteSpace(configDestination))
        {
            string config = Paths.SafeRelative(configDestination);
            if (!seen.Add(config))
            {
                throw new PackageException(
                    $"The package config file collides with a deployed file: {config}");
            }
        }
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
        using var archive = OpenArchive(path);
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

        // Two entries such as A.txt and a.txt would silently overwrite each other on Windows.
        ValidateDestinations(result);
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
                // Resolve again right before packing: a file or parent folder can be swapped
                // for a link after enumeration, and its target must never be packed.
                string source;
                try
                {
                    source = Paths.ResolveWithin(directory, relative);
                }
                catch (PackageException)
                {
                    continue;
                }

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

    /// <summary>
    /// Rejects archives whose central directory cannot be mapped unambiguously. Deliberately does
    /// not impose size, entry-count, or compression-ratio caps: legitimate packages can contain
    /// very large assets, and the deploy path records every byte it writes.
    /// </summary>
    private static void ValidateArchive(ZipArchive archive)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (var entry in archive.Entries)
            {
                // Directory entries have an empty Name and do not write payload bytes.
                if (entry.Name.Length == 0)
                {
                    continue;
                }

                string normalized = entry.FullName.Replace('\\', '/').TrimEnd('/');
                if (!names.Add(normalized))
                {
                    throw new PackageException($"The package contains a duplicate entry: {normalized}");
                }
            }
        }
        catch (InvalidDataException)
        {
            throw new PackageException("The package is not a readable ZIP archive.");
        }
    }

    private static string ReadText(Stream stream)
    {
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd();
    }
}

public static class Paths
{
    private static readonly char[] InvalidNameChars = { '<', '>', ':', '"', '|', '?', '*', '\0' };

    private static readonly HashSet<string> ReservedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM0", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT0", "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

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
        foreach (string part in parts)
        {
            if (part.Length == 0 || part == "." || part == "..")
            {
                throw new PackageException($"Unsafe relative path: {value}");
            }

            if (part.Length > 255)
            {
                throw new PackageException($"A path segment is too long: {part}");
            }

            if (part.Any(char.IsControl) || part.IndexOfAny(InvalidNameChars) >= 0)
            {
                throw new PackageException($"Unsafe characters in path: {value}");
            }

            if (part.EndsWith('.') || part.EndsWith(' '))
            {
                throw new PackageException($"A path segment cannot end with a dot or space: {value}");
            }

            string stem = part.Split('.')[0];
            if (ReservedNames.Contains(stem))
            {
                throw new PackageException($"Windows-reserved file name in path: {value}");
            }
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

        string boundary = Path.TrimEndingDirectorySeparator(rootFull);
        string prefix = boundary.EndsWith(Path.DirectorySeparatorChar)
            ? boundary
            : boundary + Path.DirectorySeparatorChar;
        if (!combined.Equals(boundary, comparison) && !combined.StartsWith(prefix, comparison))
        {
            throw new PackageException("A path in this package escapes the selected folder.");
        }

        EnsureNoReparsePoints(boundary, combined);
        return combined;
    }

    /// <summary>
    /// Rejects existing symlinks and junctions anywhere below the root. Lexical containment is
    /// not enough: a junction inside the target can redirect a write somewhere else entirely.
    /// </summary>
    private static void EnsureNoReparsePoints(string rootFull, string combined)
    {
        string relative = Path.GetRelativePath(rootFull, combined);
        if (relative == ".")
        {
            return;
        }

        string current = rootFull;
        foreach (string segment in relative.Split(
            Path.DirectorySeparatorChar,
            StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, segment);
            if (IsReparsePoint(current))
            {
                throw new PackageException(
                    $"A path component is a symbolic link or junction and cannot be used: {segment}");
            }
        }
    }

    /// <summary>True when the path itself is a symlink, junction, or other reparse point.</summary>
    public static bool IsReparsePoint(string path)
    {
        try
        {
            return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
        }
        catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
        {
            return false;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // The write itself will report an inaccessible path; do not mask it here.
            return false;
        }
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
