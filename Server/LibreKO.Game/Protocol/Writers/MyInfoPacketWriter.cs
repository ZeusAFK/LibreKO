using LibreKO.Common.Domain.Entities.GameData;
using LibreKO.Common.Infrastructure.Network;

namespace LibreKO.Game.Protocol.Writers;

public sealed class MyInfoPacketWriter
{
    private const int SkillPointDataSize = 9;
    private const byte AuthorityTrailer = byte.MaxValue;
    private const int ReservedItemRecords = 4;

    private readonly int[] _itemIds = new int[InventoryConstants.InventoryTotal];
    private readonly short[] _itemDurability = new short[InventoryConstants.InventoryTotal];
    private readonly short[] _itemCount = new short[InventoryConstants.InventoryTotal];
    private readonly byte[] _itemFlags = new byte[InventoryConstants.InventoryTotal];
    private byte[] _skillPointData = new byte[SkillPointDataSize];

    public int CharacterId { get; set; }
    public string Name { get; set; } = string.Empty;

    public short PosX { get; set; }
    public short PosZ { get; set; }
    public short PosY { get; set; }

    public byte Nation { get; set; }
    public byte Race { get; set; }
    public short Class { get; set; }
    public byte Face { get; set; }
    public int Hair { get; set; }
    public byte Rank { get; set; }
    public byte Title { get; set; }
    public byte Level { get; set; }
    public short StatPoints { get; set; }

    public long MaxExperience { get; set; }
    public long Experience { get; set; }
    public int Loyalty { get; set; }
    public int LoyaltyMonthly { get; set; }

    public short KnightsId { get; set; }
    public byte ClanFame { get; set; }
    public short AllianceId { get; set; }
    public byte ClanFlag { get; set; }
    public string ClanName { get; set; } = string.Empty;
    public byte ClanGrade { get; set; }
    public byte ClanRanking { get; set; }
    public short ClanCape { get; set; }
    public byte CapeR { get; set; }
    public byte CapeG { get; set; }
    public byte CapeB { get; set; }
    public ushort NoClanCapeId { get; set; }

    public short MaxHp { get; set; }
    public short Hp { get; set; }
    public short MaxMp { get; set; }
    public short Mp { get; set; }
    public int MaxWeight { get; set; }
    public int ItemWeight { get; set; }

    public byte Strength { get; set; }
    public byte StrengthBonus { get; set; }
    public byte Stamina { get; set; }
    public byte StaminaBonus { get; set; }
    public byte Dexterity { get; set; }
    public byte DexterityBonus { get; set; }
    public byte Intelligence { get; set; }
    public byte IntelligenceBonus { get; set; }
    public byte Magic { get; set; }
    public byte MagicBonus { get; set; }

    public short TotalHit { get; set; }
    public short TotalAc { get; set; }

    public byte FireResistance { get; set; }
    public byte ColdResistance { get; set; }
    public byte LightningResistance { get; set; }
    public byte MagicResistance { get; set; }
    public byte DiseaseResistance { get; set; }
    public byte PoisonResistance { get; set; }

    public int Money { get; set; }
    public byte Authority { get; set; }

    public bool HasPremium { get; set; }
    public byte PremiumRecordCount { get; set; }
    public byte PremiumId { get; set; }
    public bool IsChicken { get; set; }

    public int MannerPoints { get; set; }
    public byte KarusMilitary { get; set; }
    public byte HumanMilitary { get; set; }
    public byte KarusEslantMilitary { get; set; }
    public byte HumanEslantMilitary { get; set; }
    public byte MoradonMilitary { get; set; }
    public short GenieTime { get; set; }
    public byte RebirthLevel { get; set; }
    public long SealedExperience { get; set; }
    public short CoverTitle { get; set; }
    public short SkillTitle { get; set; }
    public int ReturnStatus { get; set; }

    public bool HasClan { get; set; }

    public void SetPosition(short x, short z, short y)
    {
        PosX = x;
        PosZ = z;
        PosY = y;
    }

    public void SetVitals(short hp, short maxHp, short mp, short maxMp)
    {
        Hp = hp;
        MaxHp = maxHp;
        Mp = mp;
        MaxMp = maxMp;
    }

    public void SetPrimaryStats(
        byte strength, byte strengthBonus,
        byte stamina, byte staminaBonus,
        byte dexterity, byte dexterityBonus,
        byte intelligence, byte intelligenceBonus,
        byte magic, byte magicBonus)
    {
        Strength = strength;
        StrengthBonus = strengthBonus;
        Stamina = stamina;
        StaminaBonus = staminaBonus;
        Dexterity = dexterity;
        DexterityBonus = dexterityBonus;
        Intelligence = intelligence;
        IntelligenceBonus = intelligenceBonus;
        Magic = magic;
        MagicBonus = magicBonus;
    }

    public void SetResistances(byte fire, byte cold, byte lightning, byte magic, byte disease, byte poison)
    {
        FireResistance = fire;
        ColdResistance = cold;
        LightningResistance = lightning;
        MagicResistance = magic;
        DiseaseResistance = disease;
        PoisonResistance = poison;
    }

    public void SetClan(
        short knightsId, byte fame, short allianceId, byte flag, string name,
        byte grade, byte ranking, short cape, byte capeR, byte capeG, byte capeB)
    {
        HasClan = true;
        KnightsId = knightsId;
        ClanFame = fame;
        AllianceId = allianceId;
        ClanFlag = flag;
        ClanName = name;
        ClanGrade = grade;
        ClanRanking = ranking;
        ClanCape = cape;
        CapeR = capeR;
        CapeG = capeG;
        CapeB = capeB;
    }

    public void SetSkillPointData(byte[]? data)
    {
        _skillPointData = new byte[SkillPointDataSize];
        if (data is not null)
            Array.Copy(data, _skillPointData, Math.Min(data.Length, SkillPointDataSize));
    }

    public void SetItem(int storageSlot, int itemId, short durability, short count, byte flag)
    {
        if (storageSlot < 0 || storageSlot >= InventoryConstants.InventoryTotal)
            return;

        _itemIds[storageSlot] = itemId;
        _itemDurability[storageSlot] = durability;
        _itemCount[storageSlot] = count;
        _itemFlags[storageSlot] = flag;
    }

    public Packet Build()
    {
        var packet = new Packet(GameOpcodes.GS_MYINFO);

        packet.WriteInt(CharacterId);
        packet.WriteSByteString(Name);

        packet.WriteShort(PosX);
        packet.WriteShort(PosZ);
        packet.WriteShort(PosY);

        packet.WriteByte(Nation);
        packet.WriteByte(Race);
        packet.WriteShort(Class);
        packet.WriteByte(Face);
        packet.WriteInt(Hair);

        packet.WriteByte(Rank);
        packet.WriteByte(Title);
        packet.WriteByte(0);
        packet.WriteByte(0);

        packet.WriteByte(Level);
        packet.WriteShort(StatPoints);

        packet.WriteLong(MaxExperience);
        packet.WriteLong(Experience);

        packet.WriteInt(Loyalty);
        packet.WriteInt(LoyaltyMonthly);

        packet.WriteShort(KnightsId);
        packet.WriteByte(ClanFame);

        if (HasClan)
        {
            packet.WriteShort(AllianceId);
            packet.WriteByte(ClanFlag);
            packet.WriteSByteString(ClanName);
            packet.WriteByte(ClanGrade);
            packet.WriteByte(ClanRanking);
            packet.WriteShort(0);
            packet.WriteShort(ClanCape);
            packet.WriteByte(ClanCape > 0 ? CapeR : (byte)0);
            packet.WriteByte(ClanCape > 0 ? CapeG : (byte)0);
            packet.WriteByte(ClanCape > 0 ? CapeB : (byte)0);
            packet.WriteByte(0);
        }
        else
        {
            packet.WriteLong(0);
            packet.WriteUShort(NoClanCapeId);
            packet.WriteInt(0);
        }

        packet.WriteLong(0);

        packet.WriteShort(MaxHp);
        packet.WriteShort(Hp);
        packet.WriteShort(MaxMp);
        packet.WriteShort(Mp);

        packet.WriteInt(MaxWeight);
        packet.WriteInt(ItemWeight);

        packet.WriteByte(Strength);
        packet.WriteByte(StrengthBonus);
        packet.WriteByte(Stamina);
        packet.WriteByte(StaminaBonus);
        packet.WriteByte(Dexterity);
        packet.WriteByte(DexterityBonus);
        packet.WriteByte(Intelligence);
        packet.WriteByte(IntelligenceBonus);
        packet.WriteByte(Magic);
        packet.WriteByte(MagicBonus);

        packet.WriteShort(TotalHit);
        packet.WriteShort(TotalAc);

        packet.WriteByte(FireResistance);
        packet.WriteByte(ColdResistance);
        packet.WriteByte(LightningResistance);
        packet.WriteByte(MagicResistance);
        packet.WriteByte(DiseaseResistance);
        packet.WriteByte(PoisonResistance);

        packet.WriteInt(Money);
        packet.WriteByte(Authority);
        packet.WriteByte(AuthorityTrailer);
        packet.WriteByte(AuthorityTrailer);

        packet.WriteBytes(_skillPointData);

        WriteItems(packet);

        packet.WriteByte(HasPremium ? (byte)1 : (byte)0);
        packet.WriteByte(PremiumRecordCount);
        packet.WriteByte(PremiumId);
        packet.WriteByte(IsChicken ? (byte)1 : (byte)0);

        packet.WriteInt(MannerPoints);
        packet.WriteByte(KarusMilitary);
        packet.WriteByte(HumanMilitary);
        packet.WriteByte(KarusEslantMilitary);
        packet.WriteByte(HumanEslantMilitary);
        packet.WriteByte(MoradonMilitary);
        packet.WriteByte(0);
        packet.WriteShort(GenieTime);
        packet.WriteByte(RebirthLevel);
        packet.WriteByte(0);
        packet.WriteByte(0);
        packet.WriteByte(0);
        packet.WriteByte(0);
        packet.WriteByte(0);
        packet.WriteLong(SealedExperience);
        packet.WriteShort(CoverTitle);
        packet.WriteShort(SkillTitle);
        packet.WriteInt(ReturnStatus);
        packet.WriteShort(0);
        packet.WriteByte(0);
        packet.WriteShort(0);

        return packet;
    }

    private void WriteItems(Packet packet)
    {
        for (var wireIndex = 0; wireIndex < InventoryConstants.MyInfoWireTotal; wireIndex++)
        {
            var storageSlot = InventoryConstants.MyInfoWireSlot(wireIndex);
            WriteItemRecord(
                packet,
                _itemIds[storageSlot],
                _itemDurability[storageSlot],
                _itemCount[storageSlot],
                _itemFlags[storageSlot]);
        }

        for (var reserved = 0; reserved < ReservedItemRecords; reserved++)
            WriteItemRecord(packet, 0, 0, 0, 0);
    }

    private static void WriteItemRecord(
        Packet packet, int itemId, short durability, short count, byte flag)
    {
        packet.WriteInt(itemId);
        packet.WriteShort(durability);
        packet.WriteShort(count);
        packet.WriteByte(flag);
        packet.WriteShort(0);
        packet.WriteInt(0);
        packet.WriteInt(0);
    }
}
