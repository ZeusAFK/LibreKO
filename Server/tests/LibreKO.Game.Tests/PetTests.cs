using FluentAssertions;
using LibreKO.Common.Domain.Entities;
using LibreKO.Common.Domain.Entities.GameData;
using LibreKO.Common.Domain.Services;
using LibreKO.Common.Enums;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Common.Infrastructure.Persistence.Seed.Entities;
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
    private const int Slap = 301_001;
    private const byte SlapLevel = 10;
    private const short SlapMana = 4;
    private const int OtherClassPage = 1020;
    private const int KaulClassPage = KaulClass * PetSkills.ClassPageDivisor;
    private const int AutomaticLooting = 700_012_000;
    private const int SecondLooting = 700_012_001;
    private const int Sword = 110_110_001;
    private const int EtarothScroll = 700_019_001;
    private const int EtarothScrollBase = 700_019_000;
    private const int EtarothId = 610_015_000;
    private const byte EtarothClass = 115;
    private const short EtarothModel = 3900;
    private const short EtarothSize = 40;
    private const short CertainWeight = 10_000;
    private const int LunarScroll = 508_090_000;
    private const int TransformMaterialSlots = 3;
    private const int ImageChangeLow = 700_013_000;
    private const int ImageChangeMiddle = 700_017_000;
    private const int ImageChangeHigh = 700_018_000;

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

    [Theory]
    [InlineData(EtarothScroll, EtarothScroll)]
    [InlineData(EtarothScrollBase, EtarothScroll)]
    public async Task ATransformationScrollTurnsTheFamiliarIntoItsResult(int carried, int named)
    {
        using var provider = Provider();
        var sessions = provider.GetRequiredService<SessionManager>();
        var (owner, sent) = Player(sessions, 700);
        var kate = Kate(sessions);
        var pet = await LinkPet(provider, owner.Inventory[InventoryConstants.SlotMax]);
        PutItem(owner, 1, carried);

        await provider.GetRequiredService<IItemUpgradeService>()
            .HandleUpgradeAsync(owner.Client, TransformRequest(kate.NpcId, 0, named, 1));

        var familiar = owner.Inventory[InventoryConstants.SlotMax];
        familiar.ItemId.Should().Be(EtarothId);
        familiar.UniqueId.Should().Be(pet.Id);
        owner.Inventory[InventoryConstants.SlotMax + 1].IsEmpty.Should().BeTrue();
        var saved = await Repository(provider).GetById(pet.Id);
        saved!.ModelId.Should().Be(EtarothModel);
        saved.Size.Should().Be(EtarothSize);
        saved.Class.Should().Be(EtarothClass);

        var reply = sent.Single(p => p.GetOpcode() == (byte)GameOpcodes.GS_ITEM_UPGRADE);
        reply.ResetOffset();
        reply.ReadByte().Should().Be((byte)ItemUpgradeSubOpcode.PetTransform);
        reply.ReadByte().Should().Be((byte)HatchResult.Succeeded);
        reply.ReadInt().Should().Be(EtarothId);
        reply.ReadByte().Should().Be(0);
        reply.ReadInt().Should().Be(pet.Id);
        reply.ReadString().Should().Be(PetName);
        reply.ReadByte().Should().Be(EtarothClass);
        reply.ReadByte().Should().Be(Pet.StartLevel);
        reply.ReadUShort().Should().Be(0);
        reply.ReadShort().Should().Be(Pet.HatchedSatisfaction);
        reply.ReadByte().Should().Be(PetPacketWriter.TransformedPad, "the 2619 reader skips a byte after the familiar");
        reply.ReadInt().Should().Be(carried);
        reply.ReadByte().Should().Be(1);
        reply.RemainingBytes.Should().Be(0);
    }

    [Fact]
    public async Task AScrollWithoutATransformationIsRefused()
    {
        using var provider = Provider();
        var sessions = provider.GetRequiredService<SessionManager>();
        var (owner, sent) = Player(sessions, 700);
        var kate = Kate(sessions);
        var pet = await LinkPet(provider, owner.Inventory[InventoryConstants.SlotMax]);
        PutItem(owner, 1, LunarScroll);

        await provider.GetRequiredService<IItemUpgradeService>()
            .HandleUpgradeAsync(owner.Client, TransformRequest(kate.NpcId, 0, LunarScroll, 1));

        owner.Inventory[InventoryConstants.SlotMax].ItemId.Should().Be(KaulId);
        owner.Inventory[InventoryConstants.SlotMax + 1].ItemId.Should().Be(LunarScroll);
        (await Repository(provider).GetById(pet.Id))!.ModelId.Should().Be(PetService.HatchedModelId);
        RefusalOf(sent).Should().Be(HatchRefusal.Failed);
    }

    [Fact]
    public async Task AScrollNeverRollsTheFormTheFamiliarAlreadyHas()
    {
        using var provider = Provider();
        var sessions = provider.GetRequiredService<SessionManager>();
        var (owner, sent) = Player(sessions, 700);
        var kate = Kate(sessions);
        var familiar = owner.Inventory[InventoryConstants.SlotMax];
        await LinkPet(provider, familiar);
        familiar.ItemId = EtarothId;
        PutItem(owner, 1, EtarothScroll);

        await provider.GetRequiredService<IItemUpgradeService>()
            .HandleUpgradeAsync(owner.Client, TransformRequest(kate.NpcId, 0, EtarothScroll, 1, EtarothId));

        owner.Inventory[InventoryConstants.SlotMax + 1].ItemId.Should().Be(EtarothScroll, "a scroll that would change nothing is kept");
        RefusalOf(sent).Should().Be(HatchRefusal.Failed);
    }

    [Fact]
    public void ImageChangeScrollsOfferTieredForms()
    {
        var transforms = new PetTransformSeed().GetSeedData().ToList();
        var tiers = new[] { ImageChangeLow, ImageChangeMiddle, ImageChangeHigh }
            .Select(scroll => transforms.Where(t => t.Material == scroll).ToList())
            .ToList();

        tiers.Should().AllSatisfy(tier => tier.Sum(t => t.Weight).Should().Be(CertainWeight));
        tiers.SelectMany(tier => tier.Select(t => t.Result)).Should().OnlyHaveUniqueItems("each form belongs to one tier");
        tiers.Select(tier => tier.Count).Should().Equal(4, 4, 3);
    }

    [Fact]
    public void ATransformationIsPickedByWeight()
    {
        var never = new PetTransformData { Id = 1, Weight = 0 };
        var always = new PetTransformData { Id = 2, Weight = CertainWeight };
        var random = new Random(7);

        Enumerable.Range(0, 100).Select(_ => PetTransforms.Pick([never, always], random))
            .Should().OnlyContain(pick => pick == always);
        PetTransforms.Pick([], random).Should().BeNull();
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
        Distance(npc, owner).Should().BeGreaterThan(0f, "the familiar does not stand inside its owner")
            .And.BeLessThan(PetAiService.FollowDistance * 2);

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

    [Theory]
    [InlineData(Slap, PetSkills.SharedPageFirst, SlapLevel, SlapLevel, true)]
    [InlineData(Slap, PetSkills.SharedPageFirst, SlapLevel, SlapLevel - 1, false)]
    [InlineData(301_050, KaulClassPage, 1, 1, true)]
    [InlineData(301_050, OtherClassPage, 1, 1, false)]
    [InlineData(300_101, PetSkills.SharedPageFirst, 1, 1, false)]
    public void AFamiliarKnowsTheSharedPageAndItsClassPageUpToItsLevel(int skillId, int page, int required, int level, bool knows)
    {
        PetSkills.Knows(skillId, page, required, KaulClass, level).Should().Be(knows);
    }

    [Fact]
    public async Task AFamiliarSkillStrikesTheTargetSpendsManaAndSatisfactionAndWaitsForItsCooldown()
    {
        using var provider = Provider();
        var sessions = provider.GetRequiredService<SessionManager>();
        var (owner, sent) = Player(sessions, 700);
        await EquipPet(provider, owner);
        await provider.GetRequiredService<IPetService>().SummonAsync(owner);
        var state = owner.Pet!;
        state.Record.Level = SlapLevel;
        var npc = state.Npc!;
        npc.Attack1 = short.MaxValue;
        var monster = Monster(sessions, npc.X + 1, npc.Z, SturdyMonsterHp);
        var satisfaction = state.Record.Satisfaction;
        var mana = npc.Mp;
        sent.Clear();

        var skills = provider.GetRequiredService<IPetSkillService>();
        await skills.UseAsync(owner, Request(MagicProcessOpcode.Effecting, Slap, npc.UniqueId, monster.UniqueId));

        monster.Hp.Should().BeLessThan(SturdyMonsterHp);
        npc.Mp.Should().Be(mana - SlapMana);
        state.Record.Satisfaction.Should().Be((short)(satisfaction - PetSkills.SatisfactionPerSkill));
        state.Mode.Should().Be(PetMode.Attack, "using a skill on a monster sends the familiar into the fight");
        sent.Should().Contain(p => Function(p) == PetFunction.Mp);
        sent.Should().Contain(p => MagicStage(p) == MagicProcessOpcode.Effecting);

        sent.Clear();
        var hp = monster.Hp;
        await skills.UseAsync(owner, Request(MagicProcessOpcode.Effecting, Slap, npc.UniqueId, monster.UniqueId));
        monster.Hp.Should().Be(hp, "the skill is still cooling down");
        sent.Should().ContainSingle(p => MagicStage(p) == MagicProcessOpcode.Fail);
    }

    [Fact]
    public async Task AFamiliarRefusesASkillAboveItsLevelOrFromSomeoneElsesFamiliar()
    {
        using var provider = Provider();
        var sessions = provider.GetRequiredService<SessionManager>();
        var (owner, _) = Player(sessions, 700);
        await EquipPet(provider, owner);
        await provider.GetRequiredService<IPetService>().SummonAsync(owner);
        var npc = owner.Pet!.Npc!;
        var monster = Monster(sessions, npc.X + 1, npc.Z, SturdyMonsterHp);
        var skills = provider.GetRequiredService<IPetSkillService>();

        await skills.UseAsync(owner, Request(MagicProcessOpcode.Effecting, Slap, npc.UniqueId, monster.UniqueId));
        await skills.UseAsync(owner, Request(MagicProcessOpcode.Effecting, PetSkills.DesignatedAttack, npc.UniqueId + 1, monster.UniqueId));

        monster.Hp.Should().Be(SturdyMonsterHp);
        owner.Pet!.Mode.Should().Be(PetMode.Defence);
    }

    [Fact]
    public async Task TheAttackOrderSendsTheFamiliarAtTheOwnersTarget()
    {
        using var provider = Provider();
        var sessions = provider.GetRequiredService<SessionManager>();
        var (owner, _) = Player(sessions, 700);
        await EquipPet(provider, owner);
        await provider.GetRequiredService<IPetService>().SummonAsync(owner);
        var npc = owner.Pet!.Npc!;
        var monster = Monster(sessions, npc.X + 10, npc.Z, SturdyMonsterHp);

        await provider.GetRequiredService<IPetSkillService>().UseAsync(owner,
            Request(MagicProcessOpcode.Effecting, PetSkills.DesignatedAttack, npc.UniqueId, monster.UniqueId));

        owner.Pet!.Mode.Should().Be(PetMode.Attack);
        owner.Pet.TargetNpcId.Should().Be(monster.UniqueId);
    }

    [Fact]
    public async Task AFamiliarRegainsATenthOfItsManaEveryTenSeconds()
    {
        using var provider = Provider();
        var sessions = provider.GetRequiredService<SessionManager>();
        var (owner, sent) = Player(sessions, 700);
        await EquipPet(provider, owner);
        var pets = provider.GetRequiredService<IPetService>();
        await pets.SummonAsync(owner);
        var npc = owner.Pet!.Npc!;
        npc.Mp = 0;
        sent.Clear();

        await pets.TickAsync(owner, owner.Pet.LastRegenTicks + PetService.RegenInterval.Ticks);

        npc.Mp.Should().Be(LevelOne.MaxMp * PetService.RegenPercent / 100);
        sent.Should().Contain(p => Function(p) == PetFunction.Mp);
    }

    [Fact]
    public async Task TheFamiliarsBagTakesOneOfEachFamiliarItemAndGivesItBack()
    {
        using var provider = Provider();
        var sessions = provider.GetRequiredService<SessionManager>();
        var (owner, _) = Player(sessions, 700);
        await EquipPet(provider, owner);
        var pets = provider.GetRequiredService<IPetService>();
        await pets.SummonAsync(owner);
        Carry(owner, 0, AutomaticLooting);
        Carry(owner, 1, SecondLooting);
        Carry(owner, 2, Sword);

        (await pets.MoveItemAsync(owner, intoPet: true, AutomaticLooting, 0, 0)).Should().BeTrue();
        (await pets.MoveItemAsync(owner, intoPet: true, SecondLooting, 1, 1)).Should().BeFalse("one of each kind");
        (await pets.MoveItemAsync(owner, intoPet: true, Sword, 2, 2)).Should().BeFalse("only familiar items fit");
        owner.Pet!.Items[0].ItemId.Should().Be(AutomaticLooting);
        owner.Inventory[InventoryConstants.SlotMax].IsEmpty.Should().BeTrue();
        PetBag.Loots(owner.Pet.Items, id => id == AutomaticLooting
            ? new ItemData { Kind = PetBag.AutomaticLootingKind, Slot = PetBag.ItemSlotCode } : null).Should().BeTrue();

        (await pets.MoveItemAsync(owner, intoPet: false, AutomaticLooting, 0, 5)).Should().BeTrue();
        owner.Inventory[InventoryConstants.SlotMax + 5].ItemId.Should().Be(AutomaticLooting);
        owner.Pet.Items[0].IsEmpty.Should().BeTrue();
    }

    [Fact]
    public async Task TheFamiliarsBagIsClosedWhileTheFamiliarIsAway()
    {
        using var provider = Provider();
        var sessions = provider.GetRequiredService<SessionManager>();
        var (owner, _) = Player(sessions, 700);
        await EquipPet(provider, owner);
        Carry(owner, 0, AutomaticLooting);

        (await provider.GetRequiredService<IPetService>().MoveItemAsync(owner, intoPet: true, AutomaticLooting, 0, 0))
            .Should().BeFalse();
    }

    private static void Carry(UserSession owner, int bagPosition, int itemId)
    {
        var slot = owner.Inventory[InventoryConstants.SlotMax + bagPosition];
        slot.ItemId = itemId;
        slot.Count = 1;
        slot.Durability = 1;
    }

    private static PetSkillRequest Request(MagicProcessOpcode stage, int skillId, int casterId, int targetId) =>
        new((byte)stage, skillId, casterId, targetId, new int[5]);

    private static MagicProcessOpcode? MagicStage(Packet packet)
    {
        if (packet.GetOpcode() != (byte)GameOpcodes.GS_MAGIC_PROCESS)
            return null;
        var data = packet.GetData();
        return data.Length >= 1 ? (MagicProcessOpcode)data[0] : null;
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
        gameData.GetMagic(Slap).Returns(new MagicData
        {
            Id = Slap, Type1 = (byte)MagicSkillType.Melee, Moral = 7, SkillLevel = SlapLevel, Skill = PetSkills.SharedPageFirst,
            Msp = SlapMana, ReCastTime = 40, Range = 1,
        });
        gameData.GetMagic(PetSkills.DesignatedAttack).Returns(new MagicData
        {
            Id = PetSkills.DesignatedAttack, Type1 = (byte)MagicSkillType.Area, Moral = 7, SkillLevel = 1,
            Skill = PetSkills.SharedPageFirst, ReCastTime = 10, Range = 40,
        });
        gameData.MagicType1Table.Returns(new Dictionary<int, MagicType1Data>
        {
            [Slap] = new() { Id = Slap, HitRate = 100, Hit = 120, HitType = 1 },
        });
        gameData.PetLevelTable.Returns(new Dictionary<byte, PetLevelData> { [1] = LevelOne, [2] = LevelTwo });
        gameData.GetItem(AutomaticLooting).Returns(new ItemData { Num = AutomaticLooting, Kind = PetBag.AutomaticLootingKind, Slot = PetBag.ItemSlotCode });
        gameData.GetItem(SecondLooting).Returns(new ItemData { Num = SecondLooting, Kind = PetBag.AutomaticLootingKind, Slot = PetBag.ItemSlotCode });
        gameData.GetItem(Sword).Returns(new ItemData { Num = Sword, Kind = 21, Slot = 1 });
        gameData.GetItem(EggId).Returns(new ItemData { Num = EggId, Kind = (byte)ItemKind.PetEgg, Slot = 15, Countable = 1 });
        gameData.GetItem(KaulId).Returns(new ItemData
        {
            Num = KaulId, Kind = (byte)ItemKind.PetItem, Slot = KaulItemSlot, Damage = KaulClass, Duration = 1,
        });
        gameData.GetItem(LeafId).Returns(new ItemData
        {
            Num = LeafId, Kind = (byte)ItemKind.PetFood, Slot = 15, Damage = LeafSatisfaction, Countable = 1,
        });
        gameData.GetItem(EtarothId).Returns(new ItemData
        {
            Num = EtarothId, Kind = (byte)ItemKind.PetItem, Slot = KaulItemSlot, Damage = EtarothClass, Duration = 1,
        });
        gameData.PetTransformsByMaterial.Returns(new[]
        {
            new PetTransformData
            {
                Id = 37, Material = EtarothScrollBase, Result = EtarothId, ModelId = EtarothModel, Size = EtarothSize,
                Weight = CertainWeight,
            },
        }.ToLookup(transform => transform.Material));
    });

    private static IPetRepository Repository(ServiceProvider provider) =>
        provider.CreateScope().ServiceProvider.GetRequiredService<IPetRepository>();

    private static Task<Pet> EquipPet(ServiceProvider provider, UserSession owner) =>
        LinkPet(provider, owner.Inventory[InventoryConstants.Pet]);

    private static async Task<Pet> LinkPet(ServiceProvider provider, ItemSlot slot)
    {
        var pet = await Repository(provider).CreateAsync(new Pet
        {
            Name = PetName, Level = Pet.StartLevel, Hp = LevelOne.MaxHp, Mp = LevelOne.MaxMp,
            ModelId = PetService.HatchedModelId, Size = PetService.HatchedSize, Class = KaulClass,
        });
        slot.ItemId = KaulId;
        slot.Count = 1;
        slot.Durability = 1;
        slot.UniqueId = pet.Id;
        return pet;
    }

    private static void PutEgg(UserSession owner, int bagSlot) => PutItem(owner, bagSlot, EggId);

    private static void PutItem(UserSession owner, int bagSlot, int itemId)
    {
        var slot = owner.Inventory[InventoryConstants.SlotMax + bagSlot];
        slot.ItemId = itemId;
        slot.Count = 1;
    }

    private static Packet TransformRequest(int npcId, byte petSlot, int materialItemId, byte materialSlot,
        int petItemId = KaulId)
    {
        var packet = new Packet(GameOpcodes.GS_ITEM_UPGRADE);
        packet.WriteByte((byte)ItemUpgradeSubOpcode.PetTransform);
        packet.WriteInt(npcId);
        packet.WriteInt(petItemId);
        packet.WriteByte(petSlot);
        packet.WriteInt(materialItemId);
        packet.WriteByte(materialSlot);
        for (var i = 1; i < TransformMaterialSlots; i++)
        {
            packet.WriteInt(0);
            packet.WriteByte(0);
        }
        packet.ResetOffset();
        return packet;
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
