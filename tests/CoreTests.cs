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
            Directory.Delete(_root, true);
        }
        catch (IOException)
        {
            // Best effort cleanup for a temp directory.
        }
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
}
