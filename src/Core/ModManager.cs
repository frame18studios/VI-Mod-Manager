using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Text.Json;
using System.Text.Json.Nodes;
using ViModManager.Models;

namespace ViModManager.Core;

public sealed class LoadOutcome
{
    public string Status { get; init; } = "loaded";
    public string? ConfigPath { get; init; }
}

public sealed class LoadSummary
{
    public List<string> Loaded { get; } = new();
    public List<(string Name, string Why)> Skipped { get; } = new();
    public List<(string Name, string Why)> Failed { get; } = new();
}

public sealed record Conflict(string Destination, string First, string Second);

public sealed class ModManager
{
    private static readonly JsonSerializerOptions ConfigJson = new() { WriteIndented = true };

    private readonly object _operationGate = new();
    private readonly object _stateGate = new();
    private readonly DeploymentJournalStore _journal;

    public JsonStore<ModDatabase> Store { get; }
    public JsonStore<HistoryDatabase> History { get; }
    public JsonStore<ProfileDatabase>? Profiles { get; }
    public string ModsDir { get; }
    public string BackupsDir { get; }
    public PackageCache Cache { get; } = new();

    /// <summary>
    /// Set when a previously interrupted operation could not be resolved automatically. The UI
    /// surfaces this so the user knows why a deployment is being refused.
    /// </summary>
    public string? RecoveryWarning { get; private set; }

    public ModManager(
        JsonStore<ModDatabase> store,
        JsonStore<HistoryDatabase> history,
        string modsDir,
        string backupsDir,
        JsonStore<ProfileDatabase>? profiles = null)
    {
        Store = store;
        History = history;
        Profiles = profiles;
        ModsDir = Path.GetFullPath(modsDir);
        BackupsDir = Path.GetFullPath(backupsDir);
        Directory.CreateDirectory(ModsDir);
        Directory.CreateDirectory(BackupsDir);

        string dataDir = Path.GetDirectoryName(Store.FilePath)
            ?? throw new InvalidOperationException("The library database has no parent directory.");
        _journal = new DeploymentJournalStore(dataDir);
        AppLog.Configure(Path.Combine(dataDir, "logs"));
        RecoverPendingOperation();
    }

    // --------------------------------------------------------------- coordination
    private IDisposable EnterOperation()
    {
        Monitor.Enter(_operationGate);
        return new OperationScope(_operationGate);
    }

    private sealed class OperationScope : IDisposable
    {
        private readonly object _gate;
        private bool _released;

        public OperationScope(object gate) => _gate = gate;

        public void Dispose()
        {
            if (_released)
            {
                return;
            }

            _released = true;
            Monitor.Exit(_gate);
        }
    }

    private List<ModRecord> SnapshotMods()
    {
        lock (_stateGate)
        {
            return Store.Current.Mods.ToList();
        }
    }

    private void EnsureRecovered()
    {
        if (RecoveryWarning is null)
        {
            return;
        }

        // A retry may succeed after a transient lock or removable-drive problem disappeared.
        RecoverPendingOperation();
        if (RecoveryWarning is not null)
        {
            throw new InvalidOperationException(
                "An interrupted deployment needs attention before new changes can be made: " + RecoveryWarning);
        }
    }

    /// <summary>
    /// Resolves the single pending journal left by a crash or forced shutdown. Installs that had
    /// not committed are rolled back; uninstalls and config writes are completed because their
    /// intent is already durable. The journal remains in place if recovery cannot finish safely.
    /// </summary>
    private void RecoverPendingOperation()
    {
        DeploymentJournal? journal;
        try
        {
            journal = _journal.Read();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            RecoveryWarning =
                "The deployment journal is currently locked or unavailable. Close other file tools and restart to finish recovery: "
                + exception.Message;
            AppLog.Error(RecoveryWarning, exception);
            return;
        }

        if (journal is null)
        {
            RecoveryWarning = null;
            return;
        }

        try
        {
            switch (journal.Operation)
            {
                case DeploymentJournal.InstallOperation:
                    RecoverInstall(journal);
                    break;
                case DeploymentJournal.UninstallOperation:
                    RecoverUninstall(journal);
                    break;
                case DeploymentJournal.ConfigOperation:
                    RecoverConfig(journal);
                    break;
                default:
                    throw new InvalidOperationException(
                        $"The deployment journal contains an unknown operation: {journal.Operation}");
            }

            _journal.Delete();
            RecoveryWarning = null;
            if (!string.IsNullOrWhiteSpace(journal.ModName))
            {
                AppLog.Warn(
                    $"Recovered interrupted {journal.Operation} for '{journal.ModName}' ({journal.Steps.Count} file(s)).");
                AddHistory("Recovered", journal.ModName, $"{journal.Operation} · {journal.Steps.Count} file(s)");
            }
        }
        catch (Exception exception)
        {
            RecoveryWarning =
                $"Interrupted {journal.Operation} for '{journal.ModName}' could not be recovered automatically: {exception.Message}";
            AppLog.Error(RecoveryWarning, exception);
        }
    }

    private void RecoverInstall(DeploymentJournal journal)
    {
        if (journal.Phase == DeploymentJournal.CommitPhase)
        {
            ModRecord? mod = Get(journal.ModId);
            if (mod is null)
            {
                throw new InvalidOperationException(
                    "The deployed files were committed, but the package record is missing from the library.");
            }

            foreach (var step in journal.Steps)
            {
                string destination = Paths.ResolveWithin(journal.TargetDir, step.Destination);
                if (!File.Exists(destination))
                {
                    throw new InvalidOperationException(
                        $"The committed deployment is missing {step.Destination}.");
                }

                if (!string.IsNullOrWhiteSpace(step.InstalledHash)
                    && !string.Equals(
                        Hashing.Sha256File(destination),
                        step.InstalledHash,
                        StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(
                        $"{step.Destination} changed after the interrupted deployment.");
                }
            }

            mod.Installed = true;
            mod.InstalledFiles = journal.Steps;
            mod.InstallId = journal.InstallId;
            mod.LoadedAt ??= DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
            Store.Save();
            return;
        }

        var failures = Rollback(journal.Steps, journal.TargetDir);
        if (failures.Count > 0)
        {
            throw new InvalidOperationException(string.Join(" ", failures));
        }
    }

    private void RecoverUninstall(DeploymentJournal journal)
    {
        if (journal.Phase == DeploymentJournal.PreparePhase)
        {
            var (_, failures) = RestoreUninstallSteps(journal.Steps, journal.TargetDir);
            if (failures.Count > 0)
            {
                throw new InvalidOperationException(string.Join(" ", failures));
            }
        }

        ModRecord? mod = Get(journal.ModId);
        if (mod is null)
        {
            return;
        }

        mod.Installed = false;
        mod.InstalledFiles = new List<InstalledFile>();
        mod.InstallId = null;
        mod.LoadedAt = null;
        Store.Save();
    }

    private void RecoverConfig(DeploymentJournal journal)
    {
        if (journal.Phase == DeploymentJournal.PreparePhase)
        {
            var failures = Rollback(journal.Steps, journal.TargetDir);
            if (failures.Count > 0)
            {
                throw new InvalidOperationException(string.Join(" ", failures));
            }

            return;
        }

        ModRecord? mod = Get(journal.ModId);
        if (mod is null)
        {
            throw new InvalidOperationException(
                "The config file was committed, but the package record is missing from the library.");
        }

        foreach (var step in journal.Steps)
        {
            string destination = Paths.ResolveWithin(journal.TargetDir, step.Destination);
            if (!File.Exists(destination))
            {
                throw new InvalidOperationException(
                    $"The committed config file is missing: {step.Destination}.");
            }

            if (!string.IsNullOrWhiteSpace(step.InstalledHash)
                && !string.Equals(
                    Hashing.Sha256File(destination),
                    step.InstalledHash,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"The committed config file changed after the interrupted write: {step.Destination}.");
            }

            mod.InstalledFiles.RemoveAll(file => file.Destination == step.Destination);
            mod.InstalledFiles.Add(step);
        }

        Store.Save();
    }

    // ------------------------------------------------------------------ library
    public List<ModRecord> ListMods() =>
        SnapshotMods()
            .OrderBy(mod => mod.Priority)
            .ThenBy(mod => mod.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

    public List<ModRecord> LoadOrder() =>
        ListMods().Where(mod => mod.Enabled).ToList();

    public ModRecord? Get(string? id) =>
        id is null ? null : SnapshotMods().FirstOrDefault(mod => mod.Id == id);

    public (ModRecord Record, PackageContents Info) ImportPackage(string packagePath)
    {
        using var operation = EnterOperation();
        EnsureRecovered();

        if (string.IsNullOrWhiteSpace(packagePath))
        {
            throw new PackageException("Choose a package file or folder first.");
        }

        bool folder = Directory.Exists(packagePath);
        if (!folder && !File.Exists(packagePath))
        {
            throw new PackageException("Package file or folder not found.");
        }

        bool zip = !folder && Packages.IsZip(packagePath);
        var info = new PackageContents();
        JsonObject? manifest = null;

        if (folder)
        {
            foreach (string relative in Packages.SafeFolderFiles(packagePath))
            {
                info.Entries.Add(relative);
            }

            string? manifestPath = Packages.FindManifestFile(packagePath);
            if (manifestPath is not null)
            {
                if (Paths.IsReparsePoint(manifestPath))
                {
                    throw new PackageException("The package manifest cannot be a symbolic link or junction.");
                }

                manifest = Packages.ReadManifestFile(manifestPath);
                info.Manifest = manifest;
            }
        }
        else if (zip)
        {
            info = Packages.Inspect(packagePath);
            manifest = info.Manifest;
        }
        else
        {
            // A single loose file is a valid one-file package: texture, model, script, archive — anything.
            info.Entries.Add(Path.GetFileName(packagePath));
        }

        // Validate the complete destination plan before the package is copied into the library,
        // so a rejected import cannot leave an orphaned archive or a half-registered record.
        List<PackageFile> plannedFiles;
        if (manifest is not null)
        {
            plannedFiles = Packages.Files(manifest);
        }
        else if (zip)
        {
            plannedFiles = Packages.AutoFiles(packagePath);
        }
        else if (folder)
        {
            plannedFiles = info.Entries
                .Select(relative => new PackageFile(relative, relative))
                .ToList();
            Packages.ValidateDestinations(plannedFiles);
        }
        else
        {
            string fileName = Path.GetFileName(packagePath);
            plannedFiles = new List<PackageFile>
            {
                new(Paths.SafeRelative(fileName), Paths.SafeRelative(fileName)),
            };
        }

        string requestedId = Text(manifest?["id"]) ?? "";
        string id = new(requestedId.Where(character => char.IsLetterOrDigit(character) || character is '-' or '_').Take(64).ToArray());
        if (id.Length == 0)
        {
            id = Path.GetFileNameWithoutExtension(packagePath);
            id = new(id.Where(character => char.IsLetterOrDigit(character) || character is '-' or '_').Take(64).ToArray());
        }

        if (id.Length == 0)
        {
            id = Guid.NewGuid().ToString("N")[..12];
        }

        // Resolve collisions before storing anything: overwriting an existing package
        // would silently corrupt the mod that already owns that id.
        var existing = SnapshotMods();
        if (existing.Any(mod => mod.Id == id))
        {
            id = $"{id}-{Guid.NewGuid().ToString("N")[..6]}";
        }

        string destination = Path.Combine(ModsDir, id + ".zip");
        while (File.Exists(destination))
        {
            id = $"{id}-{Guid.NewGuid().ToString("N")[..4]}";
            destination = Path.Combine(ModsDir, id + ".zip");
        }

        if (folder)
        {
            Packages.PackDirectory(packagePath, destination);
        }
        else if (zip)
        {
            File.Copy(packagePath, destination, true);
        }
        else
        {
            Packages.WrapFile(packagePath, destination);
        }

        List<PackageFile> files = plannedFiles;
        bool autoDeploy = manifest is null && files.Count > 0;

        var record = new ModRecord
        {
            Id = id,
            Name = Text(manifest?["name"]) ?? Path.GetFileNameWithoutExtension(packagePath.TrimEnd(Path.DirectorySeparatorChar)),
            Version = Text(manifest?["version"]) ?? "Unknown",
            Author = Text(manifest?["author"]) ?? "Unknown",
            Description = Text(manifest?["description"]) ??
                (autoDeploy
                    ? "Manifest-free package. Every safe file is deployed to the same relative path in the target folder."
                    : "No description provided."),
            Package = Path.GetFileName(destination),
            Enabled = true,
            Installed = false,
            HasManifest = manifest is not null,
            AutoDeploy = autoDeploy,
            AutoFiles = autoDeploy
                ? files.Select(file => new AutoFile { Source = file.Source, Destination = file.Destination }).ToList()
                : new List<AutoFile>(),
            Kind = ModClassifier.Classify(manifest, info.Entries),
            FileCount = info.Entries.Count,
            Sha256 = Hashing.Sha256File(destination),
            Priority = existing.Count == 0 ? 0 : existing.Max(mod => mod.Priority) + 1,
        };

        lock (_stateGate)
        {
            Store.Current.Mods.Add(record);
            try
            {
                Store.Save();
            }
            catch
            {
                // Do not leave an in-memory record or an orphaned archive behind a failed save.
                Store.Current.Mods.Remove(record);
                Cache.Invalidate(destination);
                TryDelete(destination);
                throw;
            }
        }

        AppLog.Info($"Imported '{record.Name}' as {record.Id} ({info.Entries.Count} entries).");
        AddHistory("Imported", record.Name, $"{info.Entries.Count} item(s) · {record.Kind}");
        return (record, info);
    }

    public void Toggle(ModRecord mod, bool enabled)
    {
        using var operation = EnterOperation();
        mod.Enabled = enabled;
        Store.Save();
        AddHistory(enabled ? "Enabled" : "Disabled", mod.Name);
    }

    public void Rename(ModRecord mod, string name)
    {
        using var operation = EnterOperation();
        name = name.Trim();
        if (name.Length == 0)
        {
            throw new InvalidOperationException("Mod name cannot be empty.");
        }

        mod.Name = name;
        Store.Save();
        AddHistory("Renamed", mod.Name);
    }

    public bool Move(ModRecord mod, int delta)
    {
        using var operation = EnterOperation();
        var mods = ListMods();
        int index = mods.FindIndex(candidate => candidate.Id == mod.Id);
        if (index < 0)
        {
            throw new InvalidOperationException("Mod not found.");
        }

        int target = index + delta;
        if (target < 0 || target >= mods.Count)
        {
            return false;
        }

        (mods[index], mods[target]) = (mods[target], mods[index]);
        for (int position = 0; position < mods.Count; position++)
        {
            mods[position].Priority = position;
        }

        Store.Save();
        AddHistory("Load order changed", mods[target].Name, $"position {target + 1}");
        return true;
    }

    public bool MoveTo(ModRecord mod, int target)
    {
        using var operation = EnterOperation();
        var mods = ListMods();
        int index = mods.FindIndex(candidate => candidate.Id == mod.Id);
        if (index < 0)
        {
            throw new InvalidOperationException("Mod not found.");
        }

        target = Math.Clamp(target, 0, mods.Count - 1);
        if (target == index)
        {
            return false;
        }

        mods.RemoveAt(index);
        mods.Insert(target, mod);
        for (int position = 0; position < mods.Count; position++)
        {
            mods[position].Priority = position;
        }

        Store.Save();
        AddHistory("Load order changed", mod.Name, $"position {target + 1}");
        return true;
    }

    public void Remove(ModRecord mod)
    {
        using var operation = EnterOperation();
        EnsureRecovered();

        if (mod.Installed)
        {
            throw new InvalidOperationException("Unload this mod before removing it from the library.");
        }

        string package = Path.Combine(ModsDir, mod.Package);
        lock (_stateGate)
        {
            Store.Current.Mods.RemoveAll(candidate => candidate.Id == mod.Id);
            Store.Save();
        }

        Cache.Invalidate(package);
        try
        {
            if (File.Exists(package))
            {
                File.Delete(package);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // The record is already gone; an orphaned archive is harmless and can be cleaned up manually.
            AppLog.Warn($"Could not delete package archive '{package}': {exception.Message}");
        }

        AppLog.Info($"Removed '{mod.Name}' from the library.");
        AddHistory("Removed", mod.Name);
    }

    public List<Conflict> Conflicts()
    {
        var owners = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var conflicts = new List<Conflict>();
        foreach (var mod in SnapshotMods().Where(mod => mod.Enabled && (mod.HasManifest || mod.AutoDeploy)))
        {
            JsonObject? manifest = ManifestFor(mod);
            if (manifest is null)
            {
                continue;
            }

            List<PackageFile> files;
            try
            {
                files = FilesFor(mod);
            }
            catch (PackageException)
            {
                // A damaged or manually edited package cannot claim destinations.
                continue;
            }

            if (files.Count == 0)
            {
                continue;
            }

            foreach (var file in files)
            {
                if (owners.TryGetValue(file.Destination, out string? owner))
                {
                    conflicts.Add(new Conflict(file.Destination, owner, mod.Name));
                }
                else
                {
                    owners[file.Destination] = mod.Name;
                }
            }
        }

        return conflicts;
    }

    // ------------------------------------------------------------------ packages
    public JsonObject? ManifestFor(ModRecord mod)
    {
        string package = Path.Combine(ModsDir, mod.Package);
        if (!File.Exists(package))
        {
            return null;
        }

        return Cache.Manifest(package);
    }

    public List<ConfigField> SchemaFor(ModRecord mod)
    {
        string package = Path.Combine(ModsDir, mod.Package);
        return File.Exists(package) ? Cache.Schema(package) : new List<ConfigField>();
    }

    /// <summary>Files declared by the manifest, or auto-detected pairs for manifest-free packages.</summary>
    public List<PackageFile> FilesFor(ModRecord mod)
    {
        var manifest = ManifestFor(mod);
        if (manifest is not null)
        {
            return Packages.Files(manifest);
        }

        if (!mod.AutoDeploy)
        {
            return new List<PackageFile>();
        }

        var files = new List<PackageFile>();
        foreach (var file in mod.AutoFiles)
        {
            files.Add(new PackageFile(Paths.SafeRelative(file.Source), Paths.SafeRelative(file.Destination)));
        }

        Packages.ValidateDestinations(files);
        return files;
    }

    public string DeployableSummary(ModRecord mod)
    {
        int count;
        try
        {
            count = FilesFor(mod).Count;
        }
        catch (PackageException)
        {
            return "Invalid file mapping";
        }

        if (mod.HasManifest)
        {
            return $"{count} declared file(s)";
        }

        return mod.AutoDeploy ? $"{count} auto-detected file(s)" : "Catalogued only";
    }

    public Dictionary<string, string> ConfigValues(ModRecord mod)
    {
        var schema = SchemaFor(mod);
        var values = ModConfig.Resolve(schema, mod.Config);
        var declared = schema.Select(field => field.Key).ToHashSet(StringComparer.Ordinal);
        foreach (var pair in mod.Config)
        {
            if (!declared.Contains(pair.Key))
            {
                values[pair.Key] = pair.Value ?? "";
            }
        }

        return values;
    }

    public void SaveConfig(ModRecord mod, IReadOnlyDictionary<string, string> values)
    {
        using var operation = EnterOperation();
        var schema = SchemaFor(mod);
        var declared = schema.ToDictionary(field => field.Key, StringComparer.Ordinal);
        var cleaned = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var pair in values)
        {
            string key = pair.Key.Trim();
            if (key.Length == 0 || key.Any(char.IsControl))
            {
                continue;
            }

            cleaned[key] = declared.TryGetValue(key, out ConfigField? field)
                ? ModConfig.Coerce(field, pair.Value)
                : pair.Value ?? "";
        }

        var stored = ModConfig.Resolve(schema, cleaned);
        foreach (var pair in cleaned)
        {
            if (!declared.ContainsKey(pair.Key))
            {
                stored[pair.Key] = pair.Value;
            }
        }

        mod.Config = stored;
        Store.Save();
        AddHistory("Variables saved", mod.Name, $"{stored.Count} variable(s)");
    }

    public void ResetConfig(ModRecord mod)
    {
        using var operation = EnterOperation();
        mod.Config = new Dictionary<string, string>();
        Store.Save();
        AddHistory("Variables reset", mod.Name);
    }

    // ---------------------------------------------------------------- deployment
    public int Install(ModRecord mod, string targetDir)
    {
        using var operation = EnterOperation();
        EnsureRecovered();

        string root = Path.GetFullPath(targetDir);
        if (!Directory.Exists(root))
        {
            throw new InvalidOperationException("Select a valid target folder first.");
        }

        if (mod.Installed)
        {
            throw new InvalidOperationException("This package is already deployed. Sync it again to refresh its values.");
        }

        if (!mod.Enabled)
        {
            throw new InvalidOperationException("This package is disabled.");
        }

        JsonObject? manifest = ManifestFor(mod);
        if (manifest is null && !mod.AutoDeploy)
        {
            throw new InvalidOperationException("This package has no readable manifest and auto-deploy is off.");
        }

        var files = FilesFor(mod);
        if (files.Count == 0)
        {
            throw new InvalidOperationException("This package contains no deployable files.");
        }

        string package = Path.Combine(ModsDir, mod.Package);
        VerifyPackageIntegrity(mod, package);

        string installId = Guid.NewGuid().ToString("N");
        string backupRoot = Path.Combine(BackupsDir, installId);
        var installed = new List<InstalledFile>();
        var journal = new DeploymentJournal
        {
            Operation = DeploymentJournal.InstallOperation,
            Phase = DeploymentJournal.PreparePhase,
            ModId = mod.Id,
            ModName = mod.Name,
            TargetDir = root,
            InstallId = installId,
            StartedAt = DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
        };

        AppLog.Info($"Deploy started for '{mod.Name}' ({mod.Id}) into {root}.");
        _journal.Write(journal);

        try
        {
            using (var archive = Packages.OpenArchive(package))
            {
                var entries = new Dictionary<string, System.IO.Compression.ZipArchiveEntry>(
                    StringComparer.OrdinalIgnoreCase);
                foreach (var entry in archive.Entries)
                {
                    if (entry.Name.Length == 0)
                    {
                        continue;
                    }

                    entries[entry.FullName.Replace('\\', '/')] = entry;
                }

                // Preflight the whole plan before writing anything. A missing source should abort
                // before a single destination has been replaced.
                foreach (var file in files)
                {
                    if (!entries.ContainsKey(file.Source))
                    {
                        throw new PackageException($"Package is missing source file: {file.Source}");
                    }

                    _ = Paths.ResolveWithin(root, file.Destination);
                    _ = Paths.ResolveWithin(backupRoot, file.Destination);
                }

                foreach (var file in files)
                {
                    var entry = entries[file.Source];
                    string destination = Paths.ResolveWithin(root, file.Destination);
                    Directory.CreateDirectory(Path.GetDirectoryName(destination)!);

                    string? backup = null;
                    string? backupHash = null;
                    if (File.Exists(destination))
                    {
                        string backupPath = Paths.ResolveWithin(backupRoot, file.Destination);
                        Directory.CreateDirectory(Path.GetDirectoryName(backupPath)!);
                        string originalHash = Hashing.Sha256File(destination);
                        File.Copy(destination, backupPath, true);
                        backup = Path.GetRelativePath(BackupsDir, backupPath).Replace('\\', '/');
                        backupHash = Hashing.Sha256File(backupPath);
                        if (!string.Equals(originalHash, backupHash, StringComparison.OrdinalIgnoreCase))
                        {
                            throw new IOException($"Backup verification failed for {file.Destination}.");
                        }
                    }

                    // Register the file and persist the journal before the destructive move. If the
                    // process dies after this point, startup recovery can still restore the original.
                    var record = new InstalledFile
                    {
                        Destination = file.Destination,
                        Backup = backup,
                        BackupHash = backupHash,
                    };
                    installed.Add(record);
                    journal.Steps.Add(record);
                    _journal.Write(journal);

                    string temp = destination + ".vmm-tmp";
                    try
                    {
                        // Revalidate right before each write: shortens the window for a link swap
                        // between the initial check and the actual filesystem operation.
                        destination = Paths.ResolveWithin(root, file.Destination);
                        Packages.ExtractEntryToFile(entry, temp);

                        destination = Paths.ResolveWithin(root, file.Destination);
                        File.Move(temp, destination, true);
                        record.InstalledHash = Hashing.Sha256File(destination);
                    }
                    finally
                    {
                        TryDelete(temp);
                    }
                }
            }

            if (manifest is not null)
            {
                WriteConfig(
                    mod,
                    manifest,
                    root,
                    backupRoot,
                    backupExisting: true,
                    transaction: installed,
                    journal: journal);
            }

            journal.Phase = DeploymentJournal.CommitPhase;
            _journal.Write(journal);
        }
        catch (Exception exception)
        {
            AppLog.Error($"Deploy failed for '{mod.Name}'.", exception);
            var failures = Rollback(installed, root);
            if (failures.Count > 0)
            {
                RecoveryWarning =
                    $"Deployment failed and {failures.Count} file(s) could not be restored: {string.Join(" ", failures)}";
                throw new InvalidOperationException(RecoveryWarning, exception);
            }

            _journal.Delete();
            throw;
        }

        mod.Installed = true;
        mod.InstalledFiles = installed;
        mod.InstallId = installId;
        mod.LoadedAt = DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
        try
        {
            Store.Save();
        }
        catch (Exception exception)
        {
            RecoveryWarning =
                "Files were deployed, but the library record could not be saved. Restart VI Mod Manager to finish recovery.";
            AppLog.Error($"Deploy commit failed for '{mod.Name}'.", exception);
            throw new InvalidOperationException(RecoveryWarning, exception);
        }

        _journal.Delete();
        AppLog.Info($"Deploy complete for '{mod.Name}': {installed.Count} file(s).");
        AddHistory("Deployed", mod.Name, $"{installed.Count} file(s)");
        return installed.Count;
    }

    private static void VerifyPackageIntegrity(ModRecord mod, string package)
    {
        if (!File.Exists(package))
        {
            throw new PackageException("The package archive is missing from the library folder.");
        }

        if (string.IsNullOrWhiteSpace(mod.Sha256))
        {
            // Records created by older builds did not always store a package hash.
            return;
        }

        string actual = Hashing.Sha256File(package);
        if (!string.Equals(actual, mod.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "The package archive changed after it was imported. Remove and re-import it before deploying.");
        }
    }

    public string ApplyConfig(ModRecord mod, string targetDir)
    {
        using var operation = EnterOperation();
        EnsureRecovered();

        string root = Path.GetFullPath(targetDir);
        if (!Directory.Exists(root))
        {
            throw new InvalidOperationException("Select a valid target folder first.");
        }

        JsonObject? manifest = ManifestFor(mod)
            ?? throw new InvalidOperationException("This package has no readable manifest.");
        if (ModConfig.ConfigDestination(manifest) is null)
        {
            throw new InvalidOperationException("This package does not declare a config file in its manifest.");
        }

        mod.InstallId ??= Guid.NewGuid().ToString("N");
        string backupRoot = Path.Combine(BackupsDir, mod.InstallId);
        var existing = mod.InstalledFiles.FirstOrDefault(file => file.Kind == "config");
        var journal = new DeploymentJournal
        {
            Operation = DeploymentJournal.ConfigOperation,
            Phase = DeploymentJournal.PreparePhase,
            ModId = mod.Id,
            ModName = mod.Name,
            TargetDir = root,
            InstallId = mod.InstallId,
            StartedAt = DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
        };
        _journal.Write(journal);

        InstalledFile entry;
        try
        {
            entry = WriteConfig(
                mod,
                manifest,
                root,
                backupRoot,
                backupExisting: existing is null,
                journal: journal,
                existing: existing)
                ?? throw new InvalidOperationException("The config file could not be written.");

            journal.Phase = DeploymentJournal.CommitPhase;
            _journal.Write(journal);
        }
        catch (Exception exception)
        {
            AppLog.Error($"Config write failed for '{mod.Name}'.", exception);
            if (journal.Phase == DeploymentJournal.PreparePhase)
            {
                var failures = Rollback(journal.Steps, root);
                if (failures.Count > 0)
                {
                    RecoveryWarning =
                        $"A config write failed and {failures.Count} file(s) could not be restored: {string.Join(" ", failures)}";
                    throw new InvalidOperationException(RecoveryWarning, exception);
                }

                _journal.Delete();
            }

            throw;
        }

        mod.InstalledFiles.RemoveAll(file => file.Destination == entry.Destination);
        mod.InstalledFiles.Add(entry);
        try
        {
            Store.Save();
        }
        catch (Exception exception)
        {
            RecoveryWarning =
                "The config file was written, but the library record could not be saved. Restart VI Mod Manager to finish recovery.";
            AppLog.Error($"Config commit failed for '{mod.Name}'.", exception);
            throw new InvalidOperationException(RecoveryWarning, exception);
        }

        _journal.Delete();
        AppLog.Info($"Config written for '{mod.Name}' at {entry.Destination}.");
        AddHistory("Values written", mod.Name, entry.Destination);
        return entry.Destination;
    }

    public LoadOutcome LoadMod(ModRecord mod, string targetDir)
    {
        using var operation = EnterOperation();
        EnsureRecovered();

        if (!mod.HasManifest && !mod.AutoDeploy)
        {
            throw new InvalidOperationException("This package has no manifest and auto-deploy is off.");
        }

        if (mod.Installed)
        {
            JsonObject? manifest = ManifestFor(mod);
            if (manifest is not null && ModConfig.ConfigDestination(manifest) is not null)
            {
                return new LoadOutcome { Status = "configured", ConfigPath = ApplyConfig(mod, targetDir) };
            }

            return new LoadOutcome { Status = "loaded" };
        }

        Install(mod, targetDir);
        JsonObject? installedManifest = ManifestFor(mod);
        return new LoadOutcome
        {
            Status = "installed",
            ConfigPath = installedManifest is null ? null : ModConfig.ConfigDestination(installedManifest),
        };
    }

    public LoadSummary LoadAll(string targetDir)
    {
        using var operation = EnterOperation();
        EnsureRecovered();

        if (!Directory.Exists(targetDir))
        {
            throw new InvalidOperationException("Select a valid target folder first.");
        }

        var summary = new LoadSummary();
        foreach (var mod in LoadOrder())
        {
            if (RecoveryWarning is not null)
            {
                summary.Failed.Add((mod.Name, RecoveryWarning));
                break;
            }

            if (!mod.HasManifest && !mod.AutoDeploy)
            {
                summary.Skipped.Add((mod.Name, "No manifest and auto-deploy is off"));
                continue;
            }

            try
            {
                LoadMod(mod, targetDir);
                summary.Loaded.Add(mod.Name);
            }
            catch (Exception exception) when (
                exception is InvalidOperationException
                or PackageException
                or IOException
                or UnauthorizedAccessException
                or ArgumentException
                or NotSupportedException
                or System.Security.SecurityException)
            {
                summary.Failed.Add((mod.Name, exception.Message));
            }
        }

        AddHistory("Deployed package set", $"{summary.Loaded.Count} deployed, {summary.Failed.Count} failed");
        return summary;
    }

    public int Uninstall(ModRecord mod, string targetDir)
    {
        using var operation = EnterOperation();
        EnsureRecovered();

        string root = Path.GetFullPath(targetDir);
        if (!mod.Installed)
        {
            throw new InvalidOperationException("This package is not deployed.");
        }

        // Preflight every file before touching anything. A later failure must not leave the
        // target half-restored, and a missing/damaged backup must never be skipped silently.
        var problems = PreflightUninstall(mod.InstalledFiles, root);
        if (problems.Count > 0)
        {
            throw new InvalidOperationException(
                "Refusing to unload because the target or backups are not in a known state: "
                + string.Join(" ", problems));
        }

        var journal = new DeploymentJournal
        {
            Operation = DeploymentJournal.UninstallOperation,
            Phase = DeploymentJournal.PreparePhase,
            ModId = mod.Id,
            ModName = mod.Name,
            TargetDir = root,
            InstallId = mod.InstallId ?? "",
            StartedAt = DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
            Steps = mod.InstalledFiles.Select(CloneStep).ToList(),
        };
        _journal.Write(journal);
        AppLog.Info($"Unload started for '{mod.Name}' ({mod.Id}) from {root}.");

        var (restored, failures) = RestoreUninstallSteps(journal.Steps, root);
        if (failures.Count > 0)
        {
            RecoveryWarning =
                $"Unload was interrupted and {failures.Count} file(s) could not be restored: {string.Join(" ", failures)}";
            AppLog.Error(RecoveryWarning);
            throw new InvalidOperationException(RecoveryWarning);
        }

        try
        {
            journal.Phase = DeploymentJournal.CommitPhase;
            _journal.Write(journal);
        }
        catch (Exception exception)
        {
            RecoveryWarning =
                "Files were restored, but the operation record could not be updated. Restart VI Mod Manager to finish recovery.";
            AppLog.Error($"Unload commit failed for '{mod.Name}'.", exception);
            throw new InvalidOperationException(RecoveryWarning, exception);
        }

        mod.Installed = false;
        mod.InstalledFiles = new List<InstalledFile>();
        mod.InstallId = null;
        mod.LoadedAt = null;
        try
        {
            Store.Save();
        }
        catch (Exception exception)
        {
            RecoveryWarning =
                "Files were restored, but the library record could not be saved. Restart VI Mod Manager to finish recovery.";
            AppLog.Error($"Unload record save failed for '{mod.Name}'.", exception);
            throw new InvalidOperationException(RecoveryWarning, exception);
        }

        _journal.Delete();
        AppLog.Info($"Unload complete for '{mod.Name}': {restored} original file(s) restored.");
        AddHistory("Unloaded", mod.Name, $"{restored} original file(s) restored");
        return restored;
    }

    private static InstalledFile CloneStep(InstalledFile file) => new()
    {
        Destination = file.Destination,
        Backup = file.Backup,
        BackupHash = file.BackupHash,
        InstalledHash = file.InstalledHash,
        Kind = file.Kind,
    };

    /// <summary>
    /// Verifies every deployed file and backup before an unload starts. Files that were already
    /// restored by a previous interrupted attempt are treated as valid, so a retry is safe.
    /// </summary>
    private List<string> PreflightUninstall(IEnumerable<InstalledFile> files, string root)
    {
        var problems = new List<string>();
        foreach (var file in files)
        {
            try
            {
                string destination = Paths.ResolveWithin(root, file.Destination);
                string? backupPath = null;
                if (!string.IsNullOrWhiteSpace(file.Backup))
                {
                    backupPath = Paths.ResolveWithin(BackupsDir, file.Backup!);
                    if (!File.Exists(backupPath))
                    {
                        problems.Add($"{file.Destination}: original backup is missing.");
                        continue;
                    }

                    if (!string.IsNullOrWhiteSpace(file.BackupHash)
                        && !string.Equals(
                            Hashing.Sha256File(backupPath),
                            file.BackupHash,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        problems.Add($"{file.Destination}: original backup is damaged.");
                        continue;
                    }
                }

                if (!File.Exists(destination))
                {
                    continue;
                }

                string actual = Hashing.Sha256File(destination);
                bool alreadyRestored = !string.IsNullOrWhiteSpace(file.BackupHash)
                    && string.Equals(actual, file.BackupHash, StringComparison.OrdinalIgnoreCase);
                if (!alreadyRestored
                    && !string.IsNullOrWhiteSpace(file.InstalledHash)
                    && !string.Equals(actual, file.InstalledHash, StringComparison.OrdinalIgnoreCase))
                {
                    problems.Add($"{file.Destination}: file changed since deployment.");
                }
            }
            catch (Exception exception)
            {
                problems.Add($"{file.Destination}: {exception.Message}");
            }
        }

        return problems;
    }

    /// <summary>Restores originals or deletes files that did not exist before deployment.</summary>
    private (int Restored, List<string> Failures) RestoreUninstallSteps(
        IEnumerable<InstalledFile> files,
        string root)
    {
        int restored = 0;
        var failures = new List<string>();
        foreach (var file in files.Reverse())
        {
            try
            {
                string destination = Paths.ResolveWithin(root, file.Destination);
                if (!string.IsNullOrWhiteSpace(file.Backup))
                {
                    string backupPath = Paths.ResolveWithin(BackupsDir, file.Backup!);
                    if (!File.Exists(backupPath))
                    {
                        failures.Add($"{file.Destination}: backup is missing.");
                        continue;
                    }

                    if (!string.IsNullOrWhiteSpace(file.BackupHash)
                        && !string.Equals(
                            Hashing.Sha256File(backupPath),
                            file.BackupHash,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        failures.Add($"{file.Destination}: backup is damaged.");
                        continue;
                    }

                    if (File.Exists(destination)
                        && !string.IsNullOrWhiteSpace(file.BackupHash)
                        && string.Equals(
                            Hashing.Sha256File(destination),
                            file.BackupHash,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        // A previous attempt already restored this file.
                        continue;
                    }

                    Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                    File.Copy(backupPath, destination, true);
                    restored++;
                }
                else if (File.Exists(destination))
                {
                    File.Delete(destination);
                }
            }
            catch (Exception exception)
            {
                failures.Add($"{file.Destination}: {exception.Message}");
            }
        }

        return (restored, failures);
    }

    /// <summary>
    /// Restores a failed install so a package cannot leave half-deployed files behind. Failures
    /// are returned instead of swallowed, because an incomplete rollback changes what the user
    /// must be told (and may require manual repair).
    /// </summary>
    private List<string> Rollback(IEnumerable<InstalledFile> installed, string targetDir)
    {
        var failures = new List<string>();
        foreach (var file in installed.Reverse())
        {
            try
            {
                string destination = Paths.ResolveWithin(targetDir, file.Destination);
                if (!string.IsNullOrWhiteSpace(file.Backup))
                {
                    string backup = Paths.ResolveWithin(BackupsDir, file.Backup!);
                    if (!File.Exists(backup))
                    {
                        failures.Add($"{file.Destination}: backup is missing.");
                        continue;
                    }

                    if (!string.IsNullOrWhiteSpace(file.BackupHash)
                        && !string.Equals(
                            Hashing.Sha256File(backup),
                            file.BackupHash,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        failures.Add($"{file.Destination}: backup is damaged.");
                        continue;
                    }

                    Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                    File.Copy(backup, destination, true);
                }
                else if (File.Exists(destination))
                {
                    File.Delete(destination);
                }
            }
            catch (Exception exception)
            {
                // Rollback is best-effort: record the failure and keep restoring the rest.
                failures.Add($"{file.Destination}: {exception.Message}");
            }
        }

        return failures;
    }

    private InstalledFile? WriteConfig(
        ModRecord mod,
        JsonObject manifest,
        string targetDir,
        string backupRoot,
        bool backupExisting,
        List<InstalledFile>? transaction = null,
        DeploymentJournal? journal = null,
        InstalledFile? existing = null)
    {
        string? relative = ModConfig.ConfigDestination(manifest);
        if (relative is null)
        {
            return null;
        }

        string safeRelative = Paths.SafeRelative(relative);
        string destination = Paths.ResolveWithin(targetDir, safeRelative);
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);

        string? backup = null;
        string? backupHash = null;
        if (backupExisting && File.Exists(destination))
        {
            string backupPath = Paths.ResolveWithin(backupRoot, safeRelative);
            Directory.CreateDirectory(Path.GetDirectoryName(backupPath)!);
            string originalHash = Hashing.Sha256File(destination);
            File.Copy(destination, backupPath, true);
            backup = Path.GetRelativePath(BackupsDir, backupPath).Replace('\\', '/');
            backupHash = Hashing.Sha256File(backupPath);
            if (!string.Equals(originalHash, backupHash, StringComparison.OrdinalIgnoreCase))
            {
                throw new IOException($"Backup verification failed for {safeRelative}.");
            }
        }
        else if (existing?.Backup is not null)
        {
            // Re-syncing values must keep the original pre-deployment backup.
            backup = existing.Backup;
            backupHash = existing.BackupHash;
        }

        var schema = ModConfig.ParseSchema(manifest["settings"] ?? manifest["variables"]);
        var values = ModConfig.BuildValues(schema, mod.Config);
        var entry = new InstalledFile
        {
            Destination = safeRelative,
            Backup = backup,
            BackupHash = backupHash,
            Kind = "config",
        };

        // Register before the write for the same reason as payload files: a failure after the
        // atomic move must still leave rollback with a record it can restore.
        transaction?.Add(entry);
        if (journal is not null)
        {
            journal.Steps.Add(entry);
            _journal.Write(journal);
        }

        string temp = destination + ".vmm-tmp";
        try
        {
            destination = Paths.ResolveWithin(targetDir, safeRelative);
            File.WriteAllText(temp, values.ToJsonString(ConfigJson) + Environment.NewLine);
            destination = Paths.ResolveWithin(targetDir, safeRelative);
            File.Move(temp, destination, true);
            entry.InstalledHash = Hashing.Sha256File(destination);
        }
        finally
        {
            TryDelete(temp);
        }

        return entry;
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
            // A leftover temp file is harmless compared with masking the original failure.
        }
    }

    // ------------------------------------------------------------------ profiles
    public List<ModProfile> ListProfiles() =>
        Profiles?.Current.Profiles
            .OrderByDescending(profile => profile.CreatedAt, StringComparer.Ordinal)
            .ThenBy(profile => profile.Name, StringComparer.OrdinalIgnoreCase)
            .ToList() ?? new List<ModProfile>();

    public ModProfile SaveProfile(string name)
    {
        using var operation = EnterOperation();
        if (Profiles is null)
        {
            throw new InvalidOperationException("Profiles are not available in this data root.");
        }

        name = name.Trim();
        if (name.Length == 0)
        {
            throw new InvalidOperationException("Profile name cannot be empty.");
        }

        var profile = new ModProfile
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = name,
            CreatedAt = DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
        };

        foreach (var mod in SnapshotMods())
        {
            profile.Enabled[mod.Id] = mod.Enabled;
            profile.Config[mod.Id] = new Dictionary<string, string>(mod.Config, StringComparer.Ordinal);
        }

        Profiles.Current.Profiles.RemoveAll(candidate => candidate.Id == profile.Id);
        Profiles.Current.Profiles.Insert(0, profile);
        if (Profiles.Current.Profiles.Count > 100)
        {
            Profiles.Current.Profiles.RemoveRange(100, Profiles.Current.Profiles.Count - 100);
        }

        Profiles.Save();
        AddHistory("Profile saved", name, $"{profile.Enabled.Count} mod(s)");
        return profile;
    }

    public int ApplyProfile(ModProfile profile, string? targetDir = null)
    {
        using var operation = EnterOperation();
        EnsureRecovered();

        int applied = 0;
        foreach (var mod in SnapshotMods())
        {
            if (profile.Enabled.TryGetValue(mod.Id, out bool enabled))
            {
                mod.Enabled = enabled;
            }

            if (profile.Config.TryGetValue(mod.Id, out Dictionary<string, string>? values))
            {
                mod.Config = new Dictionary<string, string>(values, StringComparer.Ordinal);
                applied++;
            }
        }

        Store.Save();

        if (!string.IsNullOrWhiteSpace(targetDir) && Directory.Exists(targetDir))
        {
            foreach (var mod in SnapshotMods().Where(mod => mod.Installed))
            {
                JsonObject? manifest = ManifestFor(mod);
                if (manifest is not null && ModConfig.ConfigDestination(manifest) is not null)
                {
                    try
                    {
                        ApplyConfig(mod, targetDir);
                    }
                    catch (Exception exception) when (exception is IOException or PackageException or UnauthorizedAccessException or InvalidOperationException)
                    {
                        // Keep applying the rest of the profile; report it in history.
                        AddHistory("Profile config skipped", mod.Name, exception.Message);
                    }
                }
            }
        }

        AddHistory("Profile applied", profile.Name, $"{applied} package configuration(s)");
        return applied;
    }

    public void RenameProfile(ModProfile profile, string name)
    {
        using var operation = EnterOperation();
        if (Profiles is null)
        {
            throw new InvalidOperationException("Profiles are not available in this data root.");
        }

        name = name.Trim();
        if (name.Length == 0)
        {
            throw new InvalidOperationException("Profile name cannot be empty.");
        }

        profile.Name = name;
        Profiles.Save();
        AddHistory("Profile renamed", name);
    }

    public void DeleteProfile(ModProfile profile)
    {
        using var operation = EnterOperation();
        if (Profiles is null)
        {
            throw new InvalidOperationException("Profiles are not available in this data root.");
        }

        Profiles.Current.Profiles.RemoveAll(candidate => candidate.Id == profile.Id);
        Profiles.Save();
        AddHistory("Profile deleted", profile.Name);
    }

    // ------------------------------------------------------------------ async IO
    public Task<(ModRecord Record, PackageContents Info)> ImportPackageAsync(string path) => Task.Run(() => ImportPackage(path));
    public Task<int> InstallAsync(ModRecord mod, string targetDir) => Task.Run(() => Install(mod, targetDir));
    public Task<LoadOutcome> LoadModAsync(ModRecord mod, string targetDir) => Task.Run(() => LoadMod(mod, targetDir));
    public Task<LoadSummary> LoadAllAsync(string targetDir) => Task.Run(() => LoadAll(targetDir));
    public Task<int> UninstallAsync(ModRecord mod, string targetDir) => Task.Run(() => Uninstall(mod, targetDir));

    public void ClearHistory()
    {
        using var operation = EnterOperation();
        History.Current.Entries.Clear();
        History.Save();
    }

    // ------------------------------------------------------------------ history
    private void AddHistory(string action, string mod, string detail = "")
    {
        var entries = History.Current.Entries;
        entries.Insert(0, new HistoryEntry
        {
            Time = DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
            Action = action,
            Mod = mod,
            Detail = detail,
        });
        if (entries.Count > 500)
        {
            entries.RemoveRange(500, entries.Count - 500);
        }

        History.Save();
    }

    private static string? Text(JsonNode? node) => node switch
    {
        null => null,
        JsonValue value => value.GetValueKind() == JsonValueKind.String ? value.GetValue<string>() : value.ToJsonString(),
        _ => null,
    };
}
