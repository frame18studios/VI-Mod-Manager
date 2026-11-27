using System.Collections.Generic;
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
    public string? RecoveryWarning => Services.Deployment.RecoveryWarning;

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

    public Task<PackageVerification> VerifyAsync(ModRecord mod) =>
        Services.Deployment.VerifyAsync(mod);

    public void Toggle(ModRecord mod, bool enabled) => Services.Library.Toggle(mod, enabled);
    public bool Move(ModRecord mod, int delta) => Services.Library.Move(mod, delta);
    public string DeployableSummary(ModRecord mod) => Services.Library.DeployableSummary(mod);
}
