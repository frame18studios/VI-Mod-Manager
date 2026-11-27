using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using ViModManager.Models;

namespace ViModManager.Core;

/// <summary>Result of a local package integrity check. It contains no network or upload step.</summary>
public sealed record PackageVerification(bool Matches, string Message);

public interface ILibraryService
{
    IReadOnlyList<ModRecord> List();
    ModRecord? Get(string? id);
    Task<(ModRecord Record, PackageContents Info)> ImportAsync(string path);
    void Toggle(ModRecord mod, bool enabled);
    void Rename(ModRecord mod, string name);
    bool Move(ModRecord mod, int delta);
    bool MoveTo(ModRecord mod, int target);
    void Remove(ModRecord mod);
    IReadOnlyList<Conflict> Conflicts();
    string DeployableSummary(ModRecord mod);
}

public interface IDeploymentService
{
    string? RecoveryWarning { get; }
    Task<int> DeployAsync(ModRecord mod, string targetDir);
    Task<LoadOutcome> DeploySelectedAsync(ModRecord mod, string targetDir);
    Task<LoadSummary> DeployAllAsync(string targetDir);
    Task<int> UnloadAsync(ModRecord mod, string targetDir);
    Task<string> ApplyConfigAsync(ModRecord mod, string targetDir);
    Task<PackageVerification> VerifyAsync(ModRecord mod);
    TargetFolderInfo InspectTarget(string? folder);
}

public interface IConfigurationService
{
    IReadOnlyList<ConfigField> SchemaFor(ModRecord mod);
    IReadOnlyDictionary<string, string> ValuesFor(ModRecord mod);
    void Save(ModRecord mod, IReadOnlyDictionary<string, string> values);
    void Reset(ModRecord mod);
}

public interface IProfileService
{
    IReadOnlyList<ModProfile> List();
    ModProfile Save(string name);
    int Apply(ModProfile profile, string? targetDir = null);
    void Rename(ModProfile profile, string name);
    void Delete(ModProfile profile);
}

/// <summary>
/// Small, focused seams over the existing core engine. They keep the UI from reaching directly
/// into deployment details while avoiding a risky rewrite of the tested ModManager implementation.
/// </summary>
public sealed class WorkspaceServices
{
    public WorkspaceServices(ModManager manager, TargetAdapterRegistry? targets = null)
    {
        Manager = manager ?? throw new ArgumentNullException(nameof(manager));
        Targets = targets ?? new TargetAdapterRegistry();
        Library = new LibraryService(manager);
        Deployment = new DeploymentService(manager, Targets);
        Configuration = new ConfigurationService(manager);
        Profiles = new ProfileService(manager);
    }

    public ModManager Manager { get; }
    public TargetAdapterRegistry Targets { get; }
    public ILibraryService Library { get; }
    public IDeploymentService Deployment { get; }
    public IConfigurationService Configuration { get; }
    public IProfileService Profiles { get; }
}

internal sealed class LibraryService : ILibraryService
{
    private readonly ModManager _manager;
    public LibraryService(ModManager manager) => _manager = manager;

    public IReadOnlyList<ModRecord> List() => _manager.ListMods();
    public ModRecord? Get(string? id) => _manager.Get(id);
    public Task<(ModRecord Record, PackageContents Info)> ImportAsync(string path) => _manager.ImportPackageAsync(path);
    public void Toggle(ModRecord mod, bool enabled) => _manager.Toggle(mod, enabled);
    public void Rename(ModRecord mod, string name) => _manager.Rename(mod, name);
    public bool Move(ModRecord mod, int delta) => _manager.Move(mod, delta);
    public bool MoveTo(ModRecord mod, int target) => _manager.MoveTo(mod, target);
    public void Remove(ModRecord mod) => _manager.Remove(mod);
    public IReadOnlyList<Conflict> Conflicts() => _manager.Conflicts();
    public string DeployableSummary(ModRecord mod) => _manager.DeployableSummary(mod);
}

internal sealed class DeploymentService : IDeploymentService
{
    private readonly ModManager _manager;
    private readonly TargetAdapterRegistry _targets;

    public DeploymentService(ModManager manager, TargetAdapterRegistry targets)
    {
        _manager = manager;
        _targets = targets;
    }

    public string? RecoveryWarning => _manager.RecoveryWarning;
    public Task<int> DeployAsync(ModRecord mod, string targetDir) => _manager.InstallAsync(mod, targetDir);
    public Task<LoadOutcome> DeploySelectedAsync(ModRecord mod, string targetDir) => _manager.LoadModAsync(mod, targetDir);
    public Task<LoadSummary> DeployAllAsync(string targetDir) => _manager.LoadAllAsync(targetDir);
    public Task<int> UnloadAsync(ModRecord mod, string targetDir) => _manager.UninstallAsync(mod, targetDir);
    public Task<string> ApplyConfigAsync(ModRecord mod, string targetDir) => Task.Run(() => _manager.ApplyConfig(mod, targetDir));

    public async Task<PackageVerification> VerifyAsync(ModRecord mod)
    {
        string package = Path.Combine(_manager.ModsDir, mod.Package);
        if (!File.Exists(package))
        {
            return new PackageVerification(false, "The package file is missing.");
        }

        string actual = await Task.Run(() => Hashing.Sha256File(package)).ConfigureAwait(false);
        bool matches = string.Equals(actual, mod.Sha256, StringComparison.OrdinalIgnoreCase);
        return new PackageVerification(
            matches,
            matches
                ? "SHA-256 matches the imported package record."
                : "SHA-256 mismatch. The package changed or became corrupted; re-import it before deploying.");
    }

    public TargetFolderInfo InspectTarget(string? folder) => _targets.Resolve(folder).Inspect(folder);
}

internal sealed class ConfigurationService : IConfigurationService
{
    private readonly ModManager _manager;
    public ConfigurationService(ModManager manager) => _manager = manager;

    public IReadOnlyList<ConfigField> SchemaFor(ModRecord mod) => _manager.SchemaFor(mod);
    public IReadOnlyDictionary<string, string> ValuesFor(ModRecord mod) => _manager.ConfigValues(mod);
    public void Save(ModRecord mod, IReadOnlyDictionary<string, string> values) => _manager.SaveConfig(mod, values);
    public void Reset(ModRecord mod) => _manager.ResetConfig(mod);
}

internal sealed class ProfileService : IProfileService
{
    private readonly ModManager _manager;
    public ProfileService(ModManager manager) => _manager = manager;

    public IReadOnlyList<ModProfile> List() => _manager.ListProfiles();
    public ModProfile Save(string name) => _manager.SaveProfile(name);
    public int Apply(ModProfile profile, string? targetDir = null) => _manager.ApplyProfile(profile, targetDir);
    public void Rename(ModProfile profile, string name) => _manager.RenameProfile(profile, name);
    public void Delete(ModProfile profile) => _manager.DeleteProfile(profile);
}
