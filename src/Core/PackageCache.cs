using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using ViModManager.Models;

namespace ViModManager.Core;

/// <summary>
/// Avoids reopening and reparsing every ZIP on each UI refresh. A package is cached by
/// path plus length/last-write fingerprint, so external changes are picked up naturally.
/// </summary>
public sealed class PackageCache
{
    private readonly object _gate = new();
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.OrdinalIgnoreCase);

    private sealed class Entry
    {
        public required string Fingerprint { get; init; }
        public JsonObject? Manifest { get; init; }
        public List<ConfigField> Schema { get; init; } = new();
    }

    public JsonObject? Manifest(string path)
    {
        string full = Path.GetFullPath(path);
        string fingerprint = Fingerprint(full);
        lock (_gate)
        {
            if (_entries.TryGetValue(full, out Entry? cached) && cached.Fingerprint == fingerprint)
            {
                return cached.Manifest;
            }
        }

        JsonObject? manifest = Packages.ReadManifest(full);
        var schema = manifest is null
            ? new List<ConfigField>()
            : ModConfig.ParseSchema(manifest["settings"] ?? manifest["variables"]);
        Store(full, fingerprint, manifest, schema);
        return manifest;
    }

    public List<ConfigField> Schema(string path)
    {
        string full = Path.GetFullPath(path);
        string fingerprint = Fingerprint(full);
        lock (_gate)
        {
            if (_entries.TryGetValue(full, out Entry? cached) && cached.Fingerprint == fingerprint)
            {
                return new List<ConfigField>(cached.Schema);
            }
        }

        JsonObject? manifest = Packages.ReadManifest(full);
        var schema = manifest is null
            ? new List<ConfigField>()
            : ModConfig.ParseSchema(manifest["settings"] ?? manifest["variables"]);
        Store(full, fingerprint, manifest, schema);
        return new List<ConfigField>(schema);
    }

    public void Invalidate(string path)
    {
        lock (_gate)
        {
            _entries.Remove(Path.GetFullPath(path));
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            _entries.Clear();
        }
    }

    private void Store(string path, string fingerprint, JsonObject? manifest, List<ConfigField> schema)
    {
        lock (_gate)
        {
            _entries[path] = new Entry
            {
                Fingerprint = fingerprint,
                Manifest = manifest,
                Schema = new List<ConfigField>(schema),
            };
        }
    }

    /// <summary>
    /// Length plus timestamps catch normal edits. A cheap head/tail sample hash also catches a
    /// same-length rewrite that preserves the timestamp, without hashing a multi-gigabyte archive
    /// on every UI refresh.
    /// </summary>
    private static string Fingerprint(string path)
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists)
            {
                return "missing";
            }

            const int sampleSize = 16 * 1024;
            var sample = new byte[sampleSize];
            using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

            int head = stream.Read(sample, 0, (int)Math.Min(sampleSize, stream.Length));
            hash.AppendData(sample, 0, head);

            if (stream.Length > sampleSize)
            {
                stream.Seek(-Math.Min(sampleSize, stream.Length), SeekOrigin.End);
                int tail = stream.Read(sample, 0, (int)Math.Min(sampleSize, stream.Length));
                hash.AppendData(sample, 0, tail);
            }

            string digest = Convert.ToHexString(hash.GetHashAndReset());
            return string.Create(
                System.Globalization.CultureInfo.InvariantCulture,
                $"{info.Length}:{info.LastWriteTimeUtc.Ticks}:{info.CreationTimeUtc.Ticks}:{digest}");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return "unreadable";
        }
    }
}
