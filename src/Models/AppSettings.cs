namespace ViModManager.Models;

public sealed class AppSettings
{
    public string Accent { get; set; } = "#ff5ca8";
    public string Background { get; set; } = "#0d0912";
    public string Surface { get; set; } = "#150f1c";
    public string Panel { get; set; } = "#1d1526";
    public string Text { get; set; } = "#faf4fc";
    public string Muted { get; set; } = "#b4a3c0";
    public int FontSize { get; set; } = 13;
    public string TargetPath { get; set; } = "";
    public string ThemePreset { get; set; } = "Nocturne";
    public string CornerStyle { get; set; } = "Rounded";

    public void ResetColors()
    {
        Accent = "#ff5ca8";
        Background = "#0d0912";
        Surface = "#150f1c";
        Panel = "#1d1526";
        Text = "#faf4fc";
        Muted = "#b4a3c0";
        ThemePreset = "Nocturne";
        CornerStyle = "Rounded";
    }

    public void ResetAll()
    {
        ResetColors();
        FontSize = 13;
        TargetPath = "";
    }
}
