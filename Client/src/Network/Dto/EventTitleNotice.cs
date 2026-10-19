namespace LibreKO.Network;

public readonly record struct EventTitleNotice(bool Removed, string Title, string Message)
{
    public static EventTitleNotice? Read(Packet p)
    {
        if (p.RemainingBytes < 1 + sizeof(ushort)) return null;
        var sub = (EventTitleSubOpcode)p.ReadByte();
        if (sub is not (EventTitleSubOpcode.Shown or EventTitleSubOpcode.Removed or EventTitleSubOpcode.ShownWithEffect)) return null;
        string title = p.ReadString();
        if (sub == EventTitleSubOpcode.Removed) return new EventTitleNotice(true, title, "");
        string message = p.RemainingBytes >= sizeof(ushort) ? p.ReadString() : "";
        if (sub == EventTitleSubOpcode.ShownWithEffect && p.RemainingBytes >= 1) p.ReadByte();
        return new EventTitleNotice(false, title, message);
    }
}
