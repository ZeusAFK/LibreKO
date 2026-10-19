namespace LibreKO.Network;

public readonly record struct PartyBoardInvite(string Inviter, int Level, int Class)
{
    public const int PromptText = 3422;
    public const string PromptFallback = "ID: %s,  Job: %s has invited you into a party.\nWill you accept?";
    private const byte Succeeded = 1;
    private const string Slot = "%s";

    public static PartyBoardInvite? Read(Packet p)
    {
        if (p.RemainingBytes < 1 || p.ReadByte() != Succeeded || p.RemainingBytes < sizeof(short)) return null;
        string inviter = p.ReadString();
        if (p.RemainingBytes < 1 + sizeof(short)) return null;
        return new PartyBoardInvite(inviter, p.ReadByte(), p.ReadShort());
    }

    public string Prompt(string template, string className)
    {
        int first = template.IndexOf(Slot, System.StringComparison.Ordinal);
        if (first < 0) return template;
        string text = template.Remove(first, Slot.Length).Insert(first, Inviter);
        int second = text.IndexOf(Slot, first + Inviter.Length, System.StringComparison.Ordinal);
        return second < 0 ? text : text.Remove(second, Slot.Length).Insert(second, className);
    }
}
