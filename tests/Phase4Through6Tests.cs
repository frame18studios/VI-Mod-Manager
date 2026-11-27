using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;
using ViModManager;
using ViModManager.Core;
using ViModManager.Models;
using ViModManager.ViewModels;
using Xunit;

namespace ViModManager.Tests;

/// <summary>Adversarial and architecture tests for the v0.2 hardening phases 4-6.</summary>
public sealed class Phase4Through6Tests : IDisposable
{
    private readonly string _root;

    public Phase4Through6Tests()
    {
        _root = Path.Combine(Path.GetTempPath(), "vimm-phase456-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try
        {
            DeleteTree(_root);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Best-effort cleanup only.
        }
    }

    [Fact]
    public void TargetInspectionCountsEveryTopLevelEntry()
    {
        string target = Path.Combine(_root, "wide-target");
        Directory.CreateDirectory(target);
        for (int index = 0; index < 2105; index++)
        {
            File.WriteAllText(Path.Combine(target, $"asset-{index:D4}.txt"), "x");
        }

        for (int index = 0; index < 25; index++)
        {
            Directory.CreateDirectory(Path.Combine(target, $"folder-{index:D2}"));
        }

        ITargetAdapter adapter = new TargetAdapterRegistry().Resolve(target);
        TargetFolderInfo info = adapter.Inspect(target);

        Assert.Equal("folder", adapter.Id);
        Assert.True(info.Valid);
        Assert.Equal(2105, info.TopLevelFiles);
        Assert.Equal(25, info.Folders.Count);
    }

    [Fact]
    public void FormatNeutralAdapterNeverClaimsApplicationDetection()
    {
        var registry = new TargetAdapterRegistry();
        string target = Path.Combine(_root, "plain-target");
        Directory.CreateDirectory(target);

        ITargetAdapter adapter = registry.Resolve(target);
        Assert.Same(registry.Default, adapter);
        Assert.Equal("Local folder", adapter.DisplayName);
        Assert.All(registry.Adapters, candidate => Assert.DoesNotContain(
            "game", candidate.Id, StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain("game", adapter.Inspect(target).Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CorruptJournalIsQuarantinedAndNormalOperationsContinue()
    {
        string data = DataPath();
        ModManager first = NewManager(data);
        string journal = Path.Combine(data, "deployment-journal.json");
        File.WriteAllText(journal, "{ this journal is damaged ");

        ModManager restarted = NewManager(data);
        Assert.Null(restarted.RecoveryWarning);
        Assert.Contains(
            Directory.GetFiles(data, "deployment-journal.json.corrupt-*"),
            file => File.ReadAllText(file).Contains("damaged"));
        Assert.False(File.Exists(journal));

        var (mod, _) = restarted.ImportPackage(Package("after-corrupt-journal", null, ("a.txt", "payload")));
        string target = Path.Combine(_root, "after-corrupt-target");
        Directory.CreateDirectory(target);
        Assert.Equal(1, restarted.Install(mod, target));
        Assert.Equal("payload", File.ReadAllText(Path.Combine(target, "a.txt")));
        Assert.NotNull(first);
    }

    [Fact]
    public void CommittedRecoveryRefusesTamperedFilesUntilTheyAreRestored()
    {
        string data = DataPath();
        ModManager manager = NewManager(data);
        var (mod, _) = manager.ImportPackage(Package("tampered-recovery", """
        {"id":"tampered-recovery","name":"Tampered Recovery","files":[{"source":"a.txt","destination":"a.txt"}]}
        """, ("a.txt", "expected")));

        string target = Path.Combine(_root, "tampered-target");
        Directory.CreateDirectory(target);
        string destination = Path.Combine(target, "a.txt");
        File.WriteAllText(destination, "expected");

        var store = new DeploymentJournalStore(data);
        store.Write(new DeploymentJournal
        {
            Operation = DeploymentJournal.InstallOperation,
            Phase = DeploymentJournal.CommitPhase,
            ModId = mod.Id,
            ModName = mod.Name,
            TargetDir = target,
            InstallId = Guid.NewGuid().ToString("N"),
            Steps =
            {
                new InstalledFile
                {
                    Destination = "a.txt",
                    InstalledHash = Hashing.Sha256File(destination),
                },
            },
        });

        File.WriteAllText(destination, "changed outside the manager");
        ModManager blocked = NewManager(data);
        Assert.NotNull(blocked.RecoveryWarning);
        Assert.Throws<InvalidOperationException>(() => blocked.Install(blocked.Get(mod.Id)!, target));

        File.WriteAllText(destination, "expected");
        ModManager recovered = NewManager(data);
        Assert.Null(recovered.RecoveryWarning);
        Assert.True(recovered.Get(mod.Id)!.Installed);
        Assert.False(File.Exists(Path.Combine(data, "deployment-journal.json")));
    }

    [Fact]
    public void MissingBackupRecoveryBlocksOperationsUntilTheBackupIsRestored()
    {
        string data = DataPath();
        ModManager manager = NewManager(data);
        var (mod, _) = manager.ImportPackage(Package("missing-backup-recovery", """
        {"id":"missing-backup-recovery","name":"Missing Backup Recovery","files":[{"source":"a.txt","destination":"a.txt"}]}
        """, ("a.txt", "modded")));

        string target = Path.Combine(_root, "backup-target");
        Directory.CreateDirectory(target);
        string destination = Path.Combine(target, "a.txt");
        File.WriteAllText(destination, "original");
        manager.Install(mod, target);

        InstalledFile installed = Assert.Single(mod.InstalledFiles);
        string backup = Path.Combine(manager.BackupsDir, installed.Backup!.Replace('/', Path.DirectorySeparatorChar));
        Assert.True(File.Exists(backup));
        File.Delete(backup);

        new DeploymentJournalStore(data).Write(new DeploymentJournal
        {
            Operation = DeploymentJournal.UninstallOperation,
            Phase = DeploymentJournal.PreparePhase,
            ModId = mod.Id,
            ModName = mod.Name,
            TargetDir = target,
            InstallId = mod.InstallId ?? "",
            Steps = { installed },
        });

        ModManager blocked = NewManager(data);
        Assert.NotNull(blocked.RecoveryWarning);
        Assert.Throws<InvalidOperationException>(() => blocked.Install(blocked.Get(mod.Id)!, target));

        File.WriteAllText(backup, "original");
        ModManager recovered = NewManager(data);
        Assert.Null(recovered.RecoveryWarning);
        Assert.Equal("original", File.ReadAllText(destination));
        Assert.False(recovered.Get(mod.Id)!.Installed);
    }

    [Fact]
    public async Task ViewModelExercisesLocalOperationsThroughServiceSeams()
    {
        var services = new AppServices(Path.Combine(_root, "services"));
        var viewModel = new MainViewModel(services.Workspace);

        var (mod, _) = await viewModel.ImportAsync(Package("seam", """
        {"id":"seam","name":"Seam","files":[{"source":"a.txt","destination":"a.txt"}]}
        """, ("a.txt", "payload")));

        string target = Path.Combine(_root, "seam-target");
        Directory.CreateDirectory(target);

        PackageVerification valid = await viewModel.VerifyAsync(mod);
        Assert.True(valid.Matches);

        Assert.Equal(1, await viewModel.DeployAsync(mod, target));
        Assert.Equal("payload", File.ReadAllText(Path.Combine(target, "a.txt")));
        Assert.Single(viewModel.Mods);

        string package = Path.Combine(services.ModsDir, mod.Package);
        using (var archive = ZipFile.Open(package, ZipArchiveMode.Update))
        {
            archive.CreateEntry("injected.txt");
        }

        PackageVerification tampered = await viewModel.VerifyAsync(mod);
        Assert.False(tampered.Matches);
        Assert.Contains("re-import", tampered.Message, StringComparison.OrdinalIgnoreCase);

        Assert.Equal(0, await viewModel.UnloadAsync(mod, target));
        Assert.False(File.Exists(Path.Combine(target, "a.txt")));
    }

    private string DataPath()
    {
        string data = Path.Combine(_root, "data");
        Directory.CreateDirectory(data);
        return data;
    }

    private ModManager NewManager(string data) => new(
        new JsonStore<ModDatabase>(Path.Combine(data, "mods.json")),
        new JsonStore<HistoryDatabase>(Path.Combine(data, "history.json")),
        Path.Combine(_root, "mods"),
        Path.Combine(_root, "backups"));

    private string Package(string stem, string? manifest, params (string Name, string Body)[] files)
    {
        string path = Path.Combine(_root, stem + ".zip");
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        if (manifest is not null)
        {
            using var writer = new StreamWriter(archive.CreateEntry(Packages.ManifestFileName).Open());
            writer.Write(manifest);
        }

        foreach (var (name, body) in files)
        {
            using var writer = new StreamWriter(archive.CreateEntry(name).Open());
            writer.Write(body);
        }

        return path;
    }

    private static void DeleteTree(string directory)
    {
        if (!Directory.Exists(directory))
        {
            return;
        }

        foreach (string entry in Directory.EnumerateFileSystemEntries(directory))
        {
            FileAttributes attributes = File.GetAttributes(entry);
            if ((attributes & FileAttributes.Directory) != 0)
            {
                DeleteTree(entry);
            }
            else
            {
                File.Delete(entry);
            }
        }

        Directory.Delete(directory);
    }
}
