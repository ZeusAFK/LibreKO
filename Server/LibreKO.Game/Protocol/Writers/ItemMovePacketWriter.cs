using LibreKO.Common.Infrastructure.Network;

namespace LibreKO.Game.Protocol.Writers;

public enum ArrangeResult : byte
{
    Failed = 0,
    Succeeded = 1,
}

public sealed class ItemMovePacketWriter
{
    public const int InventoryEntryBytes = 19;

    public sealed class StatBlock
    {
        public short TotalHit { get; set; }
        public short TotalAc { get; set; }
        public int MaxWeight { get; set; }
        public short MaxHp { get; set; }
        public short MaxMp { get; set; }
        public short StrengthBonus { get; set; }
        public short StaminaBonus { get; set; }
        public short DexterityBonus { get; set; }
        public short IntelligenceBonus { get; set; }
        public short CharismaBonus { get; set; }
        public short FireResistance { get; set; }
        public short ColdResistance { get; set; }
        public short LightningResistance { get; set; }
        public short MagicResistance { get; set; }
        public short DiseaseResistance { get; set; }
        public short PoisonResistance { get; set; }
    }

    public readonly record struct InventoryEntry(
        int ItemId,
        ushort Durability,
        ushort Count,
        byte Flag,
        ushort RentalMinutes,
        int Serial,
        int ExpiresAt);

    private readonly List<InventoryEntry> _inventory = [];

    public byte Command { get; set; }
    public byte SubCommand { get; set; }
    public StatBlock? Stats { get; set; }

    public static ItemMovePacketWriter Move(byte subCommand) =>
        new() { Command = (byte)ItemMoveSubOpcode.Move, SubCommand = subCommand };

    public static ItemMovePacketWriter Arranged() =>
        new() { Command = (byte)ItemMoveSubOpcode.ArrangeInventory, SubCommand = (byte)ArrangeResult.Succeeded };

    public static ItemMovePacketWriter ArrangeRefused() =>
        new() { Command = (byte)ItemMoveSubOpcode.ArrangeInventory, SubCommand = (byte)ArrangeResult.Failed };

    public ItemMovePacketWriter AddInventorySlot(int itemId, short durability, ushort count, byte flag)
    {
        _inventory.Add(new InventoryEntry(
            itemId,
            durability < 0 ? (ushort)0 : (ushort)durability,
            count,
            flag,
            RentalMinutes: 0,
            Serial: 0,
            ExpiresAt: 0));

        return this;
    }

    public Packet Build()
    {
        var packet = new Packet(GameOpcodes.GS_ITEM_MOVE);
        packet.WriteByte(Command);
        packet.WriteByte(SubCommand);

        if (Command == (byte)ItemMoveSubOpcode.ArrangeInventory)
        {
            foreach (var entry in _inventory)
            {
                packet.WriteInt(entry.ItemId);
                packet.WriteUShort(entry.Durability);
                packet.WriteUShort(entry.Count);
                packet.WriteByte(entry.Flag);
                packet.WriteUShort(entry.RentalMinutes);
                packet.WriteInt(entry.Serial);
                packet.WriteInt(entry.ExpiresAt);
            }

            return packet;
        }

        if (SubCommand == (byte)ItemMoveSubOpcode.Failed || Stats is null)
            return packet;

        packet.WriteShort(Stats.TotalHit);
        packet.WriteShort(Stats.TotalAc);
        packet.WriteInt(Stats.MaxWeight);
        packet.WriteByte(0);
        packet.WriteByte(0);
        packet.WriteShort(Stats.MaxHp);
        packet.WriteShort(Stats.MaxMp);
        packet.WriteShort(Stats.StrengthBonus);
        packet.WriteShort(Stats.StaminaBonus);
        packet.WriteShort(Stats.DexterityBonus);
        packet.WriteShort(Stats.IntelligenceBonus);
        packet.WriteShort(Stats.CharismaBonus);
        packet.WriteShort(Stats.FireResistance);
        packet.WriteShort(Stats.ColdResistance);
        packet.WriteShort(Stats.LightningResistance);
        packet.WriteShort(Stats.MagicResistance);
        packet.WriteShort(Stats.DiseaseResistance);
        packet.WriteShort(Stats.PoisonResistance);

        return packet;
    }
}
