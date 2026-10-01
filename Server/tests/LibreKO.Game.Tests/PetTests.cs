using FluentAssertions;
using LibreKO.Common.Domain.Entities;
using LibreKO.Common.Domain.Entities.GameData;
using LibreKO.Common.Domain.Services;
using LibreKO.Common.Enums;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.Protocol;
using LibreKO.Game.Protocol.Writers;
using LibreKO.Game.World;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace LibreKO.Game.Tests;

public class PetTests : GameTestBase
{
    private const byte Zone = (byte)ZoneId.Moradon;
    private const int EggId = 600_001_000;
    private const int KaulId = PetService.HatchedPetItem;
    private const int LeafId = 389_570_000;
    private const short LeafSatisfaction = 200;
    private const byte KaulClass = 101;
    private const byte KaulItemSlot = 20;
    private const int KateNpcId = 13016;
    private const int MonsterExperience = 400;
    private const int SturdyMonsterHp = 100_000;
    private const int FrailMonsterHp = 1;
    private const string PetName = "Kauly";
    private const int FamiliarSummonScroll = 389_191_000;

    private static readonly PetLevelData LevelOne = new()
    {
        Level = 1, MaxHp = 35, MaxMp = 20, Attack = 5, Defence = 0, Resist = 0, Exp = 50,
    };

    private static readonly PetLevelData LevelTwo = new()
    {
        Level = 2, MaxHp = 41, MaxMp = 30, Attack = 7, Defence = 10, Resist = 2, Exp = 100,
    };

    [Fact]
    public void APetItemRecordCarriesTheFamiliarBlockAfterItsUniqueId()
    {
        var packet = new Packet(GameOpcodes.GS_MYINFO);
        ItemRecordWriter.Write(packet, KaulId, 1, 1, 0, new PetItemInfo(42, PetName, 5, 3, 1234, 9000));

        packet.ResetOffset();
        packet.ReadInt().Should().Be(KaulId);
        packet.ReadShort().Should().Be(1);
        packet.ReadShort().Should().Be(1);
        packet.ReadByte().Should().Be(0);
        packet.ReadShort().Should().Be(ItemRecordWriter.NoRentalMinutes);
        packet.ReadInt().Should().Be(42, "a non-zero unique id tells the client a familiar block follows");
        packet.ReadString().Should().Be(PetName);
        packet.ReadByte().Should().Be(5);
        packet.ReadByte().Should().Be(3);
        packet.ReadUShort().Should().Be(1234);
        packet.ReadShort().Should().Be(9000);
        packet.ReadByte().Should().Be(ItemRecordWriter.PetBlockTail);
        packet.ReadInt().Should().Be(ItemRecordWriter.NoExpiry);
        packet.RemainingBytes.Should().Be(0);
    }

    [Fact]
    public void AnItemWithoutAFamiliarKeepsTheNineteenByteRecord()
    {
        var packet = new Packet(GameOpcodes.GS_MYINFO);
        ItemRecordWriter.Write(packet, EggId, 1, 1, 0, null);

        packet.GetLength().Should().Be(MyInfoItemEntrySize);
    }

    [Fact]
    public void APetNpcRecordNamesItsOwnerAndItselfAfterTheWeapons()
    {
        var packet = new Packet(GameOpcodes.GS_NPC_INOUT);
        NpcSpawnPacketWriter.WriteRecord(packet, new NpcSpawnPacketWriter.NpcState(
            7, 0, false, PetService.HatchedModelId, 0, NpcData.TypePet, PetService.HatchedSize, 0, 0,
            (byte)AccountNation.Karus, 1, 100, 100, 0, 0, 0, 0, "Owner", PetName));

        packet.ResetOffset();
        packet.ReadShort().Should().Be(0);
        packet.ReadByte().Should().Be((byte)NpcSpawnKind.Npc);
        packet.ReadShort().Should().Be(PetService.HatchedModelId);
        packet.ReadInt();
        packet.ReadByte().Should().Be(NpcData.TypePet);
        packet.ReadInt();
        packet.ReadShort().Should().Be(PetService.HatchedSize);
        packet.ReadInt();
        packet.ReadInt();
        packet.ReadSByteString().Should().Be("Owner");
        packet.ReadSByteString().Should().Be(PetName);
        packet.ReadInt().Should().Be(NpcSpawnPacketWriter.PetRecordTail);
        packet.ReadByte().Should().Be((byte)AccountNation.Karus, "the nation follows the familiar's names");
    }

    [Fact]
    public async Task HatchingTurnsTheEggIntoAKaulLinkedToANewFamiliar()
    {
        using var provider = Provider();
        var sessions = provider.GetRequiredService<SessionManager>();
        var (owner, sent) = Player(sessions, 700);
        var kate = Kate(sessions);
        PutEgg(owner, 0);

        await provider.GetRequiredService<IPetService>().HatchAsync(owner, kate.NpcId, EggId, 0, PetName);

        var slot = owner.Inventory[InventoryConstants.SlotMax];
        slot.ItemId.Should().Be(KaulId);
        slot.IsLinked.Should().BeTrue();
        var pet = await Repository(provider).GetById(slot.UniqueId);
        pet.Should().NotBeNull();
        pet!.Name.Should().Be(PetName);
        pet.Level.Should().Be(Pet.StartLevel);
        pet.Satisfaction.Should().Be(Pet.HatchedSatisfaction);
        pet.Class.Should().Be(KaulClass);

        var reply = sent.Single(p => p.GetOpcode() == (byte)GameOpcodes.GS_ITEM_UPGRADE);
        reply.ResetOffset();
        reply.ReadByte().Should().Be((byte)ItemUpgradeSubOpcode.PetHatching);
        reply.ReadByte().Should().Be((byte)HatchResult.Succeeded);
        reply.ReadInt().Should().Be(KaulId);
        reply.ReadByte().Should().Be(0);
        reply.ReadInt().Should().Be(pet.Id);
        reply.ReadString().Should().Be(PetName);
        reply.ReadByte().Should().Be(KaulClass);
        reply.ReadByte().Should().Be(Pet.StartLevel);
        reply.ReadUShort().Should().Be(0);
        reply.ReadShort().Should().Be(Pet.HatchedSatisfaction);
    }

    [Fact]
    public async Task ATakenNameIsRefusedAndTheEggStays()
    {
        using var provider = Provider();
        var sessions = provider.GetRequiredService<SessionManager>();
        var (owner, sent) = Player(sessions, 700);
        var kate = Kate(sessions);
        PutEgg(owner, 0);
        PutEgg(owner, 1);
        var pets = provider.GetRequiredService<IPetService>();

        await pets.HatchAsync(owner, kate.NpcId, EggId, 0, PetName);
        sent.Clear();
        await pets.HatchAsync(owner, kate.NpcId, EggId, 1, PetName);

        owner.Inventory[InventoryConstants.SlotMax + 1].ItemId.Should().Be(EggId);
        var reply = sent.Single(p => p.GetOpcode() == (byte)GameOpcodes.GS_ITEM_UPGRADE);
        reply.ResetOffset();
        reply.ReadByte();
        reply.ReadByte().Should().Be((byte)HatchResult.NameTaken);
    }

    [Theory]
    [InlineData("", HatchRefusal.InvalidName)]
    [InlineData("SixteenLettersXy", HatchRefusal.InvalidName)]
    [InlineData("two words", HatchRefusal.InvalidName)]
    public async Task ABadNameIsRefused(string name, HatchRefusal refusal)
    {
        using var provider = Provider();
        var sessions = provider.GetRequiredService<SessionManager>();
        var (owner, sent) = Player(sessions, 700);
        var kate = Kate(sessions);
        PutEgg(owner, 0);

        await provider.GetRequiredService<IPetService>().HatchAsync(owner, kate.NpcId, EggId, 0, name);

        owner.Inventory[InventoryConstants.SlotMax].ItemId.Should().Be(EggId);
        RefusalOf(sent).Should().Be(refusal);
    }

    [Fact]
    public async Task OnlyAnEggHatches()
    {
        using var provider = Provider();
        var sessions = provider.GetRequiredService<SessionManager>();
        var (owner, sent) = Player(sessions, 700);
        var kate = Kate(sessions);
        owner.Inventory[InventoryConstants.SlotMax].ItemId = LeafId;
        owner.Inventory[InventoryConstants.SlotMax].Count = 1;

        await provider.GetRequiredService<IPetService>().HatchAsync(owner, kate.NpcId, LeafId, 0, PetName);

        owner.Inventory[InventoryConstants.SlotMax].ItemId.Should().Be(LeafId);
        RefusalOf(sent).Should().Be(HatchRefusal.CannotHatch);
    }

    [Fact]
    public async Task HatchingNeedsTheTrainerNearby()
    {
        using var provider = Provider();
        var sessions = provider.GetRequiredService<SessionManager>();
        var (owner, sent) = Player(sessions, 700);
        var kate = Kate(sessions);
        kate.X += 50;
        PutEgg(owner, 0);

        await provider.GetRequiredService<IPetService>().HatchAsync(owner, kate.NpcId, EggId, 0, PetName);

        owner.Inventory[InventoryConstants.SlotMax].ItemId.Should().Be(EggId);
        RefusalOf(sent).Should().Be(HatchRefusal.Failed);
    }

    [Fact]
    public async Task SummoningSpawnsTheFamiliarBesideItsOwnerAndSendsItsSheet()
    {
        using var provider = Provider();
        var sessions = provider.GetRequiredService<SessionManager>();
        var (owner, sent) = Player(sessions, 700);
        var pet = await EquipPet(provider, owner);

        (await provider.GetRequiredService<IPetService>().SummonAsync(owner)).Should().BeTrue();

        owner.Pet.Should().NotBeNull();
        var npc = owner.Pet!.Npc!;
        npc.IsPet.Should().BeTrue();
        npc.Name.Should().Be(PetName);
        npc.PetOwnerName.Should().Be(owner.Name);
        npc.ModelId.Should().Be(PetService.HatchedModelId);
        npc.OwnerCharId.Should().Be(owner.CharacterId);
        sessions.Regions.GetNpc(npc.UniqueId).Should().BeSameAs(npc);

        var sheet = sent.Single(p => p.GetOpcode() == (byte)GameOpcodes.GS_PET);
        sheet.ResetOffset();
        sheet.ReadByte().Should().Be((byte)PetSubOpcode.ModeFunction);
        sheet.ReadByte().Should().Be((byte)PetFunction.Mode);
        sheet.ReadByte().Should().Be((byte)PetMode.Summoned);
        sheet.ReadShort().Should().Be(PetPacketWriter.Succeeded);
        sheet.ReadInt().Should().Be(pet.Id);
        sheet.ReadString().Should().Be(PetName);
        sheet.ReadByte().Should().Be(KaulClass);
        sheet.ReadByte().Should().Be(Pet.StartLevel);
        sheet.ReadUShort().Should().Be(0);
        sheet.ReadShort().Should().Be(LevelOne.MaxHp);
        sheet.ReadShort().Should().Be(LevelOne.MaxHp);
        sheet.ReadShort().Should().Be(LevelOne.MaxMp);
        sheet.ReadShort().Should().Be(LevelOne.MaxMp);
        sheet.ReadShort().Should().Be(Pet.HatchedSatisfaction);
        sheet.ReadShort().Should().Be(LevelOne.Attack);
        sheet.ReadShort().Should().Be(LevelOne.Defence);
        for (var i = 0; i < PetPacketWriter.ResistanceCount; i++)
            sheet.ReadByte();
        sheet.RemainingBytes.Should().Be(Pet.InventorySize * MyInfoItemEntrySize, "four empty familiar bag records close the sheet");
    }

    [Fact]
    public async Task AFamiliarCannotBeCalledTwiceOrIntoABarredZone()
    {
        using var provider = Provider();
        var sessions = provider.GetRequiredService<SessionManager>();
        var (owner, _) = Player(sessions, 700);
        await EquipPet(provider, owner);
        var pets = provider.GetRequiredService<IPetService>();

        (await pets.SummonAsync(owner)).Should().BeTrue();
        (await pets.SummonAsync(owner)).Should().BeFalse();

        await pets.DismissAsync(owner);
        owner.ZoneId = (byte)ZoneId.Delos;
        (await pets.SummonAsync(owner)).Should().BeFalse();
        ZoneRules.AllowsPets((byte)ZoneId.Moradon).Should().BeTrue();
    }

    [Fact]
    public async Task TheFamiliarFollowsAndCatchesUpWhenLeftFarBehind()
    {
        using var provider = Provider();
        var sessions = provider.GetRequiredService<SessionManager>();
        var (owner, _) = Player(sessions, 700);
        await EquipPet(provider, owner);
        await provider.GetRequiredService<IPetService>().SummonAsync(owner);
        var npc = owner.Pet!.Npc!;
        var ai = provider.GetRequiredService<IPetAiService>();

        owner.X += 10;
        var before = Distance(npc, owner);
        await ai.TickAsync(npc, DateTime.UtcNow.Ticks);
        Distance(npc, owner).Should().BeLessThan(before);

        owner.X += 100;
        await ai.TickAsync(npc, DateTime.UtcNow.Ticks);
        Distance(npc, owner).Should().BeLessThan(PetAiService.FollowDistance * 2, "a familiar far behind is put back beside its owner");
    }

    [Fact]
    public async Task InAttackModeTheFamiliarJoinsItsOwnersFightAndEarnsItsShare()
    {
        using var provider = Provider();
        var sessions = provider.GetRequiredService<SessionManager>();
        var (owner, sent) = Player(sessions, 700);
        await EquipPet(provider, owner);
        var pets = provider.GetRequiredService<IPetService>();
        await pets.SummonAsync(owner);
        await pets.SetModeAsync(owner, PetMode.Attack);
        var npc = owner.Pet!.Npc!;
        npc.Attack1 = short.MaxValue;
        npc.HitRate = short.MaxValue;
        var monster = Monster(sessions, npc.X + 1, npc.Z, FrailMonsterHp);
        monster.RecordDamage(owner.CharacterId, 0, owner);
        sent.Clear();

        var ai = provider.GetRequiredService<IPetAiService>();
        var now = DateTime.UtcNow.Ticks;
        for (var i = 0; i < 10 && monster.IsAlive; i++)
            await ai.TickAsync(npc, now + i * TimeSpan.TicksPerSecond * 2);

        monster.IsAlive.Should().BeFalse();
        owner.Pet!.Record.Exp.Should().BeGreaterThan(0, "the familiar dealt the damage and earns its share of the kill");
        sent.Should().Contain(p => p.GetOpcode() == (byte)GameOpcodes.GS_PET && Function(p) == PetFunction.Exp);
    }

    [Fact]
    public async Task InDefenceModeTheFamiliarStaysOutOfTheFight()
    {
        using var provider = Provider();
        var sessions = provider.GetRequiredService<SessionManager>();
        var (owner, _) = Player(sessions, 700);
        await EquipPet(provider, owner);
        await provider.GetRequiredService<IPetService>().SummonAsync(owner);
        var npc = owner.Pet!.Npc!;
        var monster = Monster(sessions, npc.X + 1, npc.Z, SturdyMonsterHp);
        monster.RecordDamage(owner.CharacterId, 0, owner);

        var ai = provider.GetRequiredService<IPetAiService>();
        var now = DateTime.UtcNow.Ticks;
        for (var i = 0; i < 10; i++)
            await ai.TickAsync(npc, now + i * TimeSpan.TicksPerSecond * 2);

        monster.Hp.Should().Be(SturdyMonsterHp);
    }

    [Fact]
    public async Task FeedingRaisesSatisfactionByTheFoodsValueAndEatsOne()
    {
        using var provider = Provider();
        var sessions = provider.GetRequiredService<SessionManager>();
        var (owner, sent) = Player(sessions, 700);
        await EquipPet(provider, owner);
        var pets = provider.GetRequiredService<IPetService>();
        await pets.SummonAsync(owner);
        owner.Inventory[InventoryConstants.SlotMax + 3].ItemId = LeafId;
        owner.Inventory[InventoryConstants.SlotMax + 3].Count = 2;
        sent.Clear();

        await pets.FeedAsync(owner, 3, LeafId);

        owner.Pet!.Record.Satisfaction.Should().Be(Pet.HatchedSatisfaction + LeafSatisfaction);
        owner.Inventory[InventoryConstants.SlotMax + 3].Count.Should().Be(1);
        var fed = sent.First(p => Function(p) == PetFunction.Food);
        fed.ResetOffset();
        fed.ReadByte();
        fed.ReadByte();
        fed.ReadByte().Should().Be(PetPacketWriter.FoodSucceeded);
        fed.ReadByte().Should().Be(3);
        fed.ReadInt().Should().Be(LeafId);
        fed.ReadShort().Should().Be(1);
        fed.ReadShort();
        fed.ReadInt();
        fed.ReadShort().Should().Be(LeafSatisfaction, "the client shows the increase as a percentage of the full bar");
    }

    [Fact]
    public async Task SatisfactionDropsEachMinuteAndAStarvedFamiliarLeaves()
    {
        using var provider = Provider();
        var sessions = provider.GetRequiredService<SessionManager>();
        var (owner, _) = Player(sessions, 700);
        await EquipPet(provider, owner);
        var pets = provider.GetRequiredService<IPetService>();
        await pets.SummonAsync(owner);
        var state = owner.Pet!;
        var start = state.LastSatisfactionTicks;

        await pets.TickAsync(owner, start + PetService.SatisfactionDecayInterval.Ticks);
        state.Record.Satisfaction.Should().Be(Pet.HatchedSatisfaction - PetService.SatisfactionDecayPerMinute);

        state.Record.Satisfaction = PetService.SatisfactionDecayPerMinute;
        await pets.TickAsync(owner, start + PetService.SatisfactionDecayInterval.Ticks * 2);
        owner.Pet.Should().BeNull();
    }

    [Fact]
    public async Task DismissingRemovesTheFamiliarAndKeepsWhatItLearned()
    {
        using var provider = Provider();
        var sessions = provider.GetRequiredService<SessionManager>();
        var (owner, _) = Player(sessions, 700);
        var pet = await EquipPet(provider, owner);
        var pets = provider.GetRequiredService<IPetService>();
        await pets.SummonAsync(owner);
        var npc = owner.Pet!.Npc!;
        owner.Pet.Record.Exp = 30;

        await pets.DismissAsync(owner);

        owner.Pet.Should().BeNull();
        sessions.Regions.GetNpc(npc.UniqueId).Should().BeNull();
        (await Repository(provider).GetById(pet.Id))!.Exp.Should().Be(30);
    }

    [Fact]
    public async Task TheFamiliarsItemCannotLeaveItsSlotWhileItIsOut()
    {
        using var provider = Provider();
        var sessions = provider.GetRequiredService<SessionManager>();
        var (owner, _) = Player(sessions, 700);
        await EquipPet(provider, owner);
        var pets = provider.GetRequiredService<IPetService>();
        var items = provider.GetRequiredService<IItemPacketCoordinator>();
        await pets.SummonAsync(owner);

        await items.HandleMoveAsync(owner.Client, Unequip());
        owner.Inventory[InventoryConstants.Pet].ItemId.Should().Be(KaulId);

        await pets.DismissAsync(owner);
        await items.HandleMoveAsync(owner.Client, Unequip());
        owner.Inventory[InventoryConstants.Pet].IsEmpty.Should().BeTrue();
        owner.Inventory[InventoryConstants.SlotMax + 5].IsLinked.Should().BeTrue("the familiar's link travels with its item");
    }

    [Fact]
    public void ALinkedItemSurvivesTheSaveAndCannotBeTraded()
    {
        var slots = PetInventory.Load(null);
        slots[2].ItemId = KaulId;
        slots[2].Count = 1;
        slots[2].UniqueId = 77;

        var reloaded = PetInventory.Load(UserSessionBinaryState.SerializeSlots(slots));

        reloaded[2].UniqueId.Should().Be(77);
        reloaded[2].IsTradable.Should().BeFalse();
        reloaded[1].IsLinked.Should().BeFalse();
    }

    [Fact]
    public async Task TheFamiliarSummonScrollCallsThePetAndCancellingItSendsThePetHome()
    {
        using var provider = Provider();
        var sessions = provider.GetRequiredService<SessionManager>();
        var (owner, sent) = Player(sessions, 700);
        await EquipPet(provider, owner);
        var scroll = owner.Inventory[InventoryConstants.SlotMax + 4];
        scroll.ItemId = FamiliarSummonScroll;
        scroll.Count = 5;
        var magic = provider.GetRequiredService<IMagicPacketCoordinator>();

        await magic.HandleAsync(owner.Client, Magic(MagicProcessOpcode.Casting, PetService.FamiliarSummonSkill, owner));
        await magic.HandleAsync(owner.Client, Magic(MagicProcessOpcode.Effecting, PetService.FamiliarSummonSkill, owner));

        owner.Pet.Should().NotBeNull("the scroll's skill is the familiar summon");
        owner.Pet!.IsSummoned.Should().BeTrue();
        scroll.Count.Should().Be(4, "each summon uses one scroll");
        var npc = owner.Pet.Npc!;

        await magic.HandleAsync(owner.Client, Magic(MagicProcessOpcode.Cancel, PetService.FamiliarSummonSkill, owner));

        owner.Pet.Should().BeNull();
        sessions.Regions.GetNpc(npc.UniqueId).Should().BeNull();
    }

    private static Packet Magic(MagicProcessOpcode sub, int skillId, UserSession caster)
    {
        var packet = new Packet(GameOpcodes.GS_MAGIC_PROCESS);
        packet.WriteByte((byte)sub);
        packet.WriteInt(skillId);
        packet.WriteInt(caster.CharacterId);
        packet.WriteInt(caster.CharacterId);
        for (var i = 0; i < 7; i++)
            packet.WriteInt(0);
        packet.ResetOffset();
        return packet;
    }

    private static ServiceProvider Provider() => CreateProvider(_ => { }, gameData =>
    {
        gameData.GetMagic(PetService.FamiliarSummonSkill).Returns(new MagicData
        {
            Id = PetService.FamiliarSummonSkill, Type1 = (byte)MagicSkillType.Stealth, Moral = 1, SkillLevel = 1,
            UseItem = FamiliarSummonScroll, ItemGroup = 9, CastTime = 100, ReCastTime = 30, SuccessRate = 70,
            SelfEffect = 255,
        });
        gameData.MagicType9Table.Returns(new Dictionary<int, MagicType9Data>
        {
            [PetService.FamiliarSummonSkill] = new()
            {
                Id = PetService.FamiliarSummonSkill, TargetChange = 3, StateChange = (byte)MagicStealthType.PetSummon,
                HitRate = 100, Duration = -1, Vision = 100,
            },
        });
        gameData.GetItem(FamiliarSummonScroll).Returns(new ItemData
        {
            Num = FamiliarSummonScroll, Kind = 97, Slot = 15, Countable = 1, Effect1 = PetService.FamiliarSummonSkill,
        });
        gameData.PetLevelTable.Returns(new Dictionary<byte, PetLevelData> { [1] = LevelOne, [2] = LevelTwo });
        gameData.GetItem(EggId).Returns(new ItemData { Num = EggId, Kind = (byte)ItemKind.PetEgg, Slot = 15, Countable = 1 });
        gameData.GetItem(KaulId).Returns(new ItemData
        {
            Num = KaulId, Kind = (byte)ItemKind.PetItem, Slot = KaulItemSlot, Damage = KaulClass, Duration = 1,
        });
        gameData.GetItem(LeafId).Returns(new ItemData
        {
            Num = LeafId, Kind = (byte)ItemKind.PetFood, Slot = 15, Damage = LeafSatisfaction, Countable = 1,
        });
    });

    private static IPetRepository Repository(ServiceProvider provider) =>
        provider.CreateScope().ServiceProvider.GetRequiredService<IPetRepository>();

    private static async Task<Pet> EquipPet(ServiceProvider provider, UserSession owner)
    {
        var pet = await Repository(provider).CreateAsync(new Pet
        {
            Name = PetName, Level = Pet.StartLevel, Hp = LevelOne.MaxHp, Mp = LevelOne.MaxMp,
            ModelId = PetService.HatchedModelId, Size = PetService.HatchedSize, Class = KaulClass,
        });
        var slot = owner.Inventory[InventoryConstants.Pet];
        slot.ItemId = KaulId;
        slot.Count = 1;
        slot.Durability = 1;
        slot.UniqueId = pet.Id;
        return pet;
    }

    private static void PutEgg(UserSession owner, int bagSlot)
    {
        var slot = owner.Inventory[InventoryConstants.SlotMax + bagSlot];
        slot.ItemId = EggId;
        slot.Count = 1;
    }

    private static NpcInstance Kate(SessionManager sessions) =>
        sessions.Regions.SpawnNpc(new NpcInstance
        {
            NpcId = KateNpcId, NpcType = NpcData.TypeTalk, ZoneId = Zone, X = 102, Z = 101, Hp = 1, MaxHp = 1,
        });

    private static NpcInstance Monster(SessionManager sessions, float x, float z, int hp) =>
        sessions.Regions.SpawnNpc(new NpcInstance
        {
            NpcId = 101, IsMonster = true, ZoneId = Zone, X = x, Z = z, Hp = hp, MaxHp = hp,
            Experience = MonsterExperience,
        });

    private static Packet Unequip()
    {
        var packet = new Packet(GameOpcodes.GS_ITEM_MOVE);
        packet.WriteByte(1);
        packet.WriteByte((byte)ItemMoveDirection.SlotToInventory);
        packet.WriteInt(KaulId);
        packet.WriteByte((byte)InventoryConstants.Pet);
        packet.WriteByte(5);
        packet.ResetOffset();
        return packet;
    }

    private static HatchRefusal RefusalOf(List<Packet> sent)
    {
        var reply = sent.Single(p => p.GetOpcode() == (byte)GameOpcodes.GS_ITEM_UPGRADE);
        reply.ResetOffset();
        reply.ReadByte();
        reply.ReadByte().Should().Be((byte)HatchResult.Refused);
        return (HatchRefusal)reply.ReadByte();
    }

    private static PetFunction? Function(Packet packet)
    {
        if (packet.GetOpcode() != (byte)GameOpcodes.GS_PET)
            return null;
        var data = packet.GetData();
        return data.Length >= 2 ? (PetFunction)data[1] : null;
    }

    private static float Distance(NpcInstance npc, UserSession owner) =>
        MathF.Sqrt((npc.X - owner.X) * (npc.X - owner.X) + (npc.Z - owner.Z) * (npc.Z - owner.Z));

    private static (UserSession Session, List<Packet> Sent) Player(SessionManager sessions, int characterId)
    {
        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        var sent = new List<Packet>();
        client.SendPacket(Arg.Do<Packet>(sent.Add), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);

        var player = sessions.CreateSession(client, characterId, characterId + 100);
        player.Name = $"P{characterId}";
        player.Class = 203;
        player.Level = 60;
        player.Nation = AccountNation.Karus;
        player.ZoneId = Zone;
        player.X = 101;
        player.Z = 101;
        player.Hp = 1000;
        player.MaxHp = 1000;
        sessions.Regions.AddToRegion(player);
        return (player, sent);
    }
}
