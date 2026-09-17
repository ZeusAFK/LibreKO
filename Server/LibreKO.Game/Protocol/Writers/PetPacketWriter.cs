using LibreKO.Common.Infrastructure.Network;

namespace LibreKO.Game.Protocol.Writers;

public sealed class PetPacketWriter
{
    public const ushort Succeeded = 1;

    public static Packet ModeChanged(PetSubOpcode sub, byte normalMode, byte mode)
    {
        var packet = Mode(sub, normalMode, mode);
        packet.WriteUShort(Succeeded);
        return packet;
    }

    public static Packet ChatChanged(PetSubOpcode sub, byte normalMode, byte chatMode, string message)
    {
        var packet = ModeChanged(sub, normalMode, chatMode);
        packet.WriteString(message);
        return packet;
    }

    public static Packet Fed(PetSubOpcode sub, byte code, ushort satisfaction, int itemId, ushort restored)
    {
        var packet = Sub(sub);
        packet.WriteByte(code);
        packet.WriteUShort(satisfaction);
        packet.WriteInt(itemId);
        packet.WriteUShort(restored);
        return packet;
    }

    public static Packet SatisfactionUpdate(PetSubOpcode sub, byte code, ushort satisfaction, int petId)
    {
        var packet = Sub(sub);
        packet.WriteByte(code);
        packet.WriteUShort(satisfaction);
        packet.WriteInt(petId);
        return packet;
    }

    public static Packet Death(PetSubOpcode sub, byte normalMode, byte code, int petId)
    {
        var packet = ModeChanged(sub, normalMode, code);
        packet.WriteInt(petId);
        return packet;
    }

    private static Packet Mode(PetSubOpcode sub, byte normalMode, byte mode)
    {
        var packet = Sub(sub);
        packet.WriteByte(normalMode);
        packet.WriteByte(mode);
        return packet;
    }

    private static Packet Sub(PetSubOpcode sub)
    {
        var packet = new Packet(GameOpcodes.GS_PET);
        packet.WriteByte((byte)sub);
        return packet;
    }
}
