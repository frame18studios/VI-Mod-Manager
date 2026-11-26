using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using ViModManager.Core;
using ViModManager.Models;

namespace ViModManager.Tests;

/// <summary>Keeps the shipped example package importable and its load/unload flow working.</summary>
public sealed class ExamplePackageTests
{
    [Fact]
    public void ExamplePackageImportsLoadsAndUnloads()
    {
        string package = Path.Combine(FindRepositoryRoot(), "examples", "example-package-demo.zip");
        Assert.True(File.Exists(package), $"Missing example package: {package}");

        PackageContents contents = Packages.Inspect(package);
        Assert.True(contents.HasManifest);
        var schema = ModConfig.ParseSchema(contents.Manifest!["settings"]);
        Assert.Equal(5, schema.Count);
        Assert.Equal(
            new[] { "bool", "choice", "float", "int", "text" },
            schema.Select(field => field.Type).OrderBy(type => type, StringComparer.Ordinal));

        string? configTarget = ModConfig.ConfigDestination(contents.Manifest);
        Assert.False(string.IsNullOrWhiteSpace(configTarget));
        Assert.DoesNotContain(Packages.Files(contents.Manifest!), file => file.Destination == configTarget);

        string temp = Path.Combine(Path.GetTempPath(), "vmm-example-" + Guid.NewGuid().ToString("N"));
        string target = Path.Combine(temp, "target");
        Directory.CreateDirectory(target);
        try
        {
            var manager = new ModManager(
                new JsonStore<ModDatabase>(Path.Combine(temp, "data", "mods.json")),
                new JsonStore<HistoryDatabase>(Path.Combine(temp, "data", "history.json")),
                Path.Combine(temp, "mods"),
                Path.Combine(temp, "backups"));

            var (mod, _) = manager.ImportPackage(package);
            manager.SaveConfig(mod, new Dictionary<string, string>
            {
                ["scale"] = "3.25",
                ["preset"] = "focused",
                ["show_debug"] = "true",
                ["item_count"] = "75",
                ["label"] = "Example Run",
            });

            LoadOutcome outcome = manager.LoadMod(mod, target);
            Assert.Equal("installed", outcome.Status);
            Assert.True(mod.Installed);

            string configPath = Path.Combine(target, configTarget!.Replace('/', Path.DirectorySeparatorChar));
            Assert.True(File.Exists(configPath));
            var written = JsonNode.Parse(File.ReadAllText(configPath))!.AsObject();
            Assert.Equal(3.25, written["scale"]!.GetValue<double>());
            Assert.Equal("focused", written["preset"]!.GetValue<string>());
            Assert.True(written["show_debug"]!.GetValue<bool>());
            Assert.Equal(75, written["item_count"]!.GetValue<int>());
            Assert.Equal("Example Run", written["label"]!.GetValue<string>());

            manager.Uninstall(mod, target);
            Assert.False(mod.Installed);
            Assert.False(File.Exists(configPath));
            Assert.False(File.Exists(Path.Combine(target, "packages", "example-package-demo", "install-note.txt")));
        }
        finally
        {
            try
            {
                Directory.Delete(temp, true);
            }
            catch (IOException)
            {
                // Best effort cleanup for a temp directory.
            }
        }
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "ViModManager.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Repository root not found from the test output folder.");
    }
}
