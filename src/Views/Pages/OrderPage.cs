using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace ViModManager.Views;

public partial class MainWindow
{
    private Control BuildOrderPage()
    {
        var root = Ui.VStack(18);

        var intro = Ui.VStack(14);
        intro.Children.Add(Ui.Kicker("DEPLOYMENT SEQUENCE"));
        intro.Children.Add(Ui.Display("Control overwrite order."));
        intro.Children.Add(Ui.Muted(
            "Enabled packages deploy from top to bottom. If two packages target the same destination file, the package lower in the list wins.",
            true));

        var actions = new WrapPanel();
        actions.Children.Add(Spaced(Ui.BusyBtn("Deploy enabled", LoadAllAsync, "primary")));
        actions.Children.Add(Spaced(Ui.BusyBtn("Unload all", UnloadAllAsync, "danger")));
        actions.Children.Add(Spaced(Ui.BusyBtn("Scan conflicts", ShowConflictsAsync, "ghost")));
        intro.Children.Add(actions);
        root.Children.Add(Ui.Panel(intro, "panel-accent"));

        var queueBody = Ui.VStack(12);
        queueBody.Children.Add(Ui.SectionHeader("Deployment queue", "Top applies first, bottom overwrites"));
        _orderPanel = Ui.VStack(10);
        queueBody.Children.Add(_orderPanel);
        root.Children.Add(Ui.Panel(queueBody, "panel"));

        var conflictsBody = Ui.VStack(12);
        conflictsBody.Children.Add(Ui.SectionHeader("Destination conflicts", "Overlapping target paths across enabled packages"));
        _conflictPanel = Ui.VStack(8);
        conflictsBody.Children.Add(Ui.Inset(_conflictPanel, new Thickness(16)));
        root.Children.Add(Ui.Panel(conflictsBody, "panel-soft"));

        return PageScroller(root);
    }

    private void RefreshOrder()
    {
        Ui.Clear(_orderPanel);
        var mods = _services.Manager.ListMods();
        if (mods.Count == 0)
        {
            _orderPanel.Children.Add(Ui.Muted("No packages in the library yet. Import a package to build a deployment order.", true));
        }

        for (int position = 0; position < mods.Count; position++)
        {
            var mod = mods[position];
            var rowGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,Auto") };

            var indexBadge = Ui.Badge((position + 1).ToString("00", CultureInfo.InvariantCulture), mod.Enabled ? "accent" : null);
            indexBadge.Margin = new Thickness(0, 0, 14, 0);
            rowGrid.Children.Add(indexBadge);

            var info = Ui.VStack(3);
            info.VerticalAlignment = VerticalAlignment.Center;
            var title = Ui.HStack(8);
            title.Children.Add(Ui.Text(mod.Name, null, true, 14, FontWeight.SemiBold));
            if (mod.Installed)
            {
                title.Children.Add(Ui.Badge("Deployed", "good"));
            }

            if (!mod.Enabled)
            {
                title.Children.Add(Ui.Badge("Skipped", "warn"));
            }

            info.Children.Add(title);
            info.Children.Add(Ui.Muted($"{mod.Kind} · {_services.Manager.DeployableSummary(mod)}", false, 11.5));
            Grid.SetColumn(info, 1);
            rowGrid.Children.Add(info);

            var enabled = new ToggleSwitch
            {
                IsChecked = mod.Enabled,
                OnContent = "Include",
                OffContent = "Hold",
                Margin = new Thickness(14, 0),
                VerticalAlignment = VerticalAlignment.Center,
            };
            enabled.Click += (_, _) => ToggleSelected(mod);
            Grid.SetColumn(enabled, 2);
            rowGrid.Children.Add(enabled);

            var actions = Ui.HStack(6);
            actions.VerticalAlignment = VerticalAlignment.Center;
            var first = Ui.Btn("Top", () => MoveModTo(mod.Id, 0), "ghost chip", "Move to top");
            first.IsEnabled = position > 0;
            var up = Ui.IconBtn("▲", () => MoveMod(mod.Id, -1), "Move up");
            up.IsEnabled = position > 0;
            var down = Ui.IconBtn("▼", () => MoveMod(mod.Id, 1), "Move down");
            down.IsEnabled = position < mods.Count - 1;
            var open = Ui.Btn("Open", () => SelectMod(mod.Id), "soft chip", "Open in Library");
            actions.Children.Add(first);
            actions.Children.Add(up);
            actions.Children.Add(down);
            actions.Children.Add(open);
            Grid.SetColumn(actions, 3);
            rowGrid.Children.Add(actions);

            _orderPanel.Children.Add(Ui.Panel(rowGrid, "tight"));
        }

        RefreshConflicts();
    }

    private void RefreshConflicts()
    {
        Ui.Clear(_conflictPanel);
        var conflicts = _services.Manager.Conflicts();
        if (conflicts.Count == 0)
        {
            _conflictPanel.Children.Add(Ui.Muted("No path overlaps detected. Every enabled package writes to distinct destinations."));
            return;
        }

        foreach (var conflict in conflicts.Take(30))
        {
            var row = Ui.HStack(10);
            row.Children.Add(Ui.Badge("Overlap", "warn"));
            row.Children.Add(Ui.Mono($"{conflict.Destination}   ({conflict.First} → {conflict.Second})", true));
            _conflictPanel.Children.Add(row);
        }

        if (conflicts.Count > 30)
        {
            _conflictPanel.Children.Add(Ui.Muted($"… {conflicts.Count - 30} more overlap(s).", true));
        }
    }
}
