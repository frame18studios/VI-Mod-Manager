using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using ViModManager.Core;

namespace ViModManager.Views;

public partial class MainWindow
{
    private Control BuildOverviewPage()
    {
        var root = Ui.VStack(18);

        // Hero banner: concise value prop + drag-and-drop cue + primary workflow actions
        var intro = Ui.VStack(14);
        intro.Children.Add(Ui.Kicker("LOCAL PACKAGE WORKBENCH"));
        intro.Children.Add(Ui.Display("Organize, configure, and deploy reversibly."));
        intro.Children.Add(Ui.Muted(
            "Drag and drop ZIP packages, folders, or loose assets anywhere on this window. "
            + "Replaced files are backed up automatically and restored when unloaded.", true, 13));

        var quick = new WrapPanel();
        quick.Children.Add(Spaced(Ui.BusyBtn("Import files", ImportAsync, "primary", "Import ZIP packages or individual assets.", Icons.Import)));
        quick.Children.Add(Spaced(Ui.BusyBtn("Import folder", ImportFolderAsync, "soft", "Pack and import an unpacked package directory.", Icons.Target)));
        quick.Children.Add(Spaced(Ui.Btn("Target folder", () => Go(4), "ghost", "Choose where files deploy.", Icons.FolderOpen)));
        intro.Children.Add(quick);

        var pills = new WrapPanel();
        pills.Children.Add(Ui.Badge("No code execution", "good"));
        pills.Children.Add(Ui.Badge("Path-traversal safe", "accent"));
        pills.Children.Add(Ui.Badge("SHA-256 verified backups"));
        intro.Children.Add(pills);

        root.Children.Add(Ui.Panel(intro, "panel-accent"));

        // v0.2 safety posture: make the guarantees visible, not only documented.
        var safetyBody = Ui.VStack(12);
        safetyBody.Children.Add(Ui.SectionHeader("Safety & integrity", $"{AppInfo.DisplayVersion} local-first guarantees"));
        var safetyGrid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,*"),
            RowDefinitions = new RowDefinitions("Auto,Auto,Auto,Auto"),
        };
        AddSafetyTile(safetyGrid, 0, 0, Icons.Check, "Path jail",
            "Every destination is re-resolved; symlinks and junctions are rejected.");
        AddSafetyTile(safetyGrid, 0, 1, Icons.Check, "Collision-free plan",
            "Duplicate and config-file destinations are rejected case-insensitively.");
        AddSafetyTile(safetyGrid, 1, 0, Icons.Check, "SHA-256 integrity",
            "Packages are verified before deploy; backups are verified before restore.");
        AddSafetyTile(safetyGrid, 1, 1, Icons.History, "Crash-safe journal",
            "Interrupted deploys are rolled back or completed on the next launch.");
        AddSafetyTile(safetyGrid, 2, 0, Icons.Check, "Atomic writes",
            "Config and library records use a temp file followed by an atomic move.");
        AddSafetyTile(safetyGrid, 2, 1, Icons.Deploy, "No size caps",
            "Large assets stream to disk without artificial GiB, count, or ratio limits.");
        AddSafetyTile(safetyGrid, 3, 0, Icons.History, "Local-only audit log",
            "Operation logs stay in data/logs; nothing is uploaded or telemetered.");
        AddSafetyTile(safetyGrid, 3, 1, Icons.Library, "Data, not code",
            "Payloads are opaque bytes and manifests are plain JSON. Nothing executes.");
        safetyBody.Children.Add(safetyGrid);
        root.Children.Add(Ui.Panel(safetyBody, "panel"));

        // Live Stats Grid
        var pulseBody = Ui.VStack(14);
        pulseBody.Children.Add(Ui.SectionHeader("Workspace summary", "Live library metrics"));
        var metrics = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,*,*,*"),
        };
        AddStatTile(metrics, 0, "Packages", "Total imported", _statLibrary = Ui.Stat("0", true));
        AddStatTile(metrics, 1, "Enabled", "Active in queue", _statEnabled = Ui.Stat("0"));
        AddStatTile(metrics, 2, "Deployed", "Currently active", _statLoaded = Ui.Stat("0"));
        AddStatTile(metrics, 3, "Values", "Saved parameters", _statVariables = Ui.Stat("0"));
        pulseBody.Children.Add(metrics);
        root.Children.Add(Ui.Panel(pulseBody, "panel"));

        // 2-Column Lower: Health & Recent Activity
        var lower = new Grid { ColumnDefinitions = new ColumnDefinitions("1.2*,*") };

        var checksBody = Ui.VStack(12);
        checksBody.Children.Add(Ui.SectionHeader("Environment & health", "Actionable status checks"));
        _healthContainer = Ui.VStack(10);
        checksBody.Children.Add(_healthContainer);
        var checks = Ui.Panel(checksBody, "panel");
        lower.Children.Add(checks);

        var activityBody = Ui.VStack(12);
        var actHeader = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        actHeader.Children.Add(Ui.H2("Recent activity"));
        var viewAll = Ui.Btn("View all →", () => Go(5), "link");
        viewAll.HorizontalAlignment = HorizontalAlignment.Right;
        Grid.SetColumn(viewAll, 1);
        actHeader.Children.Add(viewAll);
        activityBody.Children.Add(actHeader);
        activityBody.Children.Add(Ui.Rule(2, 4));
        _activityPanel = Ui.VStack(8);
        activityBody.Children.Add(_activityPanel);

        var activity = Ui.Panel(activityBody, "panel-soft");
        activity.Margin = new Thickness(16, 0, 0, 0);
        Grid.SetColumn(activity, 1);
        lower.Children.Add(activity);

        root.Children.Add(lower);
        return PageScroller(root);
    }

    private static void AddStatTile(Grid grid, int column, string title, string sub, TextBlock value)
    {
        var body = Ui.VStack(4);
        body.Children.Add(value);
        body.Children.Add(Ui.Text(title, null, false, 13, FontWeight.SemiBold));
        body.Children.Add(Ui.Muted(sub, false, 11));
        var tile = Ui.Inset(body, new Thickness(16));
        tile.Margin = new Thickness(column == 0 ? 0 : 6, 0, column == 3 ? 0 : 6, 0);
        Grid.SetColumn(tile, column);
        grid.Children.Add(tile);
    }

    private static void AddSafetyTile(Grid grid, int row, int column, string icon, string title, string detail)
    {
        var body = Ui.VStack(4);
        var header = Ui.HStack(8);
        header.Children.Add(Icons.Tinted(icon, 15, "AccentBrush"));
        header.Children.Add(Ui.Text(title, null, false, 12.5, FontWeight.SemiBold));
        body.Children.Add(header);
        body.Children.Add(Ui.Muted(detail, true, 11.5));
        var tile = Ui.Inset(body, new Thickness(14, 11));
        tile.Margin = new Thickness(column == 0 ? 0 : 6, 0, column == 0 ? 6 : 0, 8);
        Grid.SetRow(tile, row);
        Grid.SetColumn(tile, column);
        grid.Children.Add(tile);
    }

    private void RefreshOverview()
    {
        var mods = _services.Manager.ListMods();
        _statLibrary.Text = mods.Count.ToString(CultureInfo.InvariantCulture);
        _statEnabled.Text = mods.Count(mod => mod.Enabled).ToString(CultureInfo.InvariantCulture);
        _statLoaded.Text = mods.Count(mod => mod.Installed).ToString(CultureInfo.InvariantCulture);
        _statVariables.Text = mods.Sum(SafeVariableCount).ToString(CultureInfo.InvariantCulture);

        Ui.Clear(_healthContainer);
        var checks = new List<(string Text, string? ActionLabel, Action? Action, bool IsWarning)>();

        if (_services.Manager.RecoveryWarning is { } recovery)
        {
            Action openLog = () =>
            {
                string? log = AppLog.FilePath;
                if (log is not null)
                {
                    OpenFolderInExplorer(Path.GetDirectoryName(log)!);
                }
                else
                {
                    Go(6);
                }
            };
            checks.Add((
                "An interrupted operation needs attention: " + recovery,
                "Open log →",
                openLog,
                true));
        }

        if (mods.Count == 0)
        {
            checks.Add(("Your library is empty. Import a package or try examples/example-package-demo.zip.",
                "Import →", () => _ = ImportAsync(), false));
        }

        string path = _services.Settings.Current.TargetPath.Trim();
        if (path.Length == 0)
        {
            checks.Add(("No target folder selected. You can still configure package values before deploying.",
                "Select folder →", () => Go(4), true));
        }
        else if (!Directory.Exists(path))
        {
            checks.Add(("The saved target folder no longer exists on disk.",
                "Fix target →", () => Go(4), true));
        }

        int conflicts = _services.Manager.Conflicts().Count;
        if (conflicts > 0)
        {
            checks.Add(($"{conflicts} destination path overlap(s) found between enabled packages.",
                "Review order →", () => Go(2), true));
        }

        int missing = mods.Count(mod => !File.Exists(Path.Combine(_services.ModsDir, mod.Package)));
        if (missing > 0)
        {
            checks.Add(($"{missing} package archive(s) are missing from the local library directory.",
                "Open library →", () => Go(1), true));
        }

        if (checks.Count == 0)
        {
            var okRow = Ui.HStack(10);
            okRow.Children.Add(Icons.Tinted(Icons.Check, 16, "GoodBrush"));
            okRow.Children.Add(Ui.Text("All systems ready. Target exists and no conflicts or missing files were detected.",
                null, true, 13));
            _healthContainer.Children.Add(Ui.Inset(okRow, new Thickness(14)));
        }
        else
        {
            foreach (var (text, actionLabel, action, isWarning) in checks)
            {
                var rowGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };
                var icon = Icons.Tinted(isWarning ? Icons.Warning : Icons.Import, 15,
                    isWarning ? "WarnBrush" : "AccentBrush");
                icon.Margin = new Thickness(0, 2, 12, 0);
                rowGrid.Children.Add(icon);

                var msg = Ui.Muted(text, true, 12.5);
                msg.VerticalAlignment = VerticalAlignment.Center;
                Grid.SetColumn(msg, 1);
                rowGrid.Children.Add(msg);

                if (actionLabel is not null && action is not null)
                {
                    var btn = Ui.Btn(actionLabel, action, "link");
                    btn.Margin = new Thickness(12, 0, 0, 0);
                    btn.VerticalAlignment = VerticalAlignment.Center;
                    Grid.SetColumn(btn, 2);
                    rowGrid.Children.Add(btn);
                }

                _healthContainer.Children.Add(Ui.Inset(rowGrid, new Thickness(14, 12)));
            }
        }

        Ui.Clear(_activityPanel);
        var entries = _services.History.Current.Entries.Take(6).ToList();
        if (entries.Count == 0)
        {
            _activityPanel.Children.Add(Ui.Muted("No activity recorded yet. Import a package to begin.", true));
            return;
        }

        foreach (var entry in entries)
        {
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };
            var dot = new Border
            {
                Width = 7,
                Height = 7,
                CornerRadius = new CornerRadius(4),
                Background = Ui.Brush("AccentBrush"),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 10, 0),
            };
            row.Children.Add(dot);

            var info = Ui.VStack(2);
            info.Children.Add(Ui.Text($"{entry.Action} · {entry.Mod}", null, true, 12.5, FontWeight.SemiBold));
            if (!string.IsNullOrWhiteSpace(entry.Detail))
            {
                info.Children.Add(Ui.Muted(entry.Detail, true, 11));
            }

            Grid.SetColumn(info, 1);
            row.Children.Add(info);

            string shortTime = entry.Time.Length >= 16 ? entry.Time[11..16] : entry.Time;
            var time = Ui.Mono(shortTime);
            time.Margin = new Thickness(10, 0, 0, 0);
            time.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(time, 2);
            row.Children.Add(time);

            _activityPanel.Children.Add(Ui.Inset(row, new Thickness(12, 9)));
        }
    }

    private int SafeVariableCount(Models.ModRecord mod)
    {
        try
        {
            return _services.Manager.ConfigValues(mod).Count;
        }
        catch (Exception)
        {
            return mod.Config.Count;
        }
    }
}
