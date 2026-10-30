using System;
using System.Collections.Generic;

namespace BetterGenshinImpact.Model;

[Serializable]
public class SettingItem
{
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;

    public List<string>? Options { get; set; }

    public Dictionary<string, List<string>>? CascadeOptions { get; set; }

    public object? Default { get; set; }
}
