using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;

namespace ViModManager.Views;

public partial class MainWindow
{
    private Control BuildHistoryPage()
    {
        var root = Ui.VStack(18);

        var intro = Ui.VStack(14);
        intro.Children.Add(Ui.Kicker("AUDIT TRAIL"));
        intro.Children.Add(Ui.Display("Every action, in sequence."));
        intro.Children.Add(Ui.Muted(
            "A local chronological log of package imports, deployments, configuration changes, and profile switches (newest first, up to 500 entries).",
            true));
        root.Children.Add(Ui.Panel(intro, "panel-accent"));

        var body = Ui.VStack(12);
        body.Children.Add(Ui.SectionHeader("Activity log", "Filter or clear local history"));

        var controls = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        _historySearch = Ui.Search("Filter by action, package name, or detail…", _ => RefreshHistory());
        controls.Children.Add(_historySearch);

        var clear = Ui.BusyBtn("Clear log", ClearHistoryAsync, "danger chip");
        clear.Margin = new Thickness(10, 0, 0, 0);
        Grid.SetColumn(clear, 1);
        controls.Children.Add(clear);

        body.Children.Add(controls);
        body.Children.Add(Ui.AccentRule(4, 6));
        _historyBox = Ui.Readout("No activity matches this filter.", 500);
        body.Children.Add(_historyBox);
        root.Children.Add(Ui.Panel(body, "panel"));

        return PageScroller(root);
    }

    private void RefreshHistory()
    {
        string query = _historySearch.Text?.Trim().ToLowerInvariant() ?? "";
        var entries = _services.History.Current.Entries.Where(entry =>
            query.Length == 0 || $"{entry.Time} {entry.Action} {entry.Mod} {entry.Detail}"
                .ToLowerInvariant().Contains(query, StringComparison.Ordinal));

        _historyBox.Text = entries.Any()
            ? string.Join(Environment.NewLine, entries.Select(entry =>
                $"{entry.Time}   {entry.Action,-22} {entry.Mod,-28} {entry.Detail}"))
            : "No activity matches this filter.";
    }
}
