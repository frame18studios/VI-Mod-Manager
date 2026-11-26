using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace ViModManager.Views;

/// <summary>Shared control factory for consistent layout, spacing, and visual hierarchy.</summary>
internal static class Ui
{
    public static IBrush Brush(string key) =>
        Application.Current?.Resources[key] is IBrush brush ? brush : Brushes.Transparent;

    public static TextBlock Text(string text, string? classes = null, bool wrap = false, double? size = null, FontWeight? weight = null)
    {
        var block = new TextBlock
        {
            Text = text,
            TextWrapping = wrap ? TextWrapping.Wrap : TextWrapping.NoWrap,
        };
        AddClasses(block, classes);
        if (size is double fontSize)
        {
            block.FontSize = fontSize;
        }

        if (weight is FontWeight fontWeight)
        {
            block.FontWeight = fontWeight;
        }

        return block;
    }

    public static TextBlock Display(string text, bool wrap = true) => Text(text, "display", wrap);

    public static TextBlock Kicker(string text) => Text(text, "kicker");

    public static TextBlock Muted(string text, bool wrap = true, double? size = null) => Text(text, "muted", wrap, size);

    public static TextBlock H2(string text) => Text(text, "h2", true);

    public static TextBlock Overline(string text) => Text(text, "overline");

    public static TextBlock Mono(string text, bool wrap = false) => Text(text, "mono", wrap);

    public static TextBlock Stat(string text, bool accent = false) => Text(text, accent ? "stat stat-accent" : "stat");

    public static Button Btn(string text, Action click, string? classes = null, string? tip = null, string? iconPath = null)
    {
        var button = new Button { Content = BuildButtonContent(text, iconPath) };
        AddClasses(button, classes);
        if (tip is not null)
        {
            ToolTip.SetTip(button, tip);
        }

        button.Click += (_, _) => click();
        return button;
    }

    /// <summary>An action button that cannot be fired twice while work is in progress.</summary>
    public static Button BusyBtn(string text, Func<Task> click, string? classes = null, string? tip = null, string? iconPath = null)
    {
        var button = new Button { Content = BuildButtonContent(text, iconPath) };
        AddClasses(button, classes);
        if (tip is not null)
        {
            ToolTip.SetTip(button, tip);
        }

        button.Click += async (_, _) =>
        {
            if (!button.IsEnabled)
            {
                return;
            }

            button.IsEnabled = false;
            try
            {
                await click();
            }
            catch (Exception exception)
            {
                if (TopLevel.GetTopLevel(button) is Window owner)
                {
                    await Dialogs.Info(owner, "Something went wrong", exception.Message);
                }
            }
            finally
            {
                button.IsEnabled = true;
            }
        };

        return button;
    }

    private static object BuildButtonContent(string text, string? iconPath)
    {
        if (string.IsNullOrWhiteSpace(iconPath))
        {
            return text;
        }

        var row = HStack(7);
        row.VerticalAlignment = VerticalAlignment.Center;
        row.HorizontalAlignment = HorizontalAlignment.Center;
        row.Children.Add(Icons.Vector(iconPath, 14));
        row.Children.Add(new TextBlock
        {
            Text = text,
            VerticalAlignment = VerticalAlignment.Center,
        });
        return row;
    }

    public static Button IconBtn(string text, Action click, string tip, string? classes = null)
    {
        var button = new Button { Content = text };
        AddClasses(button, "icon " + classes);
        ToolTip.SetTip(button, tip);
        button.Click += (_, _) => click();
        return button;
    }

    public static Border Badge(string text, string? variant = null)
    {
        var border = new Border
        {
            Child = Text(text, "tag"),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 6, 4),
        };
        border.Classes.Add(variant is null ? "badge" : $"badge-{variant}");
        return border;
    }

    public static Border AccentMark()
    {
        var mark = new Border();
        mark.Classes.Add("accent-mark");
        return mark;
    }

    public static Border Panel(Control child, string classes = "panel", Thickness? padding = null)
    {
        var border = new Border { Child = child };
        AddClasses(border, classes);
        if (padding is Thickness value)
        {
            border.Padding = value;
        }

        return border;
    }

    public static Border Inset(Control child, Thickness? padding = null)
    {
        var border = new Border { Child = child };
        border.Classes.Add("inset");
        if (padding is Thickness value)
        {
            border.Padding = value;
        }

        return border;
    }

    public static Border Rule(double top = 0, double bottom = 0) => new()
    {
        Classes = { "rule" },
        Margin = new Thickness(0, top, 0, bottom),
    };

    public static Border AccentRule(double top = 0, double bottom = 0) => new()
    {
        Classes = { "accent-rule" },
        Margin = new Thickness(0, top, 0, bottom),
    };

    public static StackPanel SectionHeader(string title, string? meta = null)
    {
        var header = VStack(6);
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };

        var heading = H2(title);
        heading.VerticalAlignment = VerticalAlignment.Center;
        row.Children.Add(heading);

        if (!string.IsNullOrWhiteSpace(meta))
        {
            var note = Muted(meta, true, 11.5);
            note.VerticalAlignment = VerticalAlignment.Center;
            note.HorizontalAlignment = HorizontalAlignment.Right;
            note.Margin = new Thickness(16, 0, 0, 0);
            Grid.SetColumn(note, 1);
            row.Children.Add(note);
        }

        header.Children.Add(row);
        header.Children.Add(Rule(4, 6));
        return header;
    }

    public static StackPanel VStack(double spacing = 8) => new() { Spacing = spacing };

    public static StackPanel HStack(double spacing = 8) => new()
    {
        Spacing = spacing,
        Orientation = Orientation.Horizontal,
    };

    public static TextBox Field(string watermark, string? text = null) => new()
    {
        Watermark = watermark,
        Text = text ?? "",
    };

    public static TextBox Readout(string watermark, double height = 480) => new()
    {
        IsReadOnly = true,
        AcceptsReturn = true,
        TextWrapping = TextWrapping.NoWrap,
        Classes = { "readout" },
        Height = height,
        Watermark = watermark,
    };

    public static TextBox Search(string watermark, Action<string> changed)
    {
        var box = new TextBox { Watermark = watermark };
        box.TextChanged += (_, _) => changed(box.Text ?? "");
        return box;
    }

    public static ComboBox Drop(IEnumerable<string> items, string selected, Action<string> changed)
    {
        var list = items.ToList();
        var combo = new ComboBox
        {
            ItemsSource = list,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        combo.SelectedItem = list.Contains(selected) ? selected : list.FirstOrDefault() ?? "";
        combo.SelectionChanged += (_, _) =>
        {
            if (combo.SelectedItem is string value)
            {
                changed(value);
            }
        };
        return combo;
    }

    public static Border Swatch(string hex, double size = 36)
    {
        var border = new Border
        {
            Width = size,
            Height = size,
            CornerRadius = new CornerRadius(size * 0.28),
            BorderThickness = new Thickness(1.5),
            BorderBrush = Brush("LineStrongBrush"),
            VerticalAlignment = VerticalAlignment.Center,
        };
        SetSwatch(border, hex);
        return border;
    }

    public static void SetSwatch(Border border, string hex)
    {
        border.Background = Color.TryParse(hex, out Color color)
            ? new SolidColorBrush(color)
            : new SolidColorBrush(Colors.Transparent);
    }

    public static void AddClasses(StyledElement element, string? classes)
    {
        if (classes is null)
        {
            return;
        }

        foreach (string name in classes.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            element.Classes.Add(name);
        }
    }

    public static void Clear(StackPanel panel) => panel.Children.Clear();

    public static void Clear(WrapPanel panel) => panel.Children.Clear();
}
