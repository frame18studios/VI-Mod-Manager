using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace ViModManager.Views;

public partial class MainWindow
{
    private Control BuildProfilesPage()
    {
        var root = Ui.VStack(18);

        var intro = Ui.VStack(14);
        intro.Children.Add(Ui.Kicker("WORKSPACE SNAPSHOTS"));
        intro.Children.Add(Ui.Display("Save and switch setups cleanly."));
        intro.Children.Add(Ui.Muted(
            "Profiles record which packages are enabled and every configuration value you have set. Switch setups in one click without re-importing anything.",
            true));
        root.Children.Add(Ui.Panel(intro, "panel-accent"));

        var (card, body) = SectionCard("Create snapshot", "Save your current enabled flags and values as a reusable profile.");
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        _profileName = Ui.Field("Profile name (e.g. Default, Minimal, Testing)");
        row.Children.Add(_profileName);
        var save = Ui.BusyBtn("Save profile", SaveProfileAsync, "primary");
        save.Margin = new Thickness(10, 0, 0, 0);
        Grid.SetColumn(save, 1);
        row.Children.Add(save);
        body.Children.Add(row);

        _profilesPanel = Ui.VStack(10);
        body.Children.Add(Ui.Rule(6, 4));
        body.Children.Add(_profilesPanel);
        root.Children.Add(card);

        var noteBody = Ui.VStack(10);
        noteBody.Children.Add(Ui.SectionHeader("How profiles behave", "Instant state & config sync"));
        noteBody.Children.Add(Ui.Muted(
            "Applying a profile restores enabled flags and saved values immediately. If a target folder is configured, any already-deployed packages "
            + "also have their config files rewritten. Copied payload files are untouched until you deploy or unload.", true));
        root.Children.Add(Ui.Panel(noteBody, "panel-soft"));

        return PageScroller(root);
    }

    private void RefreshProfiles()
    {
        Ui.Clear(_profilesPanel);
        var profiles = _services.Manager.ListProfiles();
        if (profiles.Count == 0)
        {
            _profilesPanel.Children.Add(Ui.Muted("No saved profiles yet. Name your current setup above to create one.", true));
            RefreshSidebarProfile();
            return;
        }

        foreach (var profile in profiles)
        {
            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,Auto,Auto") };
            var marker = Ui.AccentMark();
            marker.VerticalAlignment = VerticalAlignment.Center;
            marker.Margin = new Thickness(0, 0, 14, 0);
            grid.Children.Add(marker);

            var info = Ui.VStack(4);
            Grid.SetColumn(info, 1);
            info.Children.Add(Ui.Text(profile.Name, null, false, 14, FontWeight.SemiBold));
            info.Children.Add(Ui.Muted(
                $"{profile.CreatedAt}  ·  {profile.Enabled.Count} package(s)  ·  {profile.Config.Sum(entry => entry.Value.Count)} value(s)",
                false, 11.5));
            grid.Children.Add(info);

            var apply = Ui.BusyBtn("Apply", () => ApplyProfileAsync(profile), "primary chip");
            apply.Margin = new Thickness(10, 0, 6, 0);
            Grid.SetColumn(apply, 2);
            grid.Children.Add(apply);

            var rename = Ui.BusyBtn("Rename", () => RenameProfileAsync(profile), "ghost chip");
            rename.Margin = new Thickness(0, 0, 6, 0);
            Grid.SetColumn(rename, 3);
            grid.Children.Add(rename);

            var delete = Ui.BusyBtn("Delete", () => DeleteProfileAsync(profile), "danger chip");
            Grid.SetColumn(delete, 4);
            grid.Children.Add(delete);

            _profilesPanel.Children.Add(Ui.Panel(grid, "tight"));
        }

        RefreshSidebarProfile();
    }

    private void RefreshSidebarProfile()
    {
        var profile = _services.Manager.ListProfiles().FirstOrDefault();
        _sideProfile.Text = profile is null ? "No profile saved" : $"Latest profile: {profile.Name}";
    }
}
