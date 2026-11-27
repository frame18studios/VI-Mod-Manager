using System;
using System.IO;
using System.Linq;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using ViModManager.Core;

namespace ViModManager.Views;

public partial class MainWindow
{
    private Control BuildTargetPage()
    {
        var root = Ui.VStack(18);

        var intro = Ui.VStack(14);
        intro.Children.Add(Ui.Kicker("DESTINATION FOLDER"));
        intro.Children.Add(Ui.Display("Choose where packages deploy."));
        intro.Children.Add(Ui.Muted(
            "Select any writable folder you control. VI Mod Manager never launches external executables and treats all deployed files strictly as data.",
            true));
        root.Children.Add(Ui.Panel(intro, "panel-accent"));

        var (card, body) = SectionCard("Target path", "All package destinations resolve relative to this directory.");
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto,Auto,Auto") };
        _targetPath = Ui.Field("Path to the deployment folder");
        row.Children.Add(_targetPath);

        var browse = Ui.BusyBtn("Browse", ChooseTargetAsync, "ghost chip");
        browse.Margin = new Thickness(10, 0, 8, 0);
        Grid.SetColumn(browse, 1);
        row.Children.Add(browse);

        var save = Ui.Btn("Save", SaveTarget, "primary chip");
        Grid.SetColumn(save, 2);
        row.Children.Add(save);

        var refresh = Ui.Btn("Rescan", RefreshTarget, "ghost chip");
        refresh.Margin = new Thickness(8, 0, 8, 0);
        Grid.SetColumn(refresh, 3);
        row.Children.Add(refresh);

        var openExplorer = Ui.Btn("Open", () => OpenFolderInExplorer(_services.Settings.Current.TargetPath.Trim()),
            "soft chip", "Open target folder in File Explorer");
        Grid.SetColumn(openExplorer, 4);
        row.Children.Add(openExplorer);

        body.Children.Add(row);

        _targetInfo = Ui.Muted("No folder scanned yet.", true);
        body.Children.Add(_targetInfo);
        root.Children.Add(card);

        var lower = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*") };

        var contentsBody = Ui.VStack(10);
        contentsBody.Children.Add(Ui.SectionHeader("Directory preview", "Read-only top-level scan"));
        _targetFolders = Ui.Mono("Select a folder to see its top-level layout.", true);
        contentsBody.Children.Add(Ui.Inset(_targetFolders, new Thickness(16)));
        lower.Children.Add(Ui.Panel(contentsBody, "panel"));

        var protocolBody = Ui.VStack(10);
        protocolBody.Children.Add(Ui.SectionHeader("Safety & backups", "Predictable and reversible"));
        protocolBody.Children.Add(Ui.Muted(
            "• Every destination is validated to stay strictly inside this folder. Absolute paths, drive letters, and '..' segments are rejected.\n\n"
            + "• Existing files are copied to backups/ before replacement, and a SHA-256 hash is recorded.\n\n"
            + "• Unloading restores originals only if the deployed file still matches its recorded hash, protecting manual edits.", true));
        var protocol = Ui.Panel(protocolBody, "panel-soft");
        protocol.Margin = new Thickness(16, 0, 0, 0);
        Grid.SetColumn(protocol, 1);
        lower.Children.Add(protocol);

        root.Children.Add(lower);
        return PageScroller(root);
    }

    private void RefreshTarget()
    {
        string path = _services.Settings.Current.TargetPath.Trim();
        bool hasPath = path.Length > 0 && Directory.Exists(path);

        _targetChip.Text = hasPath ? "TARGET READY" : "NO TARGET";
        _targetChip.Foreground = Ui.Brush(hasPath ? "GoodBrush" : "MutedBrush");
        _targetChipBorder.Background = Ui.Brush(hasPath ? "GoodSoftBrush" : "PanelBrush");
        _targetChipBorder.BorderBrush = Ui.Brush(hasPath ? "GoodBrush" : "LineStrongBrush");
        _sideTargetPath.Text = hasPath ? path : "Not set";
        _statusDot.Background = Ui.Brush(hasPath ? "GoodBrush" : "MutedBrush");

        if (_pageIndex != 4 || _targetPath is null || _targetInfo is null || _targetFolders is null)
        {
            return;
        }

        if (!hasPath)
        {
            _targetPath.Text = path;
            _targetInfo.Text = path.Length == 0
                ? "No target folder selected. You can still configure package values in the Library."
                : "The saved folder does not exist on disk. Choose a valid folder.";
            _targetFolders.Text = "Select a folder to inspect its top-level layout.";
            return;
        }

        _targetPath.Text = path;
        TargetFolderInfo info = _services.Workspace.Deployment.InspectTarget(path);
        _targetInfo.Text = info.Message
            + $"\nTop-level files: {info.TopLevelFiles:N0}   ·   Top-level size: {FormatBytes(info.TopLevelBytes)}";

        if (info.Folders.Count == 0)
        {
            _targetFolders.Text = "No subfolders at the top level.";
            return;
        }

        var builder = new StringBuilder();
        builder.AppendLine($"{info.Folders.Count} subfolder(s)");
        foreach (string folder in info.Folders)
        {
            builder.AppendLine($"  /{folder}");
        }

        builder.AppendLine();
        builder.AppendLine("Read-only scan. No files are modified.");
        _targetFolders.Text = builder.ToString().TrimEnd();
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes < 1024)
        {
            return $"{bytes:N0} B";
        }

        if (bytes < 1024 * 1024)
        {
            return $"{bytes / 1024.0:N1} KB";
        }

        return $"{bytes / (1024.0 * 1024.0):N1} MB";
    }
}
