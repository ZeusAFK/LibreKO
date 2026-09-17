using LibreKO.Common.Infrastructure.Network;

namespace LibreKO.Game.Protocol.Writers;

public sealed class SkillDataPacketWriter
{
    public const int MaxSlots = 64;
    public const int BytesPerSlot = 4;

    private readonly int[] _slots;

    private SkillDataPacketWriter(int[] slots) => _slots = slots;

    public static Packet Slots(byte[] skillData)
    {
        var count = skillData.Length / BytesPerSlot;
        var slots = new int[count];
        for (var index = 0; index < count; index++)
            slots[index] = BitConverter.ToInt32(skillData, index * BytesPerSlot);
        return new SkillDataPacketWriter(slots).Build();
    }

    public static Packet Cleared() => new SkillDataPacketWriter([]).Build();

    private Packet Build()
    {
        var packet = new Packet(GameOpcodes.GS_SKILLDATA);
        packet.WriteShort((short)_slots.Length);
        foreach (var slot in _slots)
            packet.WriteInt(slot);
        return packet;
    }
}
