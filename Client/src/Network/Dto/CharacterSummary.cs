namespace LibreKO.Network;

public sealed class CharacterSummary
{
    public string Name = "";
    public int Race, Class, Level, Rebirth, Face, Zone;
    public long Hair;
    public int[] Gear = System.Array.Empty<int>();
    public bool Empty => Name.Length == 0;
}
