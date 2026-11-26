using System.Collections.Generic;

namespace ViModManager.Models;

public sealed class HistoryEntry
{
    public string Time { get; set; } = "";
    public string Action { get; set; } = "";
    public string Mod { get; set; } = "";
    public string Detail { get; set; } = "";
}

public sealed class HistoryDatabase
{
    public List<HistoryEntry> Entries { get; set; } = new();
}
