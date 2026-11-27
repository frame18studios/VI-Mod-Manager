using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace ViModManager.Core;

/// <summary>Summary of a folder the user chose as a deployment target.</summary>
public sealed class TargetFolderInfo
{
    public bool Valid { get; init; }
    public string Message { get; set; } = "";
    public string Path { get; init; } = "";
    public int TopLevelFiles { get; set; }
    public long TopLevelBytes { get; set; }
    public List<string> Folders { get; } = new();
}

/// <summary>
/// Format-neutral boundary for a deployment target. Implementations may only describe a folder;
/// they must not identify applications, inspect proprietary containers, or bypass access controls.
/// </summary>
public interface ITargetAdapter
{
    string Id { get; }
    string DisplayName { get; }
    bool Supports(string? folder);
    TargetFolderInfo Inspect(string? folder);
}

/// <summary>
/// Describes any folder in generic terms. The tool never assumes which application
/// owns the folder and never looks for identifiable executable names.
/// </summary>
public sealed class TargetFolder : ITargetAdapter
{
    public string Id => "folder";
    public string DisplayName => "Local folder";

    public bool Supports(string? folder) =>
        !string.IsNullOrWhiteSpace(folder) && Directory.Exists(folder);

    public TargetFolderInfo Inspect(string? folder)
    {
        if (string.IsNullOrWhiteSpace(folder))
        {
            return new TargetFolderInfo { Valid = false, Message = "No target folder selected yet." };
        }

        string root;
        try
        {
            root = Path.GetFullPath(folder);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return new TargetFolderInfo { Valid = false, Message = "That path is not valid." };
        }

        if (!Directory.Exists(root))
        {
            return new TargetFolderInfo { Valid = false, Message = "This folder does not exist." };
        }

        var info = new TargetFolderInfo
        {
            Valid = true,
            Path = root,
            Message = "Folder ready. Packages deploy relative to this folder.",
        };

        try
        {
            var files = Directory.EnumerateFiles(root).ToList();
            info.TopLevelFiles = files.Count;
            long total = 0;
            foreach (string file in files)
            {
                try
                {
                    total += new FileInfo(file).Length;
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    // Best-effort only; a locked file must not fail the folder scan.
                }
            }

            info.TopLevelBytes = total;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A restricted folder is still usable as a target.
        }

        try
        {
            info.Folders.AddRange(
                Directory.EnumerateDirectories(root)
                    .Select(Path.GetFileName)
                    .Where(name => !string.IsNullOrEmpty(name))!);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Ignored; the folder list is informational.
        }

        if (info.TopLevelFiles == 0 && info.Folders.Count == 0)
        {
            info.Message = "This folder is empty. Packages can still be deployed here.";
        }

        return info;
    }
}
