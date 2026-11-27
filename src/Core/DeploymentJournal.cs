using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using ViModManager.Models;

namespace ViModManager.Core;

/// <summary>
/// A durable record of an in-flight destructive operation. It is written before a destination is
/// touched, so a crash can be resolved on the next launch instead of leaving files half deployed.
/// </summary>
public sealed class DeploymentJournal
{
    public const string InstallOperation = "install";
    public const string UninstallOperation = "uninstall";
    public const string ConfigOperation = "config";

    public const string PreparePhase = "prepare";
    public const string CommitPhase = "commit";

    public string Operation { get; set; } = InstallOperation;
    public string Phase { get; set; } = PreparePhase;
    public string ModId { get; set; } = "";
    public string ModName { get; set; } = "";
    public string TargetDir { get; set; } = "";
    public string InstallId { get; set; } = "";
    public string StartedAt { get; set; } = "";
    public List<InstalledFile> Steps { get; set; } = new();
}

public enum JournalReadStatus
{
    Missing,
    Valid,
    Corrupt,
}

/// <summary>Explicit read outcome so an unreadable journal is never mistaken for no journal.</summary>
public sealed record JournalReadResult(
    JournalReadStatus Status,
    DeploymentJournal? Journal,
    string? Error);

/// <summary>Atomic JSON persistence for the single pending <see cref="DeploymentJournal"/>.</summary>
public sealed class DeploymentJournalStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
    };

    public string FilePath { get; }

    public DeploymentJournalStore(string dataDirectory)
    {
        Directory.CreateDirectory(dataDirectory);
        FilePath = Path.Combine(dataDirectory, "deployment-journal.json");
    }

    public DeploymentJournal? Read() => ReadDetailed().Journal;

    public JournalReadResult ReadDetailed()
    {
        if (!File.Exists(FilePath))
        {
            return new JournalReadResult(JournalReadStatus.Missing, null, null);
        }

        try
        {
            using var stream = File.OpenRead(FilePath);
            var journal = JsonSerializer.Deserialize<DeploymentJournal>(stream, Options);
            return journal is null
                ? new JournalReadResult(JournalReadStatus.Corrupt, null, "The journal contains no record.")
                : new JournalReadResult(JournalReadStatus.Valid, journal, null);
        }
        catch (JsonException exception)
        {
            // A corrupt record is preserved: moving it away would turn an unfinished transaction
            // into an empty one and could leave restored files without a matching ModRecord.
            AppLog.Error($"The deployment journal at {FilePath} is unreadable and was preserved.", exception);
            return new JournalReadResult(JournalReadStatus.Corrupt, null, exception.Message);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            AppLog.Warn("The deployment journal is temporarily unavailable: " + exception.Message);
            throw;
        }
    }

    public void Write(DeploymentJournal journal)
    {
        string temp = Path.Combine(
            Path.GetDirectoryName(FilePath)!,
            $".deployment-journal-{Guid.NewGuid():N}.tmp");
        try
        {
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(stream, journal, Options);
                stream.Flush(true);
            }

            File.Move(temp, FilePath, true);
        }
        finally
        {
            TryDelete(temp);
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A leftover unnamed temp file is harmless compared with masking the real failure.
        }
    }

    public void Delete()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                File.Delete(FilePath);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            AppLog.Warn("The deployment journal could not be removed: " + exception.Message);
        }
    }
}
