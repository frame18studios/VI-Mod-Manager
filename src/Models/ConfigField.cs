using System.Collections.Generic;

namespace ViModManager.Models;

public sealed class ConfigField
{
    public string Key { get; set; } = "";
    public string Label { get; set; } = "";
    public string Type { get; set; } = "text";
    public string Description { get; set; } = "";
    public string DefaultValue { get; set; } = "";
    public List<string> Options { get; set; } = new();
    public double? Min { get; set; }
    public double? Max { get; set; }
    public double? Step { get; set; }
}
