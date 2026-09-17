using LibreKO.Common.Infrastructure.Network;

namespace LibreKO.Game.Protocol.Writers;

public enum AttackResult : byte
{
    Failed = 0,
    Succeeded = 1,
    TargetDead = 2,
}

public sealed class AttackPacketWriter
{
    public const byte TypeMelee = 1;

    public const byte NoCritical = 0;

    public static Packet Create(
        byte attackType, AttackResult result, int attackerId, int targetId, byte criticalHit = NoCritical)
    {
        var packet = new Packet(GameOpcodes.GS_ATTACK);
        packet.WriteByte(attackType);
        packet.WriteByte((byte)result);
        packet.WriteInt(attackerId);
        packet.WriteInt(targetId);
        packet.WriteByte(criticalHit);
        return packet;
    }
}
