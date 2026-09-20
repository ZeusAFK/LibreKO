using LibreKO.Common.Domain.Entities;
using LibreKO.Common.Domain.Entities.GameData;
using LibreKO.Common.Enums;
using LibreKO.Common.Domain.Services;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.Protocol.Writers;

using LibreKO.Game.World;

namespace LibreKO.Game.Protocol;

public sealed record MyInfoPacketContext(
    Character Character,
    Account Account,
    long MaxExperience,
    DerivedStats Stats,
    KnightsEntity? Clan,
    short AllianceId,
    byte ClanFame,
    short ZoneId,
    short PosX,
    short PosZ,
    short PosY,
    short PremiumHours);

public sealed record SelectCharacterPacketContext(
    short ZoneId,
    short PosX,
    short PosZ,
    short PosY,
    byte Nation);

public class CharacterPacketMapper
{
    private const int ChickenLevelLimit = 30;
    private const ushort NoCapeId = ushort.MaxValue;
    private const ushort GameMasterCapeId = 99;

    public static Packet BuildAllCharacterInfo(IReadOnlyCollection<Character> characters)
    {
        var bySlot = new PreGamePacketWriter.CharacterSlot?[GameConstants.MaxAccountCharacters];
        foreach (var character in characters)
        {
            if (character.Slot < bySlot.Length)
                bySlot[character.Slot] = SlotOf(character);
        }

        return PreGamePacketWriter.AllCharacterInfo(bySlot);
    }

    private static byte WireByte(int value) => (byte)Math.Clamp(value, 0, byte.MaxValue);

    private static PreGamePacketWriter.CharacterSlot SlotOf(Character character)
    {
        var inventory = new byte[InventoryConstants.InventoryTotal * 8];
        if (character.Items.Length > 0)
            Buffer.BlockCopy(character.Items, 0, inventory, 0,
                Math.Min(character.Items.Length, inventory.Length));

        var equipment = new List<PreGamePacketWriter.CharSelectItem>(
            InventoryConstants.CharacterListVisualSlots.Length);
        foreach (var slot in InventoryConstants.CharacterListVisualSlots)
        {
            var offset = slot * 8;
            equipment.Add(new PreGamePacketWriter.CharSelectItem(
                BitConverter.ToInt32(inventory, offset),
                BitConverter.ToInt16(inventory, offset + 4)));
        }

        return new PreGamePacketWriter.CharacterSlot(
            character.Name,
            character.Race,
            character.Class,
            character.Level,
            WireByte(character.RebirthLevel),
            character.Face,
            character.Hair,
            character.MapId,
            equipment);
    }

    public static Packet BuildSelectCharacterSuccess(SelectCharacterPacketContext context) =>
        PreGamePacketWriter.SelectCharacterSuccess(
            (byte)SelectCharacterResult.Success,
            context.ZoneId,
            context.PosX,
            context.PosZ,
            context.PosY,
            context.Nation);

    public static Packet BuildMyInfo(MyInfoPacketContext context)
    {
        var maxHp = (short)Math.Max(1, context.Stats.MaxHp > 0 ? context.Stats.MaxHp : context.Character.Hp);
        var currentHp = context.Character.Hp > 0
            ? (short)Math.Min(context.Character.Hp, maxHp)
            : maxHp;
        var maxMp = (short)Math.Max(0, context.Stats.MaxMp > 0 ? context.Stats.MaxMp : context.Character.Mp);
        var currentMp = (short)Math.Clamp(context.Character.Mp, (short)0, maxMp);

        var writer = new MyInfoPacketWriter
        {
            CharacterId = context.Character.Id,
            Name = context.Character.Name,
            Nation = (byte)context.Account.Nation,
            Race = context.Character.Race,
            Class = context.Character.Class,
            Face = context.Character.Face,
            Hair = context.Character.Hair,
            Level = context.Character.Level,
            StatPoints = context.Character.StatPoints,
            MaxExperience = context.MaxExperience,
            Experience = context.Character.Experience,
            Loyalty = context.Character.Loyalty,
            LoyaltyMonthly = context.Character.LoyaltyMonthly,
            MaxWeight = context.Stats.MaxWeight,
            ItemWeight = context.Stats.ItemWeight,
            TotalHit = (short)context.Stats.TotalHit,
            TotalAc = context.Stats.TotalAc,
            Money = context.Character.Money,
                KnightCash = context.Account.KnightCash,
            Authority = (byte)context.Account.Authority,
            NoClanCapeId = GetNoClanMyInfoCapeId(context.Account),
            HasPremium = context.PremiumHours > 0,
            GenieTime = context.Character.GenieHours,
            IsChicken = context.Character.Level < ChickenLevelLimit,
            RebirthLevel = WireByte(context.Character.RebirthLevel),
        };

        writer.SetPosition(context.PosX, context.PosZ, context.PosY);
        writer.SetVitals(currentHp, maxHp, currentMp, maxMp);

        writer.SetPrimaryStats(
            context.Character.Strength, WireByte(context.Stats.StrBonus),
            context.Character.Stamina, WireByte(context.Stats.StaBonus),
            context.Character.Dexterity, WireByte(context.Stats.DexBonus),
            context.Character.Intelligence, WireByte(context.Stats.IntBonus),
            context.Character.Magic, WireByte(context.Stats.ChaBonus));

        writer.SetResistances(
            WireByte(context.Stats.FireR), WireByte(context.Stats.ColdR),
            WireByte(context.Stats.LightningR), WireByte(context.Stats.MagicR),
            WireByte(context.Stats.DiseaseR), WireByte(context.Stats.PoisonR));

        if (context.Clan != null)
        {
            var clanCape = context.Clan.Cape > 0 ? context.Clan.Cape : (short)-1;
            writer.SetClan(
                context.Character.KnightsId,
                context.ClanFame,
                context.AllianceId,
                context.Clan.Flag,
                context.Clan.Name,
                CalculateClanGrade(context.Clan.Points),
                context.Clan.Grade,
                clanCape,
                context.Clan.CapeR,
                context.Clan.CapeG,
                context.Clan.CapeB);
        }
        else
        {
            writer.ClanFame = context.ClanFame;
        }

        writer.SetSkillPointData(context.Character.SkillPointData);

        for (var storageSlot = 0; storageSlot < InventoryConstants.InventoryTotal; storageSlot++)
        {
            if (TryReadInventoryItem(
                    context.Character.Items, storageSlot,
                    out var itemId, out var durability, out var count, out var flag))
                writer.SetItem(storageSlot, itemId, durability, count, flag);
        }

        return writer.Build();
    }

    private static ushort GetNoClanMyInfoCapeId(Account account)
    {
        return account.Authority == AccountAuthority.GameMaster ? GameMasterCapeId : NoCapeId;
    }

    private static byte CalculateClanGrade(int points)
    {
        var clanPoints = Math.Max(0, points) / KnightsPacketConstants.MaxClanUsers;

        if (clanPoints >= 20000)
            return 1;

        if (clanPoints >= 10000)
            return 2;

        if (clanPoints >= 5000)
            return 3;

        if (clanPoints >= 2000)
            return 4;

        return 5;
    }


    private static bool TryReadInventoryItem(
        byte[] inventoryData,
        int slot,
        out int itemId,
        out short durability,
        out short count,
        out byte flag)
    {
        itemId = 0;
        durability = 0;
        count = 0;
        flag = 0;

        var stride = UserSessionBinaryState.BytesPerItem;
        if (slot < 0 || inventoryData.Length < (slot + 1) * stride)
            return false;

        var itemOffset = slot * stride;
        itemId = BitConverter.ToInt32(inventoryData, itemOffset);
        durability = BitConverter.ToInt16(inventoryData, itemOffset + 4);
        count = BitConverter.ToInt16(inventoryData, itemOffset + 6);

        var flagOffset = InventoryConstants.InventoryTotal * stride + slot;
        if (flagOffset < inventoryData.Length)
            flag = inventoryData[flagOffset];

        return true;
    }
}
