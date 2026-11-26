using System;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace ViModManager.Views;

internal sealed class ColorWheelControl : Control
{
    private const int BitmapSize = 240;
    private const double OuterPadding = 9;
    private static readonly WriteableBitmap WheelBitmap = CreateWheelBitmap();

    private double _hue;
    private double _saturation;
    private double _brightness = 1;

    public ColorWheelControl()
    {
        Cursor = new Cursor(StandardCursorType.Hand);
        Focusable = true;
    }

    public event Action<Color>? ColorChanged;

    public double Brightness => _brightness;

    public Color GetColor(double brightness) => FromHsv(_hue, _saturation, brightness);

    public void SetColor(Color color)
    {
        double red = color.R / 255d;
        double green = color.G / 255d;
        double blue = color.B / 255d;
        double maximum = Math.Max(red, Math.Max(green, blue));
        double minimum = Math.Min(red, Math.Min(green, blue));
        double delta = maximum - minimum;

        _hue = delta == 0
            ? 0
            : maximum == red
                ? 60 * (((green - blue) / delta) % 6)
                : maximum == green
                    ? 60 * ((blue - red) / delta + 2)
                    : 60 * ((red - green) / delta + 4);
        if (_hue < 0)
        {
            _hue += 360;
        }

        _saturation = maximum == 0 ? 0 : delta / maximum;
        _brightness = maximum;
        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        if (Bounds.Width <= 0 || Bounds.Height <= 0)
        {
            return;
        }

        var target = new Rect(0, 0, Bounds.Width, Bounds.Height);
        context.DrawImage(WheelBitmap, new Rect(0, 0, BitmapSize, BitmapSize), target);

        double radius = Math.Min(Bounds.Width, Bounds.Height) / 2 - OuterPadding;
        double angle = _hue * Math.PI / 180;
        var marker = new Point(
            Bounds.Width / 2 + Math.Cos(angle) * radius * _saturation,
            Bounds.Height / 2 + Math.Sin(angle) * radius * _saturation);
        context.DrawEllipse(Brushes.Transparent, new Pen(Brushes.White, 3), marker, 7, 7);
        context.DrawEllipse(new SolidColorBrush(GetColor(_brightness)), new Pen(Brushes.Black, 1), marker, 4, 4);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }

        double radius = Math.Min(Bounds.Width, Bounds.Height) / 2 - OuterPadding;
        if (radius <= 0)
        {
            return;
        }

        Point point = e.GetPosition(this);
        double x = point.X - Bounds.Width / 2;
        double y = point.Y - Bounds.Height / 2;
        _hue = (Math.Atan2(y, x) * 180 / Math.PI + 360) % 360;
        _saturation = Math.Clamp(Math.Sqrt(x * x + y * y) / radius, 0, 1);
        ColorChanged?.Invoke(GetColor(_brightness));
        InvalidateVisual();
        e.Handled = true;
    }

    private static WriteableBitmap CreateWheelBitmap()
    {
        var bitmap = new WriteableBitmap(
            new PixelSize(BitmapSize, BitmapSize),
            new Vector(96, 96),
            PixelFormat.Bgra8888,
            AlphaFormat.Premul);
        var pixels = new byte[BitmapSize * BitmapSize * 4];
        double center = BitmapSize / 2d;
        double radius = center - 1;

        for (int y = 0; y < BitmapSize; y++)
        {
            for (int x = 0; x < BitmapSize; x++)
            {
                double dx = (x + 0.5 - center) / radius;
                double dy = (y + 0.5 - center) / radius;
                double distance = Math.Sqrt(dx * dx + dy * dy);
                int index = (y * BitmapSize + x) * 4;
                if (distance > 1)
                {
                    continue;
                }

                double hue = (Math.Atan2(dy, dx) * 180 / Math.PI + 360) % 360;
                Color color = FromHsv(hue, distance, 1);
                pixels[index] = color.B;
                pixels[index + 1] = color.G;
                pixels[index + 2] = color.R;
                pixels[index + 3] = 255;
            }
        }

        using ILockedFramebuffer frame = bitmap.Lock();
        for (int y = 0; y < BitmapSize; y++)
        {
            Marshal.Copy(
                pixels,
                y * BitmapSize * 4,
                IntPtr.Add(frame.Address, y * frame.RowBytes),
                BitmapSize * 4);
        }

        return bitmap;
    }

    private static Color FromHsv(double hue, double saturation, double brightness)
    {
        hue = (hue % 360 + 360) % 360;
        saturation = Math.Clamp(saturation, 0, 1);
        brightness = Math.Clamp(brightness, 0, 1);

        double chroma = brightness * saturation;
        double secondary = chroma * (1 - Math.Abs(hue / 60 % 2 - 1));
        double offset = brightness - chroma;
        (double red, double green, double blue) = hue switch
        {
            < 60 => (chroma, secondary, 0d),
            < 120 => (secondary, chroma, 0d),
            < 180 => (0d, chroma, secondary),
            < 240 => (0d, secondary, chroma),
            < 300 => (secondary, 0d, chroma),
            _ => (chroma, 0d, secondary),
        };

        return Color.FromRgb(
            (byte)Math.Round((red + offset) * 255),
            (byte)Math.Round((green + offset) * 255),
            (byte)Math.Round((blue + offset) * 255));
    }
}
