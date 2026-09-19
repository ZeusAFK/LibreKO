using LibreKO.Common.Infrastructure.Network;

namespace LibreKO.Game.Protocol.Writers;

public sealed class AdminPanelPacketWriter
{
    public const byte Denied = 0;
    public const byte Granted = 1;
    public const int SkillCategoryCount = 9;

    public readonly record struct State(
        short Class,
        byte Race,
        byte Level,
        byte Strength,
        byte Stamina,
        byte Dexterity,
        byte Intelligence,
        byte Magic,
        short StatPoints,
        short MaxHp,
        short MaxMp,
        short TotalHit,
        short TotalAc,
        int Money,
        IReadOnlyList<byte> SkillPoints,
        IReadOnlyList<short> ClassOptions);

    public static Packet GmFx(int characterId, bool enabled)
    {
        var packet = Sub(0x13);
        packet.WriteInt(characterId);
        packet.WriteByte(enabled ? Granted : Denied);
        return packet;
    }

    public static Packet Grant(byte sub, byte grant, bool speedGranted)
    {
        var packet = Sub(sub);
        packet.WriteByte(grant);
        packet.WriteByte((byte)(speedGranted ? Granted : Denied));
        return packet;
    }

    public static Packet StateDenied(byte sub)
    {
        var packet = Sub(sub);
        packet.WriteByte(Denied);
        return packet;
    }

    public static Packet StateGranted(byte sub, State state)
    {
        var packet = Sub(sub);
        packet.WriteByte(Granted);
        packet.WriteShort(state.Class);
        packet.WriteByte(state.Race);
        packet.WriteByte(state.Level);
        packet.WriteByte(state.Strength);
        packet.WriteByte(state.Stamina);
        packet.WriteByte(state.Dexterity);
        packet.WriteByte(state.Intelligence);
        packet.WriteByte(state.Magic);
        packet.WriteShort(state.StatPoints);
        packet.WriteShort(state.MaxHp);
        packet.WriteShort(state.MaxMp);
        packet.WriteShort(state.TotalHit);
        packet.WriteShort(state.TotalAc);
        packet.WriteInt(state.Money);

        for (var i = 0; i < SkillCategoryCount; i++)
            packet.WriteByte(state.SkillPoints[i]);

        packet.WriteByte((byte)state.ClassOptions.Count);
        foreach (var option in state.ClassOptions)
            packet.WriteShort(option);

        return packet;
    }

    public static Packet Result(byte sub, bool ok, string message)
    {
        var packet = Sub(sub);
        packet.WriteByte((byte)(ok ? Granted : Denied));
        packet.WriteString(message);
        return packet;
    }

    private static Packet Sub(byte sub)
    {
        var packet = new Packet(GameOpcodes.GS_ADMIN_PANEL);
        packet.WriteByte(sub);
        return packet;
    }
}
