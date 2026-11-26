using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using ViModManager.Models;

namespace ViModManager;

public sealed record ThemePresetInfo(
    string Name,
    string Tagline,
    string Accent,
    string Background,
    string Surface,
    string Panel,
    string Text,
    string Muted);

/// <summary>Applies the customizable palette, gradient surfaces, corner roundness, and type scale at runtime.</summary>
public static class ThemeManager
{
    public static readonly IReadOnlyList<ThemePresetInfo> Presets = new ThemePresetInfo[]
    {
        new("Nocturne", "Deep plum & hot pink", "#ff5ca8", "#0d0912", "#150f1c", "#1d1526", "#faf4fc", "#b4a3c0"),
        new("Sakura Night", "Obsidian & cherry blossom", "#ff7eb6", "#0e0c10", "#17131a", "#211b25", "#fcf5f9", "#bba9b5"),
        new("Velvet Rose", "Midnight wine & magenta", "#f43f8e", "#12080f", "#1b0d17", "#261320", "#fdf2f7", "#c49fb3"),
        new("Cyber Orchid", "Deep indigo & neon pink", "#ec4899", "#090b16", "#101426", "#181e36", "#f4f6ff", "#9fa9cf"),
        new("Slate", "Navy & electric cobalt", "#5b8cff", "#0b1020", "#101830", "#16213c", "#eaf0ff", "#9fb0d0"),
        new("Copper", "Espresso & warm amber", "#ff8a3d", "#160e0b", "#20140f", "#2a1a13", "#fff2e8", "#c9a997"),
        new("Paper", "Warm ivory light theme", "#c83b74", "#f4eff3", "#fcf9fb", "#ffffff", "#1e1720", "#6e6170"),
    };

    public static readonly string[] CornerStyles = { "Sharp", "Balanced", "Rounded", "Soft" };

    private static byte Alpha(double alpha) => (byte)Math.Round(Math.Clamp(alpha, 0, 1) * 255);

    public static void Apply(AppSettings settings)
    {
        Color background = Parse(settings.Background, "#0d0912");
        Color surface = Parse(settings.Surface, "#150f1c");
        Color panel = Parse(settings.Panel, "#1d1526");
        Color text = Parse(settings.Text, "#faf4fc");
        Color muted = Parse(settings.Muted, "#b4a3c0");
        Color accent = Parse(settings.Accent, "#ff5ca8");
        Color danger = Parse("#ff6b8b", "#ff6b8b");
        Color good = Parse("#6ce5a8", "#6ce5a8");
        Color warn = Parse("#ffb86c", "#ffb86c");

        bool light = Luminance(background) > 0.55;

        Set("BgBrush", background);
        Set("SurfaceBrush", surface);
        Set("PanelBrush", panel);
        Set("PanelHoverBrush", Mix(panel, accent, light ? 0.08 : 0.14));
        Set("FieldBrush", light ? Mix(surface, Colors.Black, 0.04) : Mix(surface, Colors.Black, 0.24));
        Set("InsetBrush", light ? Mix(background, Colors.Black, 0.05) : Mix(background, Colors.Black, 0.28));
        Set("TextBrush", text);
        Set("MutedBrush", Mix(muted, background, 0.05));
        Set("AccentBrush", accent);
        Set("AccentHoverBrush", Mix(accent, Colors.White, 0.18));
        Set("AccentPressedBrush", Mix(accent, Colors.Black, 0.20));
        Set("AccentSoftBrush", accent, light ? 0.13 : 0.17);
        Set("AccentSofterBrush", accent, light ? 0.06 : 0.08);
        Set("AccentLineBrush", accent, 0.42);
        Set("OnAccentBrush", Luminance(accent) > 0.72 ? Colors.Black : Colors.White);
        Set("LineBrush", text, light ? 0.10 : 0.09);
        Set("LineStrongBrush", text, light ? 0.22 : 0.20);
        Set("DangerBrush", danger);
        Set("DangerSoftBrush", danger, 0.15);
        Set("GoodBrush", good);
        Set("GoodSoftBrush", good, 0.14);
        Set("WarnBrush", warn);
        Set("WarnSoftBrush", warn, 0.15);

        if (Application.Current is { } app)
        {
            app.RequestedThemeVariant = light ? Avalonia.Styling.ThemeVariant.Light : Avalonia.Styling.ThemeVariant.Dark;

            app.Resources["HeroBrush"] = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
                GradientStops = new GradientStops
                {
                    new GradientStop(Mix(panel, accent, light ? 0.09 : 0.16), 0),
                    new GradientStop(Mix(panel, surface, 0.5), 1),
                },
            };

            app.Resources["AccentGradientBrush"] = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
                GradientStops = new GradientStops
                {
                    new GradientStop(Mix(accent, Colors.White, 0.18), 0),
                    new GradientStop(Mix(accent, Color.Parse("#b833ff"), 0.22), 1),
                },
            };

            app.Resources["BaseFontSize"] = 13d;

            (double sidebar, double hero, double panelR, double card, double control, double chip) = settings.CornerStyle switch
            {
                "Sharp" => (8d, 8d, 6d, 6d, 5d, 4d),
                "Balanced" => (14d, 14d, 12d, 10d, 8d, 6d),
                "Soft" => (26d, 26d, 22d, 18d, 15d, 12d),
                _ => (22d, 22d, 18d, 15d, 12d, 9d), // "Rounded"
            };

            app.Resources["SidebarRadius"] = new CornerRadius(sidebar);
            app.Resources["HeroRadius"] = new CornerRadius(hero);
            app.Resources["PanelRadius"] = new CornerRadius(panelR);
            app.Resources["CardRadius"] = new CornerRadius(card);
            app.Resources["ControlRadius"] = new CornerRadius(control);
            app.Resources["ChipRadius"] = new CornerRadius(chip);
        }
    }

    public static void ApplyPreset(AppSettings settings, ThemePresetInfo preset)
    {
        settings.ThemePreset = preset.Name;
        settings.Accent = preset.Accent;
        settings.Background = preset.Background;
        settings.Surface = preset.Surface;
        settings.Panel = preset.Panel;
        settings.Text = preset.Text;
        settings.Muted = preset.Muted;
    }

    private static void Set(string key, Color color, double? alpha = null)
    {
        if (Application.Current is { } app)
        {
            app.Resources[key] = new SolidColorBrush(alpha is null ? color : WithAlpha(color, alpha.Value));
        }
    }

    private static Color WithAlpha(Color color, double alpha) =>
        Color.FromArgb(Alpha(alpha), color.R, color.G, color.B);

    private static double Luminance(Color color) =>
        (0.299 * color.R + 0.587 * color.G + 0.114 * color.B) / 255.0;

    private static Color Parse(string? value, string fallback) =>
        Color.TryParse(value, out Color parsed) ? parsed : Color.Parse(fallback);

    private static Color Mix(Color first, Color second, double amount)
    {
        amount = Math.Clamp(amount, 0, 1);
        return Color.FromArgb(
            255,
            (byte)Math.Round(first.R + (second.R - first.R) * amount),
            (byte)Math.Round(first.G + (second.G - first.G) * amount),
            (byte)Math.Round(first.B + (second.B - first.B) * amount));
    }
}
