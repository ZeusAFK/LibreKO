using LibreKO.Common.Infrastructure.Network;

namespace LibreKO.Game.Protocol.Writers;

public sealed class GenieSystemPacketWriter
{
    public const byte InfoRequest = 1;
    public const byte UpdateRequest = 2;

    public const byte UseSpiritPotion = 1;
    public const byte LoadOptions = 2;
    public const byte SaveOptions = 3;
    public const byte Start = 4;
    public const byte Stop = 5;
    public const byte RemainingTime = 6;
    public const byte Activated = 7;

    public const byte Move = 1;
    public const byte Rotate = 2;
    public const byte MainAttack = 3;
    public const byte Magic = 4;

    public const ushort Acknowledged = 1;
    public const byte Inactive = 0;
    public const byte Active = 1;

    public const int OptionBytes = 100;

    public static Packet SpiritPotion(ushort remainingMinutes)
    {
        var packet = Info(UseSpiritPotion);
        packet.WriteUShort(remainingMinutes);
        return packet;
    }

    public static Packet Options(byte[] options)
    {
        var packet = Info(LoadOptions);
        for (var i = 0; i < OptionBytes; i++)
            packet.WriteByte(i < options.Length ? options[i] : (byte)0);
        return packet;
    }

    public static Packet Started(ushort remainingMinutes)
    {
        var packet = Info(Start);
        packet.WriteUShort(Acknowledged);
        packet.WriteUShort(remainingMinutes);
        return packet;
    }

    public static Packet Stopped(ushort remainingMinutes)
    {
        var packet = Info(Stop);
        packet.WriteUShort(Acknowledged);
        packet.WriteUShort(remainingMinutes);
        return packet;
    }

    public static Packet Remaining(ushort remainingMinutes)
    {
        var packet = Info(RemainingTime);
        packet.WriteUShort(remainingMinutes);
        return packet;
    }

    public static Packet ActivationState(ushort characterId, bool active)
    {
        var packet = Info(Activated);
        packet.WriteUShort(characterId);
        packet.WriteByte(active ? Active : Inactive);
        return packet;
    }

    private static Packet Info(byte status)
    {
        var packet = new Packet(GameOpcodes.GS_GENIE_SYSTEM);
        packet.WriteByte(InfoRequest);
        packet.WriteByte(status);
        return packet;
    }
}
