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

    public DeploymentJournal? Read()
    {
        if (!File.Exists(FilePath))
        {
            return null;
        }

        try
        {
            using var stream = File.OpenRead(FilePath);
            return JsonSerializer.Deserialize<DeploymentJournal>(stream, Options);
        }
        catch (JsonException exception)
        {
            // Only a genuinely unreadable document is quarantined. A transient share/lock error
            // must leave the journal in place so startup recovery can still resolve the operation.
            AppLog.Error("The deployment journal is corrupt and was quarantined.", exception);
            Quarantine();
            return null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            AppLog.Warn("The deployment journal is temporarily unavailable: " + exception.Message);
            throw;
        }
    }

    public void Write(DeploymentJournal journal)
    {
        string temp = FilePath + ".tmp";
        using (var stream = File.Create(temp))
        {
            JsonSerializer.Serialize(stream, journal, Options);
            stream.Flush(true);
        }

        File.Move(temp, FilePath, true);
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

    private void Quarantine()
    {
        try
        {
            string stamp = DateTimeOffset.Now.ToString(
                "yyyyMMdd-HHmmss",
                System.Globalization.CultureInfo.InvariantCulture);
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
            // The app can continue; the next operation writes a fresh journal.
        }
    }
}
