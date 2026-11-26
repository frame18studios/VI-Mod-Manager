using System.Collections.Generic;

namespace ViModManager.Models;

/// <summary>A named snapshot of which mods are enabled and the values they had at save time.</summary>
public sealed class ModProfile
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string CreatedAt { get; set; } = "";
    public Dictionary<string, bool> Enabled { get; set; } = new();
    public Dictionary<string, Dictionary<string, string>> Config { get; set; } = new();
}

public sealed class ProfileDatabase
{
    public List<ModProfile> Profiles { get; set; } = new();
}
