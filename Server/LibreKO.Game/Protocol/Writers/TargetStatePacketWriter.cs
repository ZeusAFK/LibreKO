using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.World;

namespace LibreKO.Game.Protocol.Writers;

public readonly record struct TargetEffect(int SkillId, ushort RemainingSeconds);

public readonly record struct ControlEffect(int SkillId, byte BuffType, int RemainingMs);

public readonly record struct ControlState(byte Flags, byte MovePercent, short AttackPercent, IReadOnlyList<ControlEffect> Effects);

public static class TargetStatePacketWriter
{
    public const byte SubDropList = 1;
    public const byte SubEffects = 2;
    public const byte DropListShown = 1;
    public const byte DropListRefused = 0;
    public const byte SubControl = 3;
    public const int MaxEffects = 20;

    public static Packet DropList(int targetId, DropSummary? summary)
    {
        var packet = new Packet(GameOpcodes.GS_TARGET_STATE);
        packet.WriteByte(SubDropList);
        packet.WriteInt(targetId);
        if (summary == null)
        {
            packet.WriteByte(DropListRefused);
            return packet;
        }

        packet.WriteByte(DropListShown);
        packet.WriteInt(summary.CoinMin);
        packet.WriteInt(summary.CoinMax);
        packet.WriteInt(summary.Experience);
        packet.WriteByte((byte)summary.Items.Count);
        foreach (var line in summary.Items)
        {
            packet.WriteInt(line.ItemId);
            packet.WriteByte((byte)line.Band);
        }
        packet.WriteUShort((ushort)Math.Min(summary.Total, ushort.MaxValue));
        return packet;
    }

    public static Packet Effects(int targetId, IReadOnlyList<TargetEffect> effects)
    {
        var packet = new Packet(GameOpcodes.GS_TARGET_STATE);
        packet.WriteByte(SubEffects);
        packet.WriteInt(targetId);
        var count = Math.Min(effects.Count, MaxEffects);
        packet.WriteByte((byte)count);
        for (var index = 0; index < count; index++)
        {
            packet.WriteInt(effects[index].SkillId);
            packet.WriteUShort(effects[index].RemainingSeconds);
        }
        return packet;
    }

    public static Packet Control(int unitId, ControlState state)
    {
        var packet = new Packet(GameOpcodes.GS_TARGET_STATE);
        packet.WriteByte(SubControl);
        packet.WriteInt(unitId);
        packet.WriteByte(state.Flags);
        packet.WriteByte(state.MovePercent);
        packet.WriteShort(state.AttackPercent);
        var count = Math.Min(state.Effects.Count, MaxEffects);
        packet.WriteByte((byte)count);
        for (var index = 0; index < count; index++)
        {
            packet.WriteInt(state.Effects[index].SkillId);
            packet.WriteByte(state.Effects[index].BuffType);
            packet.WriteInt(state.Effects[index].RemainingMs);
        }
        return packet;
    }
}
