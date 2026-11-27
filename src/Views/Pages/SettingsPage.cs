using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using ViModManager.Core;
using ViModManager.Models;

namespace ViModManager.Views;

public partial class MainWindow
{
    private static readonly (string Key, string Label, string Help)[] ColorOptions =
    {
        ("accent", "Accent", "Buttons, active navigation, and highlights."),
        ("background", "Background", "The main workspace backdrop."),
        ("surface", "Surface", "Sidebar and secondary surfaces."),
        ("panel", "Panels", "Cards and work surfaces."),
        ("text", "Main text", "Headings and primary copy."),
        ("muted", "Secondary text", "Subtitles and supporting hints."),
    };

    private Control BuildSettingsPage()
    {
        var root = Ui.VStack(18);

        var intro = Ui.VStack(14);
        intro.Children.Add(Ui.Kicker("CUSTOMIZATION"));
        intro.Children.Add(Ui.Display("Shape the workspace to your taste."));
        intro.Children.Add(Ui.Muted(
            "Choose a complete palette, tune individual colors, and adjust the reading size and corner style.",
            true));
        root.Children.Add(Ui.Panel(intro, "panel-accent"));

        var (presetsCard, presetsBody) = SectionCard("Theme presets", "Apply a complete palette in one click");
        _presetCards.Clear();
        var presetWrap = new WrapPanel();
        foreach (var preset in ThemeManager.Presets)
        {
            var card = BuildPresetCard(preset);
            card.Margin = new Thickness(0, 0, 12, 12);
            presetWrap.Children.Add(card);
            _presetCards[preset.Name] = card;
        }

        presetsBody.Children.Add(presetWrap);
        root.Children.Add(presetsCard);

        var shapeBody = Ui.VStack(14);
        shapeBody.Children.Add(Ui.SectionHeader("Shape & typography", "Roundness and readable type scale"));
        shapeBody.Children.Add(Ui.Text("Corner style", null, false, 13, FontWeight.SemiBold));

        _cornerButtons.Clear();
        var cornerRow = new WrapPanel();
        foreach (string style in ThemeManager.CornerStyles)
        {
            var button = Ui.Btn(style, () => ApplyCornerStyle(style), "ghost chip");
            button.Margin = new Thickness(0, 0, 8, 6);
            cornerRow.Children.Add(button);
            _cornerButtons[style] = button;
        }

        shapeBody.Children.Add(cornerRow);

        var fontHeader = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        fontHeader.Children.Add(Ui.Text("Text size", null, false, 13, FontWeight.SemiBold));
        _fontSizeLabel = Ui.Kicker("13 px");
        Grid.SetColumn(_fontSizeLabel, 1);
        fontHeader.Children.Add(_fontSizeLabel);
        shapeBody.Children.Add(fontHeader);

        _fontSizeSlider = new Slider
        {
            Minimum = 10,
            Maximum = 16,
            TickFrequency = 1,
            IsSnapToTickEnabled = true,
            Value = _services.Settings.Current.FontSize,
        };
        _fontSizeSlider.PropertyChanged += (_, args) =>
        {
            if (args.Property == Slider.ValueProperty)
            {
                OnFontSliderChanged();
            }
        };
        shapeBody.Children.Add(_fontSizeSlider);

        var fontPreviewBody = Ui.VStack(4);
        fontPreviewBody.Children.Add(Ui.Muted("LIVE PREVIEW", false, 10.5));
        fontPreviewBody.Children.Add(Ui.Text("Aa  Clear text at your chosen size", null, true));
        var fontPreview = Ui.Inset(fontPreviewBody, new Thickness(12, 10));
        shapeBody.Children.Add(fontPreview);
        var shapeCard = Ui.Panel(shapeBody, "panel");
        root.Children.Add(shapeCard);

        var (paletteCard, paletteBody) = SectionCard(
            "Color tokens",
            "Choose a token, then click the color wheel. No hex values to type.");
        var paletteLayout = new Grid { ColumnDefinitions = new ColumnDefinitions("190,*") };

        var tokenList = Ui.VStack(7);
        foreach (var (key, label, _) in ColorOptions)
        {
            var swatch = new Border
            {
                Width = 24,
                Height = 24,
                CornerRadius = new CornerRadius(12),
                BorderThickness = new Thickness(2),
                BorderBrush = Ui.Brush("LineStrongBrush"),
                VerticalAlignment = VerticalAlignment.Center,
            };

            var content = Ui.HStack(10);
            content.VerticalAlignment = VerticalAlignment.Center;
            content.Children.Add(swatch);
            content.Children.Add(Ui.Text(label, null, false, 12.5, FontWeight.SemiBold));

            var button = new Button
            {
                Content = content,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Left,
                MinHeight = 46,
            };
            button.Classes.Add("color-token");
            button.Click += (_, _) => SelectColorToken(key);
            tokenList.Children.Add(button);
            _colorRows[key] = (button, swatch);
        }

        paletteLayout.Children.Add(tokenList);

        var pickerLayout = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
        _colorWheel = new ColorWheelControl
        {
            Width = 224,
            Height = 224,
            Margin = new Thickness(0, 2, 20, 2),
            VerticalAlignment = VerticalAlignment.Center,
        };
        ToolTip.SetTip(_colorWheel, "Click the wheel to select a hue and saturation.");
        _colorWheel.ColorChanged += OnColorWheelChanged;
        pickerLayout.Children.Add(_colorWheel);

        var pickerControls = Ui.VStack(12);
        pickerControls.VerticalAlignment = VerticalAlignment.Center;
        var previewHeader = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        var tokenDetails = Ui.VStack(3);
        _colorTokenName = Ui.H2("Accent");
        _colorTokenHelp = Ui.Muted("Buttons, active navigation, and highlights.", true, 11);
        tokenDetails.Children.Add(_colorTokenName);
        tokenDetails.Children.Add(_colorTokenHelp);
        previewHeader.Children.Add(tokenDetails);

        _colorPreview = new Border
        {
            Width = 44,
            Height = 44,
            CornerRadius = new CornerRadius(22),
            BorderBrush = Ui.Brush("LineStrongBrush"),
            BorderThickness = new Thickness(2),
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(_colorPreview, 1);
        previewHeader.Children.Add(_colorPreview);
        pickerControls.Children.Add(previewHeader);

        var brightnessHeader = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        brightnessHeader.Children.Add(Ui.Text("Brightness", null, false, 12.5, FontWeight.SemiBold));
        var brightnessValue = Ui.Kicker("100%");
        Grid.SetColumn(brightnessValue, 1);
        brightnessHeader.Children.Add(brightnessValue);
        pickerControls.Children.Add(brightnessHeader);

        _colorBrightness = new Slider
        {
            Minimum = 0,
            Maximum = 1,
            TickFrequency = 0.05,
            IsSnapToTickEnabled = false,
            Value = 1,
        };
        _colorBrightness.PropertyChanged += (_, args) =>
        {
            if (args.Property == Slider.ValueProperty)
            {
                brightnessValue.Text = $"{Math.Round(_colorBrightness.Value * 100):0}%";
                if (!_suppressColorEvent)
                {
                    ApplySelectedColor(_colorWheel.GetColor(_colorBrightness.Value));
                }
            }
        };
        pickerControls.Children.Add(_colorBrightness);
        pickerControls.Children.Add(Ui.Muted(
            "Click anywhere on the circle to choose hue and saturation. Use the slider to darken or brighten it.",
            true, 11.5));

        Grid.SetColumn(pickerControls, 1);
        pickerLayout.Children.Add(pickerControls);
        Grid.SetColumn(paletteLayout, 0);
        Grid.SetColumn(pickerLayout, 1);
        paletteLayout.Children.Add(pickerLayout);
        paletteBody.Children.Add(paletteLayout);
        root.Children.Add(paletteCard);

        var (storageCard, storageBody) = SectionCard("Storage", "Application data directory");
        var storageRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        _dataRootText = Ui.Mono(_services.Root, true);
        storageRow.Children.Add(Ui.Inset(_dataRootText, new Thickness(14, 11)));

        var openData = Ui.Btn("Open folder", () => OpenFolderInExplorer(_services.Root), "soft chip", "Open data root in File Explorer");
        openData.Margin = new Thickness(10, 0, 0, 0);
        openData.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(openData, 1);
        storageRow.Children.Add(openData);
        storageBody.Children.Add(storageRow);
        storageBody.Children.Add(Ui.Muted(
            "Set VIMM_DATA_ROOT (or VMM_DATA_ROOT) before launch to store data in another location.",
            true, 11.5));

        var reset = Ui.BusyBtn("Restore default appearance", ResetSettingsAsync, "danger chip");
        reset.HorizontalAlignment = HorizontalAlignment.Right;
        storageBody.Children.Add(reset);
        root.Children.Add(storageCard);

        var (aboutCard, aboutBody) = SectionCard("Build & local audit log", "Version, signing status, and operation history");
        aboutBody.Children.Add(Ui.Kicker(AppInfo.DisplayVersion + " · " + AppInfo.Channel));
        aboutBody.Children.Add(Ui.Muted(AppInfo.SigningStatus, true, 11.5));
        aboutBody.Children.Add(Ui.Rule(6, 4));
        var logRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        _logPathText = Ui.Mono(AppLog.FilePath ?? "Logging is not configured.", true);
        logRow.Children.Add(Ui.Inset(_logPathText, new Thickness(14, 11)));
        var openLogs = Ui.Btn("Open logs", () =>
        {
            string? log = AppLog.FilePath;
            string folder = log is null ? _services.Root : Path.GetDirectoryName(log) ?? _services.Root;
            OpenFolderInExplorer(folder);
        }, "soft chip", "Open the local audit log folder in File Explorer");
        openLogs.Margin = new Thickness(10, 0, 0, 0);
        openLogs.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(openLogs, 1);
        logRow.Children.Add(openLogs);
        aboutBody.Children.Add(logRow);
        aboutBody.Children.Add(Ui.Muted(
            "Logs record local deployment and recovery events only. Nothing is uploaded and no telemetry is collected.",
            true, 11.5));
        root.Children.Add(aboutCard);

        return PageScroller(root);
    }

    private Border BuildPresetCard(ThemePresetInfo preset)
    {
        var dots = Ui.HStack(6);
        foreach (string hex in new[] { preset.Background, preset.Panel, preset.Accent, preset.Text })
        {
            dots.Children.Add(new Border
            {
                Width = 16,
                Height = 16,
                CornerRadius = new CornerRadius(8),
                Background = new SolidColorBrush(Color.Parse(hex)),
                BorderBrush = Ui.Brush("LineStrongBrush"),
                BorderThickness = new Thickness(1),
            });
        }

        var body = Ui.VStack(7);
        body.Children.Add(dots);
        body.Children.Add(Ui.Text(preset.Name, null, false, 13.5, FontWeight.SemiBold));
        body.Children.Add(Ui.Muted(preset.Tagline, true, 11));

        var card = new Border { Width = 188, Child = body };
        card.Classes.Add("preset-card");
        card.PointerPressed += (_, args) =>
        {
            if (args.GetCurrentPoint(card).Properties.IsLeftButtonPressed)
            {
                ApplyPreset(preset);
            }
        };
        return card;
    }

    private void RefreshSettings()
    {
        var settings = _services.Settings.Current;
        RefreshColorTokens(settings);

        foreach (var (name, card) in _presetCards)
        {
            bool active = string.Equals(settings.ThemePreset, name, StringComparison.OrdinalIgnoreCase);
            if (active && !card.Classes.Contains("selected"))
            {
                card.Classes.Add("selected");
            }
            else if (!active && card.Classes.Contains("selected"))
            {
                card.Classes.Remove("selected");
            }
        }

        foreach (var (style, button) in _cornerButtons)
        {
            bool active = string.Equals(settings.CornerStyle, style, StringComparison.OrdinalIgnoreCase);
            button.Classes.Clear();
            Ui.AddClasses(button, active ? "primary chip" : "ghost chip");
        }

        _suppressFontEvent = true;
        _fontSizeSlider.Value = Math.Clamp(settings.FontSize, 10, 16);
        _fontSizeLabel.Text = $"{settings.FontSize} px";
        _suppressFontEvent = false;
        _dataRootText.Text = _services.Root;
        _logPathText.Text = AppLog.FilePath ?? "Logging is not configured.";
        RefreshColorEditor(settings);
    }

    private void RefreshColorTokens(AppSettings settings)
    {
        foreach (var (key, _, _) in ColorOptions)
        {
            if (_colorRows.TryGetValue(key, out var row))
            {
                string value = GetColor(settings, key);
                Ui.SetSwatch(row.Swatch, value);
                bool selected = string.Equals(_selectedColorKey, key, StringComparison.Ordinal);
                row.Button.Classes.Clear();
                Ui.AddClasses(row.Button, selected ? "color-token selected" : "color-token");
            }
        }
    }

    private void RefreshPresetCards(AppSettings settings)
    {
        foreach (var (name, card) in _presetCards)
        {
            bool active = string.Equals(settings.ThemePreset, name, StringComparison.OrdinalIgnoreCase);
            if (active && !card.Classes.Contains("selected"))
            {
                card.Classes.Add("selected");
            }
            else if (!active && card.Classes.Contains("selected"))
            {
                card.Classes.Remove("selected");
            }
        }
    }

    private void SelectColorToken(string key)
    {
        _selectedColorKey = key;
        var settings = _services.Settings.Current;
        RefreshColorTokens(settings);
        RefreshColorEditor(settings);
    }

    private void RefreshColorEditor(AppSettings settings)
    {
        if (_colorWheel is null || _colorPreview is null || _colorTokenName is null
            || _colorTokenHelp is null || _colorBrightness is null)
        {
            return;
        }

        string value = GetColor(settings, _selectedColorKey);
        Color color = Color.TryParse(value, out Color parsed) ? parsed : Color.Parse("#ff5ca8");
        var option = Array.Find(ColorOptions, item => item.Key == _selectedColorKey);
        _colorTokenName.Text = option.Label;
        _colorTokenHelp.Text = option.Help;
        Ui.SetSwatch(_colorPreview, value);
        _colorWheel.SetColor(color);

        _suppressColorEvent = true;
        _colorBrightness.Value = Math.Clamp(_colorWheel.Brightness, 0, 1);
        _suppressColorEvent = false;
    }

    private void OnColorWheelChanged(Color color) => ApplySelectedColor(color);

    private void ApplySelectedColor(Color color)
    {
        var settings = _services.Settings.Current;
        SetColor(settings, _selectedColorKey, ToHex(color));
        settings.ThemePreset = "Custom";
        _services.Settings.Save();
        ThemeManager.Apply(settings);
        RefreshColorTokens(settings);
        RefreshPresetCards(settings);
        Ui.SetSwatch(_colorPreview, GetColor(settings, _selectedColorKey));
    }

    private void ApplyCornerStyle(string style)
    {
        var settings = _services.Settings.Current;
        settings.CornerStyle = style;
        _services.Settings.Save();
        ThemeManager.Apply(settings);
        RefreshSettings();
    }

    private void ApplyPreset(ThemePresetInfo preset)
    {
        var settings = _services.Settings.Current;
        ThemeManager.ApplyPreset(settings, preset);
        _services.Settings.Save();
        ThemeManager.Apply(settings);
        RefreshSettings();
    }

    private async Task ResetSettingsAsync()
    {
        bool confirmed = await Dialogs.Confirm(this, "Reset appearance",
            "Restore the Nocturne pink theme, rounded corners, and default text size?", "Reset");
        if (!confirmed)
        {
            return;
        }

        _services.Settings.Current.ResetColors();
        _services.Settings.Current.FontSize = 13;
        _services.Settings.Save();
        ThemeManager.Apply(_services.Settings.Current);
        RefreshSettings();
        ApplyFontScale();
    }

    private void OnFontSliderChanged()
    {
        if (_suppressFontEvent)
        {
            return;
        }

        int size = (int)Math.Round(_fontSizeSlider.Value);
        var settings = _services.Settings.Current;
        settings.FontSize = size;
        _fontSizeLabel.Text = $"{size} px";
        _services.Settings.Save();
        ThemeManager.Apply(settings);
        ApplyFontScale();
    }

    private static string GetColor(AppSettings settings, string key) => key switch
    {
        "accent" => settings.Accent,
        "background" => settings.Background,
        "surface" => settings.Surface,
        "panel" => settings.Panel,
        "text" => settings.Text,
        "muted" => settings.Muted,
        _ => "#ff5ca8",
    };

    private static void SetColor(AppSettings settings, string key, string value)
    {
        switch (key)
        {
            case "accent": settings.Accent = value; break;
            case "background": settings.Background = value; break;
            case "surface": settings.Surface = value; break;
            case "panel": settings.Panel = value; break;
            case "text": settings.Text = value; break;
            case "muted": settings.Muted = value; break;
        }
    }

    private static string ToHex(Color color) =>
        $"#{color.R:X2}{color.G:X2}{color.B:X2}".ToLowerInvariant();
}
