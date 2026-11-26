using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
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

    public JsonStore<ModDatabase> Store { get; }
    public JsonStore<HistoryDatabase> History { get; }
    public JsonStore<ProfileDatabase>? Profiles { get; }
    public string ModsDir { get; }
    public string BackupsDir { get; }
    public PackageCache Cache { get; } = new();

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
    }

    // ------------------------------------------------------------------ library
    public List<ModRecord> ListMods() =>
        Store.Current.Mods
            .OrderBy(mod => mod.Priority)
            .ThenBy(mod => mod.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

    public List<ModRecord> LoadOrder() =>
        ListMods().Where(mod => mod.Enabled).ToList();

    public ModRecord? Get(string? id) =>
        id is null ? null : Store.Current.Mods.FirstOrDefault(mod => mod.Id == id);

    public (ModRecord Record, PackageContents Info) ImportPackage(string packagePath)
    {
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
        if (Store.Current.Mods.Any(mod => mod.Id == id))
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

        List<PackageFile> files;
        try
        {
            files = manifest is not null ? Packages.Files(manifest) : Packages.AutoFiles(destination);
        }
        catch (PackageException)
        {
            files = new List<PackageFile>();
        }

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
            Priority = Store.Current.Mods.Count == 0 ? 0 : Store.Current.Mods.Max(mod => mod.Priority) + 1,
        };

        Store.Current.Mods.Add(record);
        Store.Save();
        AddHistory("Imported", record.Name, $"{info.Entries.Count} item(s) · {record.Kind}");
        return (record, info);
    }

    public void Toggle(ModRecord mod, bool enabled)
    {
        mod.Enabled = enabled;
        Store.Save();
        AddHistory(enabled ? "Enabled" : "Disabled", mod.Name);
    }

    public void Rename(ModRecord mod, string name)
    {
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
        if (mod.Installed)
        {
            throw new InvalidOperationException("Unload this mod before removing it from the library.");
        }

        string package = Path.Combine(ModsDir, mod.Package);
        Cache.Invalidate(package);
        if (File.Exists(package))
        {
            File.Delete(package);
        }

        Store.Current.Mods.RemoveAll(candidate => candidate.Id == mod.Id);
        Store.Save();
        AddHistory("Removed", mod.Name);
    }

    public List<Conflict> Conflicts()
    {
        var owners = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var conflicts = new List<Conflict>();
        foreach (var mod in Store.Current.Mods.Where(mod => mod.Enabled && (mod.HasManifest || mod.AutoDeploy)))
        {
            JsonObject? manifest = ManifestFor(mod);
            if (manifest is null)
            {
                continue;
            }

            List<PackageFile> files = FilesFor(mod);
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
            try
            {
                return Packages.Files(manifest);
            }
            catch (PackageException)
            {
                return new List<PackageFile>();
            }
        }

        if (!mod.AutoDeploy)
        {
            return new List<PackageFile>();
        }

        var files = new List<PackageFile>();
        foreach (var file in mod.AutoFiles)
        {
            try
            {
                files.Add(new PackageFile(Paths.SafeRelative(file.Source), Paths.SafeRelative(file.Destination)));
            }
            catch (PackageException)
            {
                // Stored auto paths were validated at import; skip anything manually corrupted.
            }
        }

        return files;
    }

    public string DeployableSummary(ModRecord mod)
    {
        int count = FilesFor(mod).Count;
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
        mod.Config = new Dictionary<string, string>();
        Store.Save();
        AddHistory("Variables reset", mod.Name);
    }

    // ---------------------------------------------------------------- deployment
    public int Install(ModRecord mod, string targetDir)
    {
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

        string installId = Guid.NewGuid().ToString("N");
        string backupRoot = Path.Combine(BackupsDir, installId);
        var installed = new List<InstalledFile>();

        string package = Path.Combine(ModsDir, mod.Package);
        try
        {
            using (var archive = ZipFile.OpenRead(package))
            {
                foreach (var file in files)
                {
                    var entry = archive.Entries.FirstOrDefault(candidate =>
                        string.Equals(candidate.FullName.Replace('\\', '/'), file.Source, StringComparison.OrdinalIgnoreCase))
                        ?? throw new PackageException($"Package is missing source file: {file.Source}");

                    string destination = Paths.ResolveWithin(root, file.Destination);
                    Directory.CreateDirectory(Path.GetDirectoryName(destination)!);

                    string? backup = null;
                    if (File.Exists(destination))
                    {
                        string backupPath = Path.Combine(backupRoot, file.Destination.Replace('/', Path.DirectorySeparatorChar));
                        Directory.CreateDirectory(Path.GetDirectoryName(backupPath)!);
                        File.Copy(destination, backupPath, true);
                        backup = Path.GetRelativePath(BackupsDir, backupPath).Replace('\\', '/');
                    }

                    string temp = destination + ".vmm-tmp";
                    entry.ExtractToFile(temp, true);
                    File.Move(temp, destination, true);
                    installed.Add(new InstalledFile
                    {
                        Destination = file.Destination,
                        Backup = backup,
                        InstalledHash = Hashing.Sha256File(destination),
                    });
                }
            }

            if (manifest is not null)
            {
                var config = WriteConfig(mod, manifest, root, backupRoot, backupExisting: true);
                if (config is not null)
                {
                    installed.Add(config);
                }
            }
        }
        catch
        {
            Rollback(installed, root);
            throw;
        }

        mod.Installed = true;
        mod.InstalledFiles = installed;
        mod.InstallId = installId;
        mod.LoadedAt = DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
        Store.Save();
        AddHistory("Deployed", mod.Name, $"{installed.Count} file(s)");
        return installed.Count;
    }

    public string ApplyConfig(ModRecord mod, string targetDir)
    {
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
        var entry = WriteConfig(mod, manifest, root, backupRoot, backupExisting: existing is null)
            ?? throw new InvalidOperationException("The config file could not be written.");
        if (existing?.Backup is not null)
        {
            entry.Backup = existing.Backup;
        }

        mod.InstalledFiles.RemoveAll(file => file.Destination == entry.Destination);
        mod.InstalledFiles.Add(entry);
        Store.Save();
        AddHistory("Values written", mod.Name, entry.Destination);
        return entry.Destination;
    }

    public LoadOutcome LoadMod(ModRecord mod, string targetDir)
    {
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
        if (!Directory.Exists(targetDir))
        {
            throw new InvalidOperationException("Select a valid target folder first.");
        }

        var summary = new LoadSummary();
        foreach (var mod in LoadOrder())
        {
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
            catch (Exception exception) when (exception is InvalidOperationException or PackageException or IOException or UnauthorizedAccessException)
            {
                summary.Failed.Add((mod.Name, exception.Message));
            }
        }

        AddHistory("Deployed package set", $"{summary.Loaded.Count} deployed, {summary.Failed.Count} failed");
        return summary;
    }

    public int Uninstall(ModRecord mod, string targetDir)
    {
        string root = Path.GetFullPath(targetDir);
        if (!mod.Installed)
        {
            throw new InvalidOperationException("This package is not deployed.");
        }

        int restored = 0;
        foreach (var file in mod.InstalledFiles)
        {
            string destination = Paths.ResolveWithin(root, file.Destination);
            if (File.Exists(destination) && Hashing.Sha256File(destination) != file.InstalledHash)
            {
                throw new InvalidOperationException(
                    $"File changed since deployment; refusing to overwrite during unload: {file.Destination}");
            }

            if (!string.IsNullOrWhiteSpace(file.Backup))
            {
                string backupPath = Paths.ResolveWithin(BackupsDir, file.Backup!);
                if (File.Exists(backupPath))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                    File.Copy(backupPath, destination, true);
                    restored++;
                }
            }
            else if (File.Exists(destination))
            {
                File.Delete(destination);
            }
        }

        mod.Installed = false;
        mod.InstalledFiles = new List<InstalledFile>();
        mod.InstallId = null;
        mod.LoadedAt = null;
        Store.Save();
        AddHistory("Unloaded", mod.Name, $"{restored} original file(s) restored");
        return restored;
    }

    /// <summary>Restores a failed install so a package cannot leave half-deployed files behind.</summary>
    private void Rollback(IEnumerable<InstalledFile> installed, string targetDir)
    {
        foreach (var file in installed)
        {
            try
            {
                string destination = Paths.ResolveWithin(targetDir, file.Destination);
                if (!string.IsNullOrWhiteSpace(file.Backup))
                {
                    string backup = Paths.ResolveWithin(BackupsDir, file.Backup!);
                    if (File.Exists(backup))
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                        File.Copy(backup, destination, true);
                    }
                }
                else if (File.Exists(destination))
                {
                    File.Delete(destination);
                }
            }
            catch (Exception exception) when (exception is IOException or PackageException or UnauthorizedAccessException)
            {
                // Keep rolling back the remaining files; the original error is still reported.
            }
        }
    }

    private InstalledFile? WriteConfig(ModRecord mod, JsonObject manifest, string targetDir, string backupRoot, bool backupExisting)
    {
        string? relative = ModConfig.ConfigDestination(manifest);
        if (relative is null)
        {
            return null;
        }

        string destination = Paths.ResolveWithin(targetDir, Paths.SafeRelative(relative));
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);

        string? backup = null;
        if (backupExisting && File.Exists(destination))
        {
            string backupPath = Path.Combine(backupRoot, relative.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(backupPath)!);
            File.Copy(destination, backupPath, true);
            backup = Path.GetRelativePath(BackupsDir, backupPath).Replace('\\', '/');
        }

        var schema = ModConfig.ParseSchema(manifest["settings"] ?? manifest["variables"]);
        var values = ModConfig.BuildValues(schema, mod.Config);
        File.WriteAllText(destination, values.ToJsonString(ConfigJson) + Environment.NewLine);
        return new InstalledFile
        {
            Destination = Paths.SafeRelative(relative),
            Backup = backup,
            InstalledHash = Hashing.Sha256File(destination),
            Kind = "config",
        };
    }

    // ------------------------------------------------------------------ profiles
    public List<ModProfile> ListProfiles() =>
        Profiles?.Current.Profiles
            .OrderByDescending(profile => profile.CreatedAt, StringComparer.Ordinal)
            .ThenBy(profile => profile.Name, StringComparer.OrdinalIgnoreCase)
            .ToList() ?? new List<ModProfile>();

    public ModProfile SaveProfile(string name)
    {
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

        foreach (var mod in Store.Current.Mods)
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
        int applied = 0;
        foreach (var mod in Store.Current.Mods)
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
            foreach (var mod in Store.Current.Mods.Where(mod => mod.Installed))
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
