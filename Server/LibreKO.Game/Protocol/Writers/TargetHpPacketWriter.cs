using LibreKO.Common.Infrastructure.Network;

namespace LibreKO.Game.Protocol.Writers;

public sealed class TargetHpPacketWriter
{
    public const byte EchoNone = 0;
    public const byte EchoPoll = 1;

    public int TargetId { get; init; }
    public byte Echo { get; init; }
    public int MaxHp { get; init; }
    public int Hp { get; init; }
    public int Damage { get; init; }

    public static Packet For(int targetId, int maxHp, int hp, int damage) =>
        new TargetHpPacketWriter
        {
            TargetId = targetId,
            Echo = EchoNone,
            MaxHp = maxHp,
            Hp = hp,
            Damage = damage == 0 ? 0 : -Math.Abs(damage),
        }.Build();

    public static Packet Polled(int targetId, byte echo, int maxHp, int hp, int damage) =>
        new TargetHpPacketWriter
        {
            TargetId = targetId,
            Echo = echo,
            MaxHp = maxHp,
            Hp = hp,
            Damage = damage,
        }.Build();

    private Packet Build()
    {
        var packet = new Packet(GameOpcodes.GS_TARGET_HP);
        packet.WriteInt(TargetId);
        packet.WriteByte(Echo);
        packet.WriteInt(MaxHp);
        packet.WriteInt(Hp);
        packet.WriteInt(Damage);
        packet.WriteInt(0);
        return packet;
    }
}
