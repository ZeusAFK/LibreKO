namespace LibreKO.Domain;

public static class GameText
{
    public const int BeginAttack = 3002;
    public const int StopAttack = 3003;
    public const int Missed = 3015;
    public const int UsingSkill = 4402;
    public const int NotEnoughMp = 3004;
    public const int Overweight = 2601;
    public const int LootNoRoom = 1708;
    public const int GoldCapNote = 6041;
    public const int Expiration = 10804;
    public const int PressStart = 6711;
    public const int DuplicateSerial = 10760;
    public const string StorageWholeStack = "Only whole stacks move inside the storage.";
    private const string Placeholder = "%s";

    public static string System(int id, string fallback, string? arg = null)
    {
        string text = ItemData.Text(id, fallback).Trim();
        return arg == null ? text : text.Replace(Placeholder, arg);
    }
}
