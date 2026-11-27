using System.Collections.Generic;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using ViModManager.Core;
using ViModManager.Models;

namespace ViModManager.ViewModels;

/// <summary>
/// UI-agnostic seam for the main window. It exposes operations, not dialogs or controls, so the
/// core can be exercised in tests without launching Avalonia.
/// </summary>
public sealed class MainViewModel
{
    public MainViewModel(WorkspaceServices services)
    {
        Services = services;
    }

    public WorkspaceServices Services { get; }
    public IReadOnlyList<ModRecord> Mods => Services.Library.List();
    public IReadOnlyList<ModRecord> LoadOrder => Services.Library.LoadOrder();
    public string? RecoveryWarning => Services.Deployment.RecoveryWarning;
    public bool HasCorruptJournal => Services.Deployment.HasCorruptJournal;

    public void AcknowledgeCorruptJournal() => Services.Deployment.AcknowledgeCorruptJournal();

    public ModRecord? Get(string? id) => Services.Library.Get(id);
    public JsonObject? ManifestFor(ModRecord mod) => Services.Library.ManifestFor(mod);
    public IReadOnlyList<PackageFile> FilesFor(ModRecord mod) => Services.Library.FilesFor(mod);

    public Task<(ModRecord Record, PackageContents Info)> ImportAsync(string path) =>
        Services.Library.ImportAsync(path);

    public Task<int> DeployAsync(ModRecord mod, string targetDir) =>
        Services.Deployment.DeployAsync(mod, targetDir);

    public Task<LoadOutcome> DeploySelectedAsync(ModRecord mod, string targetDir) =>
        Services.Deployment.DeploySelectedAsync(mod, targetDir);

    public Task<LoadSummary> DeployAllAsync(string targetDir) =>
        Services.Deployment.DeployAllAsync(targetDir);

    public Task<int> UnloadAsync(ModRecord mod, string targetDir) =>
        Services.Deployment.UnloadAsync(mod, targetDir);

    public Task<string> WriteConfigAsync(ModRecord mod, string targetDir) =>
        Services.Deployment.ApplyConfigAsync(mod, targetDir);

    public Task<PackageVerification> VerifyAsync(ModRecord mod) =>
        Services.Deployment.VerifyAsync(mod);

    public TargetFolderInfo InspectTarget(string? folder) =>
        Services.Deployment.InspectTarget(folder);

    public void Toggle(ModRecord mod, bool enabled) => Services.Library.Toggle(mod, enabled);
    public void Rename(ModRecord mod, string name) => Services.Library.Rename(mod, name);
    public bool Move(ModRecord mod, int delta) => Services.Library.Move(mod, delta);
    public bool MoveTo(ModRecord mod, int target) => Services.Library.MoveTo(mod, target);
    public void Remove(ModRecord mod) => Services.Library.Remove(mod);
    public IReadOnlyList<Conflict> Conflicts() => Services.Library.Conflicts();
    public string DeployableSummary(ModRecord mod) => Services.Library.DeployableSummary(mod);

    public IReadOnlyList<ConfigField> SchemaFor(ModRecord mod) => Services.Configuration.SchemaFor(mod);
    public IReadOnlyDictionary<string, string> ValuesFor(ModRecord mod) => Services.Configuration.ValuesFor(mod);
    public IReadOnlyDictionary<string, string> ConfigValues(ModRecord mod) => Services.Configuration.ValuesFor(mod);
    public void SaveValues(ModRecord mod, IReadOnlyDictionary<string, string> values) =>
        Services.Configuration.Save(mod, values);
    public void ResetValues(ModRecord mod) => Services.Configuration.Reset(mod);

    public IReadOnlyList<ModProfile> Profiles() => Services.Profiles.List();
    public ModProfile SaveProfile(string name) => Services.Profiles.Save(name);
    public int ApplyProfile(ModProfile profile, string? targetDir = null) =>
        Services.Profiles.Apply(profile, targetDir);
    public void RenameProfile(ModProfile profile, string name) => Services.Profiles.Rename(profile, name);
    public void DeleteProfile(ModProfile profile) => Services.Profiles.Delete(profile);
    public void ClearHistory() => Services.Manager.ClearHistory();
}
