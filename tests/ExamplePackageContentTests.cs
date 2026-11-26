using System;
using System.IO;
using System.Linq;
using Xunit;

namespace ViModManager.Tests;

/// <summary>Checks that the bundled walkthrough contains only its manifest and text examples.</summary>
public sealed class ExamplePackageContentTests
{
    [Fact]
    public void ExamplePackageContainsOnlyItsManifestAndTextWalkthrough()
    {
        string root = FindRepositoryRoot();
        string package = Path.Combine(root, "examples", "example-package-demo.zip");
        Assert.True(File.Exists(package), $"Missing example package: {package}");

        using var stream = File.OpenRead(package);
        using var archive = new System.IO.Compression.ZipArchive(stream, System.IO.Compression.ZipArchiveMode.Read);
        Assert.Equal(
            new[] { "payload/demo-info.json", "payload/install-note.txt", "vmm-manifest.json" },
            archive.Entries.Select(entry => entry.FullName).OrderBy(name => name, StringComparer.Ordinal));
        Assert.All(archive.Entries, entry => Assert.InRange(entry.Length, 1L, 16_384L));
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
