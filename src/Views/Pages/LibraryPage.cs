using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using ViModManager.Core;
using ViModManager.Models;

namespace ViModManager.Views;

public partial class MainWindow
{
    private Control BuildLibraryPage()
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("2*,3*") };

        var leftGrid = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto,Auto,*,Auto") };
        var headerRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        headerRow.Children.Add(Ui.H2("Packages"));
        _libraryCount = Ui.Kicker("0 packages");
        _libraryCount.HorizontalAlignment = HorizontalAlignment.Right;
        _libraryCount.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(_libraryCount, 1);
        headerRow.Children.Add(_libraryCount);
        leftGrid.Children.Add(headerRow);

        _search = Ui.Search("Filter by name, author or type…", _ => RefreshLibrary());
        _search.Margin = new Thickness(0, 12, 0, 0);
        Grid.SetRow(_search, 1);
        leftGrid.Children.Add(_search);

        var filters = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,*"),
            Margin = new Thickness(0, 10, 0, 0),
        };
        _typeFilter = Ui.Drop(ModClassifier.AllKinds, "All", value => { _kindFilter = value; RefreshLibrary(); });
        _statusFilter = Ui.Drop(new[] { "All", "Enabled", "Disabled", "Deployed", "Not deployed" }, "All",
            value => { _statusFilterValue = value; RefreshLibrary(); });
        _typeFilter.Margin = new Thickness(0, 0, 5, 0);
        _statusFilter.Margin = new Thickness(5, 0, 0, 0);
        filters.Children.Add(_typeFilter);
        Grid.SetColumn(_statusFilter, 1);
        filters.Children.Add(_statusFilter);
        Grid.SetRow(filters, 2);
        leftGrid.Children.Add(filters);

        _modCards = Ui.VStack(8);
        var listScroll = new ScrollViewer
        {
            Content = _modCards,
            Margin = new Thickness(0, 12, 0, 10),
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        };
        Grid.SetRow(listScroll, 3);
        leftGrid.Children.Add(listScroll);

        var importRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*") };
        var addFiles = Ui.BusyBtn("Import files", ImportAsync, "soft chip", "Import ZIP, folder or single asset.");
        addFiles.HorizontalAlignment = HorizontalAlignment.Stretch;
        addFiles.Margin = new Thickness(0, 0, 5, 0);
        var addFolder = Ui.BusyBtn("Import folder", ImportFolderAsync, "ghost chip", "Pack an unpacked folder.");
        addFolder.HorizontalAlignment = HorizontalAlignment.Stretch;
        addFolder.Margin = new Thickness(5, 0, 0, 0);
        Grid.SetColumn(addFolder, 1);
        importRow.Children.Add(addFiles);
        importRow.Children.Add(addFolder);
        Grid.SetRow(importRow, 4);
        leftGrid.Children.Add(importRow);

        var left = Ui.Panel(leftGrid, "panel-soft", new Thickness(16));
        left.HorizontalAlignment = HorizontalAlignment.Stretch;
        Grid.SetColumn(left, 0);
        grid.Children.Add(left);

        _detailPanel = Ui.VStack(16);
        var right = new ScrollViewer
        {
            Content = _detailPanel,
            Margin = new Thickness(18, 0, 0, 0),
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Padding = new Thickness(0, 0, 6, 20),
        };
        Grid.SetColumn(right, 1);
        grid.Children.Add(right);
        return grid;
    }

    private void RefreshLibrary()
    {
        var all = _viewModel.Mods;
        string query = _search.Text?.Trim().ToLowerInvariant() ?? "";
        var filtered = all.Where(mod =>
        {
            if (_kindFilter != "All" && !string.Equals(mod.Kind, _kindFilter, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return _statusFilterValue switch
            {
                "Enabled" => mod.Enabled,
                "Disabled" => !mod.Enabled,
                "Deployed" => mod.Installed,
                "Not deployed" => !mod.Installed,
                _ => true,
            };
        }).Where(mod => query.Length == 0 ||
            $"{mod.Name} {mod.Author} {mod.Description} {mod.Version} {mod.Kind}"
                .ToLowerInvariant().Contains(query, StringComparison.Ordinal)).ToList();

        if (filtered.All(mod => mod.Id != _selectedModId))
        {
            _selectedModId = filtered.FirstOrDefault()?.Id;
        }

        Ui.Clear(_modCards);
        foreach (var mod in filtered)
        {
            _modCards.Children.Add(BuildModCard(mod, mod.Id == _selectedModId));
        }

        if (filtered.Count == 0)
        {
            var emptyCard = Ui.VStack(8);
            emptyCard.Children.Add(Ui.Muted(
                all.Count == 0
                    ? "Your library is empty. Import a ZIP package, folder, or single file to get started."
                    : "No packages match your active search or filters.", true));
            _modCards.Children.Add(Ui.Inset(emptyCard, new Thickness(14)));
        }

        _libraryCount.Text = query.Length == 0 && _kindFilter == "All" && _statusFilterValue == "All"
            ? $"{all.Count} package(s)"
            : $"{filtered.Count} / {all.Count}";
        RefreshDetail();
    }

    private Border BuildModCard(ModRecord mod, bool selected)
    {
        var body = Ui.VStack(6);

        var titleRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        titleRow.Children.Add(Ui.Text(mod.Name, null, true, 14, FontWeight.SemiBold));
        var kindBadge = KindBadge(mod.Kind);
        kindBadge.Margin = new Thickness(8, 0, 0, 0);
        Grid.SetColumn(kindBadge, 1);
        titleRow.Children.Add(kindBadge);
        body.Children.Add(titleRow);

        var meta = new List<string>();
        if (!string.IsNullOrWhiteSpace(mod.Version) && mod.Version != "Unknown")
        {
            meta.Add("v" + mod.Version);
        }

        if (!string.IsNullOrWhiteSpace(mod.Author) && mod.Author != "Unknown")
        {
            meta.Add(mod.Author);
        }

        meta.Add(_viewModel.DeployableSummary(mod));
        body.Children.Add(Ui.Muted(string.Join(" · ", meta), true, 11.5));

        if (mod.Installed || !mod.Enabled)
        {
            var badges = new WrapPanel();
            if (mod.Installed)
            {
                badges.Children.Add(Ui.Badge("Deployed", "good"));
            }

            if (!mod.Enabled)
            {
                badges.Children.Add(Ui.Badge("Disabled", "warn"));
            }

            body.Children.Add(badges);
        }

        var card = new Border
        {
            Child = body,
            Cursor = new Cursor(StandardCursorType.Hand),
        };
        card.Classes.Add("card");
        if (selected)
        {
            card.Classes.Add("selected");
        }

        card.PointerPressed += (_, args) =>
        {
            if (args.GetCurrentPoint(card).Properties.IsLeftButtonPressed)
            {
                SelectMod(mod.Id);
            }
        };
        return card;
    }

    private static Border KindBadge(string kind) => Ui.Badge(kind, kind switch
    {
        "Script" => "warn",
        "Model" => "good",
        "Texture" => "accent",
        "Archive" => "accent",
        _ => null,
    });

    private void RefreshDetail()
    {
        Ui.Clear(_detailPanel);
        var mod = _viewModel.Get(_selectedModId);
        if (mod is null)
        {
            var empty = Ui.VStack(12);
            empty.Children.Add(Ui.Kicker("PACKAGE LIBRARY"));
            empty.Children.Add(Ui.Display("Nothing selected"));
            empty.Children.Add(Ui.Muted(
                "Select a package on the left to review its details, edit configuration values, or deploy it into your target folder.", true));
            var btns = Ui.HStack(10);
            btns.Children.Add(Ui.BusyBtn("Import files", ImportAsync, "primary"));
            btns.Children.Add(Ui.BusyBtn("Import folder", ImportFolderAsync, "ghost"));
            empty.Children.Add(btns);
            _detailPanel.Children.Add(Ui.Panel(empty, "panel-accent"));
            return;
        }

        var manifest = _viewModel.ManifestFor(mod);
        string? target = ModConfig.ConfigDestination(manifest);
        Dictionary<string, string> values;
        try
        {
            values = _viewModel.ConfigValues(mod).ToDictionary(
                pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        }
        catch (Exception)
        {
            values = new Dictionary<string, string>(mod.Config, StringComparer.Ordinal);
        }

        _detailPanel.Children.Add(BuildDetailHeader(mod, values.Count));
        _detailPanel.Children.Add(BuildOverviewCard(mod, target));
        _detailPanel.Children.Add(BuildVariablesCard(mod, values, target));
        _detailPanel.Children.Add(BuildFilesCard(mod));
    }

    private Border BuildDetailHeader(ModRecord mod, int variableCount)
    {
        var body = Ui.VStack(12);
        body.Children.Add(Ui.Kicker($"{mod.Kind.ToUpperInvariant()} PACKAGE"));
        body.Children.Add(Ui.Display(mod.Name));
        body.Children.Add(Ui.Muted(mod.Description, true));

        var badges = new WrapPanel();
        badges.Children.Add(Ui.Badge(mod.Installed ? "Deployed" : "In library", mod.Installed ? "good" : null));
        badges.Children.Add(Ui.Badge(mod.Enabled ? "Enabled" : "Disabled", mod.Enabled ? "accent" : "warn"));
        badges.Children.Add(Ui.Badge(mod.HasManifest ? "Manifest" : (mod.AutoDeploy ? "Auto-deploy" : "Catalogued"),
            mod.HasManifest || mod.AutoDeploy ? null : "warn"));
        if (variableCount > 0)
        {
            badges.Children.Add(Ui.Badge($"{variableCount} value(s)", "accent"));
        }

        body.Children.Add(badges);
        body.Children.Add(Ui.AccentRule(4, 4));

        var actions = new WrapPanel();
        actions.Children.Add(Spaced(Ui.BusyBtn(mod.Installed ? "Sync values" : "Deploy", DeploySelectedAsync, "primary",
            "Copy files and write values into the target folder.")));
        actions.Children.Add(Spaced(Ui.Btn(mod.Enabled ? "Disable" : "Enable", () => ToggleSelected(mod), "ghost chip")));
        actions.Children.Add(Spaced(Ui.BusyBtn("Rename", () => RenameAsync(mod), "ghost chip")));
        actions.Children.Add(Spaced(Ui.BusyBtn("Verify hash", () => VerifySelectedAsync(mod), "ghost chip")));
        if (mod.Installed)
        {
            actions.Children.Add(Spaced(Ui.BusyBtn("Unload", () => UnloadAsync(mod), "soft chip")));
        }

        actions.Children.Add(Spaced(Ui.BusyBtn("Remove", () => RemoveAsync(mod), "danger chip")));
        body.Children.Add(actions);

        return Ui.Panel(body, "panel-accent");
    }

    private Border BuildOverviewCard(ModRecord mod, string? target)
    {
        var (card, body) = SectionCard("Package details", "Metadata and deployment fingerprint");
        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("140,*"),
            RowDefinitions = new RowDefinitions("Auto,Auto,Auto,Auto,Auto,Auto,Auto"),
        };
        AddDetailRow(grid, 0, "Archive", mod.Package);
        AddDetailRow(grid, 1, "Category", $"{mod.Kind} — {ModClassifier.Hint(mod.Kind)}");
        AddDetailRow(grid, 2, "Payload", _viewModel.DeployableSummary(mod));
        AddDetailRow(grid, 3, "SHA-256", mod.Sha256.Length > 36 ? mod.Sha256[..36] + "…" : mod.Sha256);
        AddDetailRow(grid, 4, "Config output", target ?? "Not declared (app-only)");
        AddDetailRow(grid, 5, "Deployed at", mod.LoadedAt ?? "Not deployed");
        AddDetailRow(grid, 6, "Load order", $"#{(mod.Priority + 1).ToString(CultureInfo.InvariantCulture)}");
        body.Children.Add(grid);
        return card;
    }

    private Border BuildVariablesCard(ModRecord mod, Dictionary<string, string> values, string? target)
    {
        var (card, body) = SectionCard("Configuration values", "Saved locally and written on deploy when configured.");
        body.Children.Add(Ui.Muted(target is null
            ? $"Values stay saved in your library. Add \"config\": {{\"file\": \"...\"}} to {Packages.ManifestFileName} to export them as JSON on deploy."
            : $"Values save locally and write to \"{target}\" inside your target folder on deploy.", true));

        _fieldReaders.Clear();
        _customRows.Clear();

        var schema = _viewModel.SchemaFor(mod);
        if (schema.Count > 0)
        {
            var form = Ui.VStack(10);
            foreach (var field in schema)
            {
                BuildFieldRow(form, field, values.TryGetValue(field.Key, out string? value) ? value : field.DefaultValue);
            }

            body.Children.Add(form);
        }
        else
        {
            body.Children.Add(Ui.Muted(
                "This package declares no typed schema. You can still attach custom key/value entries below.", true));
        }

        body.Children.Add(Ui.Rule(6, 2));
        var customHeader = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        customHeader.Children.Add(Ui.H2("Custom entries"));
        var addCustom = Ui.Btn("+ Add entry", () => AddCustomRow(), "ghost chip");
        Grid.SetColumn(addCustom, 1);
        customHeader.Children.Add(addCustom);
        body.Children.Add(customHeader);

        _customRowsPanel = Ui.VStack(8);
        body.Children.Add(_customRowsPanel);
        var declared = schema.Select(field => field.Key).ToHashSet(StringComparer.Ordinal);
        foreach (var pair in values.Where(pair => !declared.Contains(pair.Key)))
        {
            AddCustomRow(pair.Key, pair.Value);
        }

        var actions = Ui.HStack(10);
        actions.HorizontalAlignment = HorizontalAlignment.Right;
        actions.Margin = new Thickness(0, 6, 0, 0);
        actions.Children.Add(Ui.BusyBtn("Reset defaults", () => ResetVariablesAsync(mod), "ghost"));
        actions.Children.Add(Ui.BusyBtn("Save values", () => SaveVariablesAsync(mod), "primary"));
        body.Children.Add(actions);

        return card;
    }

    private Border BuildFilesCard(ModRecord mod)
    {
        var (card, body) = SectionCard("Deployable files", "All destinations resolve safely inside the target folder.");
        List<PackageFile> files;
        try
        {
            files = _viewModel.FilesFor(mod).ToList();
        }
        catch (PackageException exception)
        {
            body.Children.Add(Ui.Muted(exception.Message));
            return card;
        }

        if (files.Count == 0)
        {
            body.Children.Add(Ui.Muted("No deployable files in this package."));
            return card;
        }

        foreach (var file in files.Take(25))
        {
            body.Children.Add(Ui.Inset(Ui.Mono($"{file.Source}   →   {file.Destination}", true), new Thickness(12, 9)));
        }

        if (files.Count > 25)
        {
            body.Children.Add(Ui.Muted($"… and {files.Count - 25} more file(s).", true));
        }

        return card;
    }

    private void BuildFieldRow(StackPanel parent, ConfigField field, string value)
    {
        Control input;
        switch (field.Type)
        {
            case "bool":
            {
                var toggle = new ToggleSwitch
                {
                    IsChecked = value == "true",
                    OnContent = "On",
                    OffContent = "Off",
                    MinHeight = 42,
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Right,
                };
                _fieldReaders.Add((field, () => toggle.IsChecked == true ? "true" : "false"));
                input = toggle;
                break;
            }
            case "choice":
            {
                var combo = Ui.Drop(field.Options, value, _ => { });
                combo.MinHeight = 44;
                _fieldReaders.Add((field, () => combo.SelectedItem as string ?? field.Options.FirstOrDefault() ?? ""));
                input = combo;
                break;
            }
            case "int":
            case "float":
            {
                bool integral = field.Type == "int";
                var numeric = new NumericUpDown
                {
                    Minimum = SafeDecimal(field.Min, -1000000d),
                    Maximum = SafeDecimal(field.Max, 1000000d),
                    Increment = SafeDecimal(field.Step, integral ? 1d : 0.1d),
                    FormatString = integral ? "0" : "0.######",
                    Value = decimal.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out decimal parsed)
                        ? parsed
                        : decimal.TryParse(field.DefaultValue, NumberStyles.Float, CultureInfo.InvariantCulture, out decimal fallback)
                            ? fallback
                            : 0m,
                };
                _fieldReaders.Add((field, () => integral
                    ? Math.Round((double)(numeric.Value ?? 0m)).ToString(CultureInfo.InvariantCulture)
                    : ((double)(numeric.Value ?? 0m)).ToString("0.######", CultureInfo.InvariantCulture)));
                input = numeric;
                break;
            }
            default:
            {
                var textBox = new TextBox { Text = value, MinHeight = 44 };
                _fieldReaders.Add((field, () => textBox.Text ?? ""));
                input = textBox;
                break;
            }
        }

        var details = Ui.VStack(4);
        details.Children.Add(Ui.Text(field.Label, null, true, null, FontWeight.SemiBold));
        if (field.Min is double min && field.Max is double max && field.Type is "int" or "float")
        {
            details.Children.Add(Ui.Mono(
                $"Range {min.ToString(CultureInfo.InvariantCulture)}–{max.ToString(CultureInfo.InvariantCulture)}",
                true));
        }

        if (!string.IsNullOrWhiteSpace(field.Description))
        {
            details.Children.Add(Ui.Text(field.Description, "muted", true, 11.5));
        }

        details.VerticalAlignment = VerticalAlignment.Center;
        input.VerticalAlignment = VerticalAlignment.Center;
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("2*,3*") };
        row.Children.Add(details);
        input.Margin = new Thickness(14, 0, 0, 0);
        Grid.SetColumn(input, 1);
        row.Children.Add(input);
        parent.Children.Add(Ui.Inset(row, new Thickness(16, 12)));
    }

    private static decimal SafeDecimal(double? value, double fallback)
    {
        double number = value ?? fallback;
        number = Math.Clamp(number, -1000000d, 1000000d);
        return (decimal)number;
    }

    private void AddCustomRow(string key = "", string value = "")
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*,Auto") };
        var keyBox = new TextBox { Watermark = "key", Text = key };
        var valueBox = new TextBox { Watermark = "value", Text = value, Margin = new Thickness(8, 0, 8, 0) };
        var row = new Border { Child = grid, Padding = new Thickness(10) };
        row.Classes.Add("tight");
        var remove = Ui.IconBtn("×", () =>
        {
            _customRows.RemoveAll(entry => entry.Row == row);
            _customRowsPanel.Children.Remove(row);
        }, "Remove entry");

        Grid.SetColumn(valueBox, 1);
        Grid.SetColumn(remove, 2);
        grid.Children.Add(keyBox);
        grid.Children.Add(valueBox);
        grid.Children.Add(remove);

        _customRowsPanel.Children.Add(row);
        _customRows.Add((row, keyBox, valueBox));
    }

    private static void AddDetailRow(Grid grid, int row, string label, string value)
    {
        var name = Ui.Muted(label, false, 12);
        var content = Ui.Text(value, "mono", true);
        Grid.SetRow(name, row);
        Grid.SetRow(content, row);
        Grid.SetColumn(content, 1);
        name.Margin = new Thickness(0, 0, 12, 7);
        content.Margin = new Thickness(0, 0, 0, 7);
        grid.Children.Add(name);
        grid.Children.Add(content);
    }
}
