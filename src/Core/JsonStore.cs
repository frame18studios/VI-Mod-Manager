using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ViModManager.Core;

/// <summary>A small atomic JSON document store, mirroring the behaviour of the original app.</summary>
public sealed class JsonStore<T> where T : class, new()
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private readonly object _gate = new();

    public string FilePath { get; }
    public T Current { get; private set; }

    public JsonStore(string filePath)
    {
        FilePath = Path.GetFullPath(filePath);
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        Current = Read();
    }

    private T Read()
    {
        try
        {
            if (!File.Exists(FilePath))
            {
                var fresh = new T();
                Write(fresh);
                return fresh;
            }

            using var stream = File.OpenRead(FilePath);
            return JsonSerializer.Deserialize<T>(stream, Options) ?? new T();
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
            // Never silently destroy user data: park the damaged document and start clean.
            Quarantine();
            var fresh = new T();
            Write(fresh);
            return fresh;
        }
    }

    private void Quarantine()
    {
        try
        {
            if (!File.Exists(FilePath))
            {
                return;
            }

            string stamp = DateTimeOffset.Now.ToString("yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture);
            string target = $"{FilePath}.corrupt-{stamp}";
            int suffix = 1;
            while (File.Exists(target))
            {
                target = $"{FilePath}.corrupt-{stamp}-{suffix++}";
            }

            File.Move(FilePath, target);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A locked or unwritable file cannot be quarantined; the fresh store still works in memory.
        }
    }

    private void Write(T value)
    {
        var temp = FilePath + ".tmp";
        using (var stream = File.Create(temp))
        {
            JsonSerializer.Serialize(stream, value, Options);
            stream.Flush(true);
        }

        File.Move(temp, FilePath, true);
    }

    public void Save()
    {
        lock (_gate)
        {
            Write(Current);
        }
    }

    public void Update(Action<T> mutate)
    {
        lock (_gate)
        {
            mutate(Current);
            Write(Current);
        }
    }
}
