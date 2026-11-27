using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json.Nodes;
using ViModManager.Core;
using ViModManager.Models;

namespace ViModManager.Tests;

public sealed class CoreTests : IDisposable
{
    private readonly string _root;

    public CoreTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "vimm-" + Guid.NewGuid().ToString("N"));
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
            // Best effort cleanup for a temp directory.
        }
    }

    /// <summary>Deletes a tree without following junctions created by the safety tests.</summary>
    private static void DeleteTree(string directory)
    {
        if (!Directory.Exists(directory))
        {
            return;
        }

        foreach (string entry in Directory.EnumerateFileSystemEntries(directory))
        {
            FileAttributes attributes = File.GetAttributes(entry);
            if ((attributes & FileAttributes.ReparsePoint) != 0)
            {
                if ((attributes & FileAttributes.Directory) != 0)
                {
                    Directory.Delete(entry);
                }
                else
                {
                    File.Delete(entry);
                }

                continue;
            }

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

    private ModManager Manager()
    {
        string data = Path.Combine(_root, "data");
        Directory.CreateDirectory(data);
        return new ModManager(
            new JsonStore<ModDatabase>(Path.Combine(data, "mods.json")),
            new JsonStore<HistoryDatabase>(Path.Combine(data, "history.json")),
            Path.Combine(_root, "mods"),
            Path.Combine(_root, "backups"));
    }

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

    [Theory]
    [InlineData("../evil.txt")]
    [InlineData("/absolute.txt")]
    [InlineData("C:/evil.txt")]
    [InlineData("a/../../b.txt")]
    public void SafeRelativeRejectsUnsafePaths(string value) =>
        Assert.Throws<PackageException>(() => Paths.SafeRelative(value));

    [Fact]
    public void ResolveWithinRejectsEscapingPaths()
    {
        string target = Path.Combine(_root, "target");
        Directory.CreateDirectory(target);
        Assert.Throws<PackageException>(() => Paths.ResolveWithin(target, "../outside.txt"));
        Assert.StartsWith(target, Paths.ResolveWithin(target, "mods/a/settings.json"));
    }

    [Fact]
    public void SchemaNormalizesTypesAndDefaults()
    {
        var schema = ModConfig.ParseSchema(JsonNode.Parse("""
        [
          {"key":"damage_multiplier","label":"Damage","type":"float","default":1.5,"min":0.1,"max":10},
          {"key":"mode","type":"choice","options":["vanilla","hardcore"]},
          {"key":"debug","type":"boolean","default":true},
          {"key":"name"},
          {"key":"","type":"text"}
        ]
        """));

        Assert.Equal(4, schema.Count);
        Assert.Equal("float", schema[0].Type);
        Assert.Equal("1.5", schema[0].DefaultValue);
        Assert.Equal("vanilla", schema[1].DefaultValue);
        Assert.Equal("true", schema[2].DefaultValue);
        Assert.Equal("text", schema[3].Type);
    }

    [Fact]
    public void CoerceClampsNumbersAndKeepsValidChoices()
    {
        var schema = ModConfig.ParseSchema(JsonNode.Parse("""
        [
          {"key":"level","type":"int","default":1,"min":1,"max":5},
          {"key":"mode","type":"choice","options":["a","b"]}
        ]
        """));

        Assert.Equal("5", ModConfig.Coerce(schema[0], "99"));
        Assert.Equal("1", ModConfig.Coerce(schema[0], "oops"));
        Assert.Equal("b", ModConfig.Coerce(schema[1], "b"));
        Assert.Equal("a", ModConfig.Coerce(schema[1], "nope"));
    }

    [Fact]
    public void InstallWritesTypedVariablesAndUninstallRestoresOriginals()
    {
        string manifest = """
        {
          "id": "demo",
          "name": "Demo Mod",
          "version": "1.0",
          "author": "Tester",
          "settings": [
            {"key":"damage","type":"float","default":1.5,"min":0.1,"max":10},
            {"key":"mode","type":"choice","options":["vanilla","hardcore"]},
            {"key":"debug","type":"bool","default":true}
          ],
          "config": {"file": "mods/demo/settings.json"},
          "files": [{"source":"payload/data.txt","destination":"data.txt"}]
        }
        """;
        string package = Package("demo", manifest, ("payload/data.txt", "modded"));

        ModManager manager = Manager();
        var (mod, info) = manager.ImportPackage(package);
        Assert.True(info.HasManifest);

        manager.SaveConfig(mod, new System.Collections.Generic.Dictionary<string, string>
        {
            ["damage"] = "2.5",
            ["mode"] = "hardcore",
            ["debug"] = "false",
            ["fov"] = "90",
        });

        string target = Path.Combine(_root, "target");
        Directory.CreateDirectory(Path.Combine(target, "mods", "demo"));
        string configPath = Path.Combine(target, "mods", "demo", "settings.json");
        File.WriteAllText(configPath, "{\"damage\": 0.5}");

        int written = manager.Install(mod, target);
        Assert.Equal(2, written);
        Assert.Equal("modded", File.ReadAllText(Path.Combine(target, "data.txt")));

        var config = JsonNode.Parse(File.ReadAllText(configPath))!.AsObject();
        Assert.Equal(2.5, config["damage"]!.GetValue<double>());
        Assert.Equal("hardcore", config["mode"]!.GetValue<string>());
        Assert.False(config["debug"]!.GetValue<bool>());
        Assert.Equal("90", config["fov"]!.GetValue<string>());

        manager.Uninstall(mod, target);
        Assert.False(File.Exists(Path.Combine(target, "data.txt")));
        Assert.Contains("0.5", File.ReadAllText(configPath));
        Assert.False(mod.Installed);
    }

    [Fact]
    public void LoadAllSkipsCatalogOnlyPackages()
    {
        ModManager manager = Manager();
        var (plain, _) = manager.ImportPackage(Package("plain", null, ("readme.txt", "hi")));
        plain.AutoDeploy = false;
        manager.Store.Save();
        manager.ImportPackage(Package("good", """
        {"id":"good","name":"Good","files":[{"source":"a.txt","destination":"a.txt"}]}
        """, ("a.txt", "payload")));

        string target = Path.Combine(_root, "target");
        Directory.CreateDirectory(target);
        LoadSummary summary = manager.LoadAll(target);

        Assert.Single(summary.Skipped);
        Assert.Single(summary.Loaded);
        Assert.Empty(summary.Failed);
        Assert.True(File.Exists(Path.Combine(target, "a.txt")));
    }

    [Fact]
    public void ManifestFreePackageDeploysEverySafeFile()
    {
        ModManager manager = Manager();
        var (mod, _) = manager.ImportPackage(Package("loose", null,
            ("textures/skin.png", "png-bytes"),
            ("scripts/boost.lua", "print(1)"),
            ("models/car.fbx", "fbx")));

        Assert.False(mod.HasManifest);
        Assert.True(mod.AutoDeploy);
        Assert.Equal("Script", mod.Kind);

        string target = Path.Combine(_root, "target");
        Directory.CreateDirectory(target);
        Assert.Equal(3, manager.Install(mod, target));
        Assert.Equal("png-bytes", File.ReadAllText(Path.Combine(target, "textures", "skin.png")));
        Assert.True(File.Exists(Path.Combine(target, "scripts", "boost.lua")));
        Assert.True(File.Exists(Path.Combine(target, "models", "car.fbx")));
    }

    [Fact]
    public void MoveChangesLoadOrder()
    {
        ModManager manager = Manager();
        manager.ImportPackage(Package("first", """
        {"id":"first","name":"First","files":[{"source":"a.txt","destination":"a.txt"}]}
        """, ("a.txt", "1")));
        var (second, _) = manager.ImportPackage(Package("second", """
        {"id":"second","name":"Second","files":[{"source":"a.txt","destination":"a.txt"}]}
        """, ("a.txt", "2")));

        Assert.Equal(new[] { "first", "second" }, manager.ListMods().Select(mod => mod.Id));
        Assert.True(manager.Move(second, -1));
        Assert.Equal(new[] { "second", "first" }, manager.ListMods().Select(mod => mod.Id));
        Assert.False(manager.Move(second, -1));
    }

    [Fact]
    public void ZipInspectionFindsManifestAndEntries()
    {
        string path = Package("inspect", """{"name":"Inspect","files":[]}""", ("payload/a.txt", "a"));
        PackageContents info = Packages.Inspect(path);

        Assert.True(info.HasManifest);
        Assert.Contains("payload/a.txt", info.Entries);
        Assert.Equal("Inspect", info.Manifest!["name"]!.GetValue<string>());
    }

    [Fact]
    public void LegacyManifestNameRemainsSupportedForZipAndFolderImports()
    {
        const string manifest = """
        {"id":"legacy","name":"Legacy Package","files":[
          {"source":"payload/a.txt","destination":"legacy/a.txt"}
        ]}
        """;
        string legacyZip = Path.Combine(_root, "legacy.zip");
        using (var archive = ZipFile.Open(legacyZip, ZipArchiveMode.Create))
        {
            using (var writer = new StreamWriter(archive.CreateEntry(Packages.LegacyManifestFileName).Open()))
            {
                writer.Write(manifest);
            }

            using var payload = new StreamWriter(archive.CreateEntry("payload/a.txt").Open());
            payload.Write("legacy zip");
        }

        string legacyFolder = Path.Combine(_root, "legacy-folder");
        Directory.CreateDirectory(Path.Combine(legacyFolder, "payload"));
        File.WriteAllText(Path.Combine(legacyFolder, Packages.LegacyManifestFileName), manifest);
        File.WriteAllText(Path.Combine(legacyFolder, "payload", "a.txt"), "legacy folder");

        ModManager manager = Manager();
        var (zipMod, zipInfo) = manager.ImportPackage(legacyZip);
        var (folderMod, folderInfo) = manager.ImportPackage(legacyFolder);
        Assert.True(zipInfo.HasManifest);
        Assert.True(folderInfo.HasManifest);
        Assert.Equal("Legacy Package", zipMod.Name);
        Assert.Equal("Legacy Package", folderMod.Name);

        string target = Path.Combine(_root, "target");
        Directory.CreateDirectory(target);
        manager.Install(zipMod, target);
        Assert.Equal("legacy zip", File.ReadAllText(Path.Combine(target, "legacy", "a.txt")));
        manager.Uninstall(zipMod, target);

        manager.Install(folderMod, target);
        Assert.Equal("legacy folder", File.ReadAllText(Path.Combine(target, "legacy", "a.txt")));
    }

    [Fact]
    public void FailedInstallRollsBackAlreadyCopiedFiles()
    {
        string manifest = """
        {"id":"broken","name":"Broken","files":[
          {"source":"a.txt","destination":"a.txt"},
          {"source":"missing.txt","destination":"b.txt"}
        ]}
        """;
        ModManager manager = Manager();
        var (mod, _) = manager.ImportPackage(Package("broken", manifest, ("a.txt", "modded")));

        string target = Path.Combine(_root, "target");
        Directory.CreateDirectory(target);
        File.WriteAllText(Path.Combine(target, "a.txt"), "original");

        Assert.Throws<PackageException>(() => manager.Install(mod, target));
        Assert.Equal("original", File.ReadAllText(Path.Combine(target, "a.txt")));
        Assert.False(File.Exists(Path.Combine(target, "b.txt")));
        Assert.False(mod.Installed);
    }

    [Fact]
    public void ReloadingVariablesKeepsTheOriginalConfigBackup()
    {
        string manifest = """
        {
          "id":"cfg","name":"Cfg",
          "settings":[{"key":"damage","type":"float","default":1.0}],
          "config":{"file":"cfg.json"},
          "files":[{"source":"a.txt","destination":"a.txt"}]
        }
        """;
        ModManager manager = Manager();
        var (mod, _) = manager.ImportPackage(Package("cfg", manifest, ("a.txt", "x")));

        string target = Path.Combine(_root, "target");
        Directory.CreateDirectory(target);
        string configPath = Path.Combine(target, "cfg.json");
        File.WriteAllText(configPath, "{\"damage\": 0.25}");

        manager.Install(mod, target);
        manager.SaveConfig(mod, new System.Collections.Generic.Dictionary<string, string> { ["damage"] = "3.5" });

        LoadOutcome outcome = manager.LoadMod(mod, target);
        Assert.Equal("configured", outcome.Status);
        Assert.Equal(3.5, JsonNode.Parse(File.ReadAllText(configPath))!["damage"]!.GetValue<double>());

        manager.Uninstall(mod, target);
        Assert.Contains("0.25", File.ReadAllText(configPath));
    }

    [Fact]
    public void DuplicateIdsDoNotOverwriteTheFirstPackage()
    {
        ModManager manager = Manager();
        var first = manager.ImportPackage(Package("one", """
        {"id":"shared","name":"First","files":[{"source":"a.txt","destination":"a.txt"}]}
        """, ("a.txt", "FIRST"))).Record;
        var second = manager.ImportPackage(Package("two", """
        {"id":"shared","name":"Second","files":[{"source":"a.txt","destination":"a.txt"}]}
        """, ("a.txt", "SECOND"))).Record;

        Assert.NotEqual(first.Id, second.Id);
        Assert.NotEqual(first.Package, second.Package);
        Assert.Equal("First", Packages.Inspect(Path.Combine(manager.ModsDir, first.Package)).Manifest!["name"]!.GetValue<string>());
        Assert.Equal("Second", Packages.Inspect(Path.Combine(manager.ModsDir, second.Package)).Manifest!["name"]!.GetValue<string>());
    }

    [Fact]
    public void SingleFileAndFolderImportsAreLoadable()
    {
        ModManager manager = Manager();
        string texture = Path.Combine(_root, "skin.png");
        File.WriteAllText(texture, "png-bytes");
        var (single, _) = manager.ImportPackage(texture);
        Assert.True(single.AutoDeploy);
        Assert.Equal("Texture", single.Kind);

        string folder = Path.Combine(_root, "folder-mod");
        Directory.CreateDirectory(Path.Combine(folder, "payload"));
        File.WriteAllText(Path.Combine(folder, Packages.ManifestFileName), """
        {"id":"folder-mod","name":"Folder Mod","kind":"Script","files":[{"source":"payload/boost.lua","destination":"scripts/boost.lua"}]}
        """);
        File.WriteAllText(Path.Combine(folder, "payload", "boost.lua"), "script-bytes");
        var (packed, _) = manager.ImportPackage(folder);
        Assert.True(packed.HasManifest);
        Assert.Equal("Script", packed.Kind);

        string target = Path.Combine(_root, "target");
        Directory.CreateDirectory(target);
        manager.Install(single, target);
        manager.Install(packed, target);
        Assert.Equal("png-bytes", File.ReadAllText(Path.Combine(target, "skin.png")));
        Assert.Equal("script-bytes", File.ReadAllText(Path.Combine(target, "scripts", "boost.lua")));
    }

    [Fact]
    public void ProfilesSnapshotAndRestoreVariables()
    {
        string data = Path.Combine(_root, "data");
        Directory.CreateDirectory(data);
        var profiles = new JsonStore<ProfileDatabase>(Path.Combine(data, "profiles.json"));
        var manager = new ModManager(
            new JsonStore<ModDatabase>(Path.Combine(data, "mods.json")),
            new JsonStore<HistoryDatabase>(Path.Combine(data, "history.json")),
            Path.Combine(_root, "mods"),
            Path.Combine(_root, "backups"),
            profiles);

        var (mod, _) = manager.ImportPackage(Package("profile", """
        {"id":"profile","name":"Profile Mod","settings":[{"key":"level","type":"int","default":1}],"files":[{"source":"a.txt","destination":"a.txt"}]}
        """, ("a.txt", "x")));
        manager.SaveConfig(mod, new System.Collections.Generic.Dictionary<string, string> { ["level"] = "7" });
        manager.Toggle(mod, false);

        var profile = manager.SaveProfile("Stealth");
        Assert.Single(manager.ListProfiles());
        Assert.False(profile.Enabled[mod.Id]);

        manager.Toggle(mod, true);
        manager.SaveConfig(mod, new System.Collections.Generic.Dictionary<string, string> { ["level"] = "2" });
        manager.ApplyProfile(profile);

        Assert.False(mod.Enabled);
        Assert.Equal("7", mod.Config["level"]);
    }

    [Fact]
    public void CorruptJsonStoreIsQuarantinedInsteadOfOverwritten()
    {
        string data = Path.Combine(_root, "data");
        Directory.CreateDirectory(data);
        string path = Path.Combine(data, "mods.json");
        File.WriteAllText(path, "{ this is not json ");

        var store = new JsonStore<ModDatabase>(path);
        Assert.Empty(store.Current.Mods);
        Assert.Contains(Directory.GetFiles(data, "mods.json.corrupt-*"), file => File.ReadAllText(file).Contains("not json"));
    }

    [Fact]
    public void ClassifierRecognisesKnownAssetTypes()
    {
        Assert.Equal("Texture", ModClassifier.Classify(new[] { "textures/leather.png", "b/logo.jpg" }));
        Assert.Equal("Script", ModClassifier.Classify(new[] { "scripts/boost.lua" }));
        Assert.Equal("Model", ModClassifier.Classify(new[] { "vehicles/car.fbx" }));
        Assert.Equal("Archive", ModClassifier.Classify(new[] { "common.pak" }));
        Assert.Equal("Other", ModClassifier.Classify(new[] { "readme.md" }));
    }

    [Fact]
    public void SafeRelativeAllowsDoubleDotsInsideNamesButRejectsWindowsDeviceNames()
    {
        Assert.Equal("foo..bar/baz..txt", Paths.SafeRelative("foo..bar/baz..txt"));
        Assert.Throws<PackageException>(() => Paths.SafeRelative("CON"));
        Assert.Throws<PackageException>(() => Paths.SafeRelative("nul.txt"));
        Assert.Throws<PackageException>(() => Paths.SafeRelative("dir/COM1.log"));
        Assert.Throws<PackageException>(() => Paths.SafeRelative("bad?.txt"));
        Assert.Throws<PackageException>(() => Paths.SafeRelative("trailing."));
    }

    [Fact]
    public void FolderEnumerationKeepsDoubleDotsInsideFileNames()
    {
        string folder = Path.Combine(_root, "dots");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "foo..bar.txt"), "x");

        Assert.Contains("foo..bar.txt", Packages.SafeFolderFiles(folder));
    }

    [Fact]
    public void ResolveWithinRejectsJunctionsInsideTheRoot()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        string target = Path.Combine(_root, "junction-target");
        string outside = Path.Combine(_root, "junction-outside");
        Directory.CreateDirectory(target);
        Directory.CreateDirectory(outside);
        CreateJunction(Path.Combine(target, "mods"), outside);

        Assert.Throws<PackageException>(() => Paths.ResolveWithin(target, "mods/evil.txt"));
    }

    [Fact]
    public void InstallRejectsDestinationsRedirectedByAJunction()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        string manifest = """
        {"id":"junction","name":"Junction","files":[
          {"source":"evil.txt","destination":"mods/evil.txt"}
        ]}
        """;
        ModManager manager = Manager();
        var (mod, _) = manager.ImportPackage(Package("junction", manifest, ("evil.txt", "payload")));

        string target = Path.Combine(_root, "junction-target");
        string outside = Path.Combine(_root, "junction-outside");
        Directory.CreateDirectory(target);
        Directory.CreateDirectory(outside);
        CreateJunction(Path.Combine(target, "mods"), outside);

        Assert.Throws<PackageException>(() => manager.Install(mod, target));
        Assert.False(File.Exists(Path.Combine(outside, "evil.txt")));
        Assert.False(mod.Installed);
    }

    [Fact]
    public void ImportRejectsDuplicateDestinationsBeforeStoringThePackage()
    {
        string manifest = """
        {"id":"dupe","name":"Dupe","files":[
          {"source":"a.txt","destination":"mods/data.txt"},
          {"source":"b.txt","destination":"MODS/DATA.TXT"}
        ]}
        """;
        ModManager manager = Manager();

        Assert.Throws<PackageException>(() =>
            manager.ImportPackage(Package("dupe", manifest, ("a.txt", "a"), ("b.txt", "b"))));
        Assert.Empty(manager.Store.Current.Mods);
        Assert.Empty(Directory.GetFiles(manager.ModsDir));
    }

    [Fact]
    public void ImportRejectsConfigFileThatCollidesWithPayload()
    {
        string manifest = """
        {"id":"collision","name":"Collision","config":{"file":"data.txt"},"files":[
          {"source":"a.txt","destination":"data.txt"}
        ]}
        """;
        ModManager manager = Manager();

        Assert.Throws<PackageException>(() =>
            manager.ImportPackage(Package("collision", manifest, ("a.txt", "a"))));
        Assert.Empty(manager.Store.Current.Mods);
    }

    [Fact]
    public void UninstallFailsWithoutClearingStateWhenABackupIsMissing()
    {
        string manifest = """
        {"id":"nobackup","name":"No Backup","files":[
          {"source":"a.txt","destination":"a.txt"}
        ]}
        """;
        ModManager manager = Manager();
        var (mod, _) = manager.ImportPackage(Package("nobackup", manifest, ("a.txt", "modded")));

        string target = Path.Combine(_root, "target");
        Directory.CreateDirectory(target);
        string destination = Path.Combine(target, "a.txt");
        File.WriteAllText(destination, "original");

        manager.Install(mod, target);
        var installed = Assert.Single(mod.InstalledFiles);
        Assert.NotNull(installed.Backup);
        File.Delete(Path.Combine(manager.BackupsDir, installed.Backup!.Replace('/', Path.DirectorySeparatorChar)));

        Assert.Throws<InvalidOperationException>(() => manager.Uninstall(mod, target));
        Assert.True(mod.Installed);
        Assert.Single(mod.InstalledFiles);
        Assert.Equal("modded", File.ReadAllText(destination));
    }

    [Fact]
    public void UninstallPreflightsEveryFileBeforeRestoringAny()
    {
        string manifest = """
        {"id":"preflight","name":"Preflight","files":[
          {"source":"a.txt","destination":"a.txt"},
          {"source":"b.txt","destination":"b.txt"}
        ]}
        """;
        ModManager manager = Manager();
        var (mod, _) = manager.ImportPackage(Package("preflight", manifest, ("a.txt", "A1"), ("b.txt", "B1")));

        string target = Path.Combine(_root, "target");
        Directory.CreateDirectory(target);
        string a = Path.Combine(target, "a.txt");
        string b = Path.Combine(target, "b.txt");
        File.WriteAllText(a, "A0");
        File.WriteAllText(b, "B0");

        manager.Install(mod, target);
        File.WriteAllText(b, "externally edited");

        Assert.Throws<InvalidOperationException>(() => manager.Uninstall(mod, target));
        Assert.Equal("A1", File.ReadAllText(a));
        Assert.True(mod.Installed);
    }

    [Fact]
    public void UninstallRejectsCorruptBackups()
    {
        string manifest = """
        {"id":"corruptbackup","name":"Corrupt Backup","files":[
          {"source":"a.txt","destination":"a.txt"}
        ]}
        """;
        ModManager manager = Manager();
        var (mod, _) = manager.ImportPackage(Package("corruptbackup", manifest, ("a.txt", "modded")));

        string target = Path.Combine(_root, "target");
        Directory.CreateDirectory(target);
        string destination = Path.Combine(target, "a.txt");
        File.WriteAllText(destination, "original");

        manager.Install(mod, target);
        var installed = Assert.Single(mod.InstalledFiles);
        Assert.NotNull(installed.Backup);
        string backupPath = Path.Combine(
            manager.BackupsDir,
            installed.Backup!.Replace('/', Path.DirectorySeparatorChar));
        File.WriteAllText(backupPath, "corrupted");

        Assert.Throws<InvalidOperationException>(() => manager.Uninstall(mod, target));
        Assert.True(mod.Installed);
        Assert.Equal("modded", File.ReadAllText(destination));
    }

    [Fact]
    public void LargePayloadsAreNotArtificiallyCapped()
    {
        string body = new string('x', 4 * 1024 * 1024);
        ModManager manager = Manager();
        var (mod, _) = manager.ImportPackage(Package("large", null, ("assets/large.bin", body)));

        string target = Path.Combine(_root, "target");
        Directory.CreateDirectory(target);
        manager.Install(mod, target);

        Assert.Equal(body.Length, new FileInfo(Path.Combine(target, "assets", "large.bin")).Length);
    }

    [Fact]
    public void PackageHashIsVerifiedBeforeDeployment()
    {
        string manifest = """
        {"id":"tamper","name":"Tamper","files":[
          {"source":"a.txt","destination":"a.txt"}
        ]}
        """;
        ModManager manager = Manager();
        var (mod, _) = manager.ImportPackage(Package("tamper", manifest, ("a.txt", "payload")));

        string package = Path.Combine(manager.ModsDir, mod.Package);
        using (var archive = ZipFile.Open(package, ZipArchiveMode.Update))
        {
            archive.CreateEntry("injected.txt");
        }

        string target = Path.Combine(_root, "target");
        Directory.CreateDirectory(target);
        Assert.Throws<InvalidOperationException>(() => manager.Install(mod, target));
        Assert.False(File.Exists(Path.Combine(target, "a.txt")));
    }

    [Fact]
    public async System.Threading.Tasks.Task ConcurrentInstallsAreSerializedAndBothComplete()
    {
        ModManager manager = Manager();
        var (first, _) = manager.ImportPackage(Package("first-concurrent", """
        {"id":"first-concurrent","name":"First","files":[{"source":"a.txt","destination":"same.txt"}]}
        """, ("a.txt", "ONE")));
        var (second, _) = manager.ImportPackage(Package("second-concurrent", """
        {"id":"second-concurrent","name":"Second","files":[{"source":"b.txt","destination":"same.txt"}]}
        """, ("b.txt", "TWO")));

        string target = Path.Combine(_root, "target");
        Directory.CreateDirectory(target);
        await System.Threading.Tasks.Task.WhenAll(
            System.Threading.Tasks.Task.Run(() => manager.Install(first, target)),
            System.Threading.Tasks.Task.Run(() => manager.Install(second, target)));

        // Shared destination and journal path: without the operation gate these writes interleave.
        string final = File.ReadAllText(Path.Combine(target, "same.txt"));
        Assert.Contains(final, new[] { "ONE", "TWO" });
        Assert.True(first.Installed);
        Assert.True(second.Installed);
        Assert.Equal(2, manager.History.Current.Entries.Count(entry => entry.Action == "Deployed"));
    }

    [Fact]
    public void InterruptedInstallIsRolledBackOnNextStart()
    {
        ModManager manager = Manager();
        var (mod, _) = manager.ImportPackage(Package("crash-install", """
        {"id":"crash-install","name":"Crash Install","files":[{"source":"a.txt","destination":"a.txt"}]}
        """, ("a.txt", "modded")));

        string target = Path.Combine(_root, "target");
        Directory.CreateDirectory(target);
        string destination = Path.Combine(target, "a.txt");
        File.WriteAllText(destination, "modded");

        string installId = Guid.NewGuid().ToString("N");
        string backupRelative = installId + "/a.txt";
        string backupPath = Path.Combine(manager.BackupsDir, installId, "a.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(backupPath)!);
        File.WriteAllText(backupPath, "original");

        var journal = new DeploymentJournal
        {
            Operation = DeploymentJournal.InstallOperation,
            Phase = DeploymentJournal.PreparePhase,
            ModId = mod.Id,
            ModName = mod.Name,
            TargetDir = target,
            InstallId = installId,
            Steps =
            {
                new InstalledFile
                {
                    Destination = "a.txt",
                    Backup = backupRelative,
                    BackupHash = Hashing.Sha256File(backupPath),
                    InstalledHash = Hashing.Sha256File(destination),
                },
            },
        };
        new DeploymentJournalStore(Path.GetDirectoryName(manager.Store.FilePath)!).Write(journal);

        ModManager restarted = Manager();
        Assert.Null(restarted.RecoveryWarning);
        Assert.Equal("original", File.ReadAllText(destination));
        Assert.False(restarted.Get(mod.Id)!.Installed);
        Assert.False(File.Exists(Path.Combine(
            Path.GetDirectoryName(manager.Store.FilePath)!,
            "deployment-journal.json")));
    }

    [Fact]
    public void CommittedInstallIsFinishedOnNextStart()
    {
        ModManager manager = Manager();
        var (mod, _) = manager.ImportPackage(Package("crash-commit", """
        {"id":"crash-commit","name":"Crash Commit","files":[{"source":"a.txt","destination":"a.txt"}]}
        """, ("a.txt", "modded")));

        string target = Path.Combine(_root, "target");
        Directory.CreateDirectory(target);
        string destination = Path.Combine(target, "a.txt");
        File.WriteAllText(destination, "modded");

        var journal = new DeploymentJournal
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
        };
        new DeploymentJournalStore(Path.GetDirectoryName(manager.Store.FilePath)!).Write(journal);

        ModManager restarted = Manager();
        Assert.Null(restarted.RecoveryWarning);
        var recovered = restarted.Get(mod.Id)!;
        Assert.True(recovered.Installed);
        Assert.Single(recovered.InstalledFiles);
        Assert.Equal("modded", File.ReadAllText(destination));
    }

    [Fact]
    public void InterruptedUninstallIsCompletedOnNextStart()
    {
        ModManager manager = Manager();
        var (mod, _) = manager.ImportPackage(Package("crash-uninstall", """
        {"id":"crash-uninstall","name":"Crash Uninstall","files":[{"source":"a.txt","destination":"a.txt"}]}
        """, ("a.txt", "modded")));

        string target = Path.Combine(_root, "target");
        Directory.CreateDirectory(target);
        string destination = Path.Combine(target, "a.txt");
        File.WriteAllText(destination, "modded");

        string installId = Guid.NewGuid().ToString("N");
        string backupRelative = installId + "/a.txt";
        string backupPath = Path.Combine(manager.BackupsDir, installId, "a.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(backupPath)!);
        File.WriteAllText(backupPath, "original");
        var installed = new InstalledFile
        {
            Destination = "a.txt",
            Backup = backupRelative,
            BackupHash = Hashing.Sha256File(backupPath),
            InstalledHash = Hashing.Sha256File(destination),
        };
        mod.Installed = true;
        mod.InstalledFiles = new System.Collections.Generic.List<InstalledFile> { installed };
        mod.InstallId = installId;
        manager.Store.Save();

        new DeploymentJournalStore(Path.GetDirectoryName(manager.Store.FilePath)!).Write(new DeploymentJournal
        {
            Operation = DeploymentJournal.UninstallOperation,
            Phase = DeploymentJournal.PreparePhase,
            ModId = mod.Id,
            ModName = mod.Name,
            TargetDir = target,
            InstallId = installId,
            Steps = { installed },
        });

        ModManager restarted = Manager();
        Assert.Null(restarted.RecoveryWarning);
        Assert.Equal("original", File.ReadAllText(destination));
        Assert.False(restarted.Get(mod.Id)!.Installed);
    }

    [Fact]
    public void ArchivesWithDuplicateEntryNamesAreRejected()
    {
        string path = Package("limit-duplicate-entry", null, ("a.txt", "1"), ("a.txt", "2"));
        Assert.Throws<PackageException>(() => Packages.Inspect(path));
    }

    private static void CreateJunction(string link, string target)
    {
        var startInfo = new System.Diagnostics.ProcessStartInfo("cmd.exe")
        {
            Arguments = $"/c mklink /J \"{link}\" \"{target}\"",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        using var process = System.Diagnostics.Process.Start(startInfo)
            ?? throw new InvalidOperationException("Could not start cmd.exe to create a test junction.");
        string output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0 || !Directory.Exists(link))
        {
            throw new InvalidOperationException($"Could not create a test junction: {output}");
        }
    }
}
