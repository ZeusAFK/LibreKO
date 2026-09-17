using Godot;

namespace LibreKO.Domain;

[GlobalClass]
public partial class KoTable : Resource
{
    [Export] public string TableName { get; set; } = "";

    [Export] public int[] ColumnTypes { get; set; } = System.Array.Empty<int>();

    [Export] public Godot.Collections.Array Rows { get; set; } = new();
}
