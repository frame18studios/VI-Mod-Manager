using System.Collections.Generic;

namespace ViModManager.Models;

/// <summary>Broad category used for filtering and badges. Mod contents are still deployed as opaque bytes.</summary>
public sealed class ModRecord
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Version { get; set; } = "Unknown";
    public string Author { get; set; } = "Unknown";
    public string Description { get; set; } = "No description provided.";
    public string Package { get; set; } = "";
    public bool Enabled { get; set; } = true;
    public bool Installed { get; set; }
    public bool HasManifest { get; set; }
    public int FileCount { get; set; }
    public string Sha256 { get; set; } = "";
    public int Priority { get; set; }
    public string Kind { get; set; } = "Other";

    /// <summary>
    /// When true a manifest-less package deploys every safe ZIP entry as a loose file.
    /// Turn this off to keep such a package catalogued but inert.
    /// </summary>
    public bool AutoDeploy { get; set; }

    /// <summary>Auto-detected source/destination pairs for manifest-less packages.</summary>
    public List<AutoFile> AutoFiles { get; set; } = new();

    public Dictionary<string, string> Config { get; set; } = new();
    public List<InstalledFile> InstalledFiles { get; set; } = new();
    public string? InstallId { get; set; }
    public string? LoadedAt { get; set; }
}

public sealed class AutoFile
{
    public string Source { get; set; } = "";
    public string Destination { get; set; } = "";
}

public sealed class InstalledFile
{
    public string Destination { get; set; } = "";

    /// <summary>The original file as it existed before the first deployment, if any.</summary>
    public string? Backup { get; set; }
    public string? BackupHash { get; set; }

    /// <summary>
    /// A snapshot of the currently generated file taken for one config-update transaction. It is
    /// separate from <see cref="Backup"/> so a failed update restores the last working config
    /// instead of deleting it when no original file existed.
    /// </summary>
    public string? TransactionBackup { get; set; }
    public string? TransactionBackupHash { get; set; }

    public string InstalledHash { get; set; } = "";
    public string Kind { get; set; } = "file";
}

public sealed class ModDatabase
{
    public List<ModRecord> Mods { get; set; } = new();
}
