using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace ViModManager.Views;

/// <summary>Crisp filled 24x24 vector icons and brand mark for VI Mod Manager.</summary>
internal static class Icons
{
    // Filled Material-style 24x24 paths (render cleanly in PathIcon and inherit Foreground)
    public const string Overview = "M4,4 H10 A1,1 0 0 1 11,5 V10 A1,1 0 0 1 10,11 H4 A1,1 0 0 1 3,10 V5 A1,1 0 0 1 4,4 Z M14,4 H20 A1,1 0 0 1 21,5 V10 A1,1 0 0 1 20,11 H14 A1,1 0 0 1 13,10 V5 A1,1 0 0 1 14,4 Z M4,13 H10 A1,1 0 0 1 11,14 V19 A1,1 0 0 1 10,20 H4 A1,1 0 0 1 3,19 V14 A1,1 0 0 1 4,13 Z M14,13 H20 A1,1 0 0 1 21,14 V19 A1,1 0 0 1 20,20 H14 A1,1 0 0 1 13,19 V14 A1,1 0 0 1 14,13 Z";
    public const string Library = "M12,2 L2,7 L12,12 L22,7 L12,2 Z M4.2,11.1 L2,12.2 L12,17.2 L22,12.2 L19.8,11.1 L12,15 L4.2,11.1 Z M4.2,15.6 L2,16.7 L12,21.7 L22,16.7 L19.8,15.6 L12,19.5 L4.2,15.6 Z";
    public const string Order = "M3,5 H15 V7 H3 V5 Z M3,11 H15 V13 H3 V11 Z M3,17 H11 V19 H3 V17 Z M18,7 L18,16.2 L15.4,13.6 L14,15 L19,20 L24,15 L22.6,13.6 L20,16.2 L20,7 H18 Z";
    public const string Profiles = "M6,3 H18 A2,2 0 0 1 20,5 V21 L12,17.4 L4,21 V5 A2,2 0 0 1 6,3 Z";
    public const string Target = "M10,4 L12,6 H20 A2,2 0 0 1 22,8 V18 A2,2 0 0 1 20,20 H4 A2,2 0 0 1 2,18 V6 A2,2 0 0 1 4,4 H10 Z";
    public const string History = "M13,3 A9,9 0 0 0 4,12 H1 L4.9,15.9 L5,16 L9,12 H6 A7,7 0 1 1 8.1,16.9 L6.6,18.4 A9,9 0 1 0 13,3 Z M12,8 V13 L16.2,15.5 L17,14.2 L13.5,12.2 V8 H12 Z";
    public const string Customization = "M3,6 H13 V8 H3 V6 Z M17,6 H21 V8 H17 V6 Z M15,4 A3,3 0 1 0 15,10 A3,3 0 1 0 15,4 Z M3,11 H7 V13 H3 V11 Z M11,11 H21 V13 H11 V11 Z M9,9 A3,3 0 1 0 9,15 A3,3 0 1 0 9,9 Z M3,16 H14 V18 H3 V16 Z M18,16 H21 V18 H18 V16 Z M16,14 A3,3 0 1 0 16,20 A3,3 0 1 0 16,14 Z";

    public const string Import = "M11,3 H13 V12.2 L16.6,8.6 L18,10 L12,16 L6,10 L7.4,8.6 L11,12.2 V3 Z M4,18 H20 V20 H4 V18 Z";
    public const string Deploy = "M7,4 L20,12 L7,20 V4 Z";
    public const string FolderOpen = "M14,3 H21 V10 H19 V6.4 L11.7,13.7 L10.3,12.3 L17.6,5 H14 V3 Z M5,5 H11 V7 H5 V19 H17 V13 H19 V19 A2,2 0 0 1 17,21 H5 A2,2 0 0 1 3,19 V7 A2,2 0 0 1 5,5 Z";
    public const string Check = "M9,16.2 L4.8,12 L3.4,13.4 L9,19 L21,7 L19.6,5.6 L9,16.2 Z";
    public const string Warning = "M1,21 H23 L12,2 L1,21 Z M13,18 H11 V16 H13 V18 Z M13,14 H11 V10 H13 V14 Z";

    public static PathIcon Vector(string pathData, double size = 15, IBrush? foreground = null)
    {
        var icon = new PathIcon
        {
            Data = StreamGeometry.Parse(pathData),
            Width = size,
            Height = size,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        if (foreground is not null)
        {
            icon.Foreground = foreground;
        }

        return icon;
    }

    public static PathIcon Tinted(string pathData, double size, string brushKey) =>
        Vector(pathData, size, Ui.Brush(brushKey));

    /// <summary>Matches the abstract stacked-module mark in Assets/vimm-logo.svg.</summary>
    public static Border LogoMark(double size = 46)
    {
        var canvas = new Canvas
        {
            Width = 64,
            Height = 64,
        };

        AddModule(canvas, 14, 14, 36, 9, Ui.Brush("AccentGradientBrush"));
        AddModule(canvas, 21, 27.5, 29, 9, Ui.Brush("TextBrush"));
        AddModule(canvas, 12, 41, 36, 9, Ui.Brush("AccentGradientBrush"));

        var viewbox = new Viewbox
        {
            Width = size - 4,
            Height = size - 4,
            Stretch = Stretch.Uniform,
            Child = canvas,
        };

        return new Border
        {
            Width = size,
            Height = size,
            CornerRadius = new CornerRadius(size * 0.28),
            Background = Ui.Brush("HeroBrush"),
            BorderBrush = Ui.Brush("AccentLineBrush"),
            BorderThickness = new Thickness(1.5),
            VerticalAlignment = VerticalAlignment.Center,
            Child = viewbox,
        };
    }

    private static void AddModule(Canvas canvas, double left, double top, double width, double height, IBrush fill)
    {
        var module = new Border
        {
            Width = width,
            Height = height,
            CornerRadius = new CornerRadius(height / 2),
            Background = fill,
        };
        Canvas.SetLeft(module, left);
        Canvas.SetTop(module, top);
        canvas.Children.Add(module);
    }
}
