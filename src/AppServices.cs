using System;
using System.IO;
using ViModManager.Core;
using ViModManager.Models;
using ViModManager.ViewModels;

namespace ViModManager;

public sealed class AppServices
{
    public string Root { get; }
    public string DataDir { get; }
    public string ModsDir { get; }
    public string BackupsDir { get; }

    public JsonStore<AppSettings> Settings { get; }
    public JsonStore<ModDatabase> Mods { get; }
    public JsonStore<HistoryDatabase> History { get; }
    public JsonStore<ProfileDatabase> Profiles { get; }
    public ModManager Manager { get; }
    public TargetFolder Targets { get; } = new();
    public WorkspaceServices Workspace { get; }
    public MainViewModel ViewModel { get; }

    public AppServices(string? root = null)
    {
        Root = Path.GetFullPath(
            root
            ?? Environment.GetEnvironmentVariable("VMM_DATA_ROOT")
            ?? Environment.GetEnvironmentVariable("VIMM_DATA_ROOT")
            ?? AppContext.BaseDirectory);
        DataDir = Path.Combine(Root, "data");
        ModsDir = Path.Combine(Root, "mods");
        BackupsDir = Path.Combine(Root, "backups");
        Directory.CreateDirectory(DataDir);
        Directory.CreateDirectory(ModsDir);
        Directory.CreateDirectory(BackupsDir);

        Settings = new JsonStore<AppSettings>(Path.Combine(DataDir, "settings.json"));
        Mods = new JsonStore<ModDatabase>(Path.Combine(DataDir, "mods.json"));
        History = new JsonStore<HistoryDatabase>(Path.Combine(DataDir, "history.json"));
        Profiles = new JsonStore<ProfileDatabase>(Path.Combine(DataDir, "profiles.json"));
        Manager = new ModManager(Mods, History, ModsDir, BackupsDir, Profiles);
        Workspace = new WorkspaceServices(Manager, new TargetAdapterRegistry(Targets));
        ViewModel = new MainViewModel(Workspace);
    }
}
