// New Ilmenyx additions (adapted from the structure of the original InfoNode; MIT attribution in NOTICE): see LICENSING.md.
namespace Ilmenyx.Contracts;

/// <summary>Node of the system information tree shown on the System page.</summary>
public sealed class InfoNode
{
    public string Header { get; set; } = "";
    public string Value { get; set; } = "";
    public List<InfoNode> Children { get; } = [];
    public bool IsGroup => Children.Count > 0;
    public InfoNode Add(string header, string value = "")
    {
        var node = new InfoNode { Header = header, Value = value };
        Children.Add(node);
        return node;
    }
}

/// <summary>One installed memory module from SMBIOS (serial numbers are deliberately not modelled).</summary>
public sealed record MemoryModule(string Bank, string Locator, ulong Capacity, string Type, string Form, int RatedSpeed, int ConfiguredSpeed,
    string Maker, string Part, int DataWidth, int TotalWidth, string Rank, double? ConfiguredVolt, double? MinVolt, double? MaxVolt);
public sealed record MemoryArrayInfo(int Slots, ulong MaxBytes);
public sealed record MemoryInventory(MemoryArrayInfo Array, IReadOnlyList<MemoryModule> Modules);
