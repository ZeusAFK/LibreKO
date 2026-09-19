using System.Runtime.CompilerServices;
using System.Text.Json;
using FluentAssertions;
using LibreKO.Common.Domain.Entities.GameData;
using LibreKO.Common.Domain.Services;
using LibreKO.Common.Enums;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.Protocol;
using LibreKO.Game.Protocol.Writers;
using LibreKO.Game.World;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;
using Xunit.Abstractions;

namespace LibreKO.Game.Tests;

public class SkillReagentAndBuffTests(ITestOutputHelper output) : GameTestBase
{
    private const int FireBlast = 109535;
    private const int FireThorn = 109554;
    private const int FireImpact = 109557;
    private const int IceOrb = 109627;
    private const int ResistCold = 109606;
    private const int Viper = 107550;

    private static readonly string SeedDirectory = FindSeedDirectory();

    private static string FindSeedDirectory([CallerFilePath] string here = "")
    {
        var dir = new DirectoryInfo(Path.GetDirectoryName(here)!);
        while (dir != null)
        {
            var candidate = Path.Combine(dir.FullName, "LibreKO.Game", "Seed", "Data");
            if (Directory.Exists(candidate))
                return candidate;
            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException("LibreKO.Game/Seed/Data not found above " + here);
    }

    private static readonly Lazy<Dictionary<int, ItemData>> Items = new(() => LoadRows("Items.json", (ItemData i) => i.Num));

    private static Dictionary<int, T> Load<T>(string file, Func<T, int> key) =>
        typeof(T) == typeof(ItemData) && file == "Items.json"
            ? (Dictionary<int, T>)(object)Items.Value : LoadRows(file, key);

    private static Dictionary<int, T> LoadRows<T>(string file, Func<T, int> key)
    {
        var path = Path.GetFullPath(Path.Combine(SeedDirectory, file));
        var sources = File.Exists(path) || file != "Items.json" ? new[] { path }
            : Directory.GetFiles(SeedDirectory, "Items.slot*.json").Order(StringComparer.OrdinalIgnoreCase).ToArray();
        var result = new Dictionary<int, T>();
        foreach (var source in sources)
        {
            using var stream = File.OpenRead(source);
            foreach (var row in JsonSerializer.Deserialize<List<T>>(stream)!)
                result[key(row)] = row;
        }
        return result;
    }

    [Theory]
    [InlineData(FireBlast, "Fire Blast")]
    [InlineData(FireThorn, "Fire Thorn")]
    [InlineData(FireImpact, "Fire Impact")]
    [InlineData(IceOrb, "Ice Orb")]
    [InlineData(Viper, "Viper")]
    public async Task ReportedAttackSkillsDamageAMonsterTwiceInARow(int skillId, string name)
    {
        var magic = Load<MagicData>("Magic.json", m => m.Id);
        var items = Load<ItemData>("Items.json", i => i.Num);
        var row = magic[skillId];

        using var provider = CreateProvider(_ => { }, gameData =>
        {
            gameData.GetMagic(Arg.Any<int>()).Returns(c => magic.GetValueOrDefault(c.Arg<int>()));
            gameData.GetItem(Arg.Any<int>()).Returns(c => items.GetValueOrDefault(c.Arg<int>()));
            gameData.MagicType2Table.Returns(Load<MagicType2Data>("MagicType2.json", m => m.Id));
            gameData.MagicType3Table.Returns(Load<MagicType3Data>("MagicType3.json", m => m.Id));
            gameData.MagicType4Table.Returns(Load<MagicType4Data>("MagicType4.json", m => m.Id));
        });

        var sessionManager = provider.GetRequiredService<SessionManager>();
        var caster = CreateCaster(provider, sessionManager, row, items);
        var monster = sessionManager.Regions.SpawnNpc(new NpcInstance
        {
            IsMonster = true,
            NpcId = 2000, NpcType = 0, ZoneId = 1, X = 50, Z = 50,
            MaxHp = 100000, Hp = 100000, Ac = 0, EvadeRate = 1
        });

        var execution = provider.GetRequiredService<IMagicExecutionService>();

        const int attemptsPerRound = 8;
        var damage = new int[2];
        for (var cast = 0; cast < 2; cast++)
        {
            for (var attempt = 0; attempt < attemptsPerRound && damage[cast] == 0; attempt++)
            {
                var before = monster.Hp;
                caster.Mp = caster.MaxMp;
                await execution.ExecuteAsync(caster, row, skillId, monster.UniqueId, new int[7]);
                damage[cast] = before - monster.Hp;
            }

            output.WriteLine($"{name} cast {cast + 1}: damage {damage[cast]}, reagent left "
                + $"{CountReagent(caster, row.UseItem)}");
        }

        damage[0].Should().BeGreaterThan(0, "{0} must damage a monster on its first cast", name);
        damage[1].Should().BeGreaterThan(0, "{0} must still damage a monster on its second cast", name);
    }

    [Fact]
    public async Task ResistColdRaisesColdResistance()
    {
        var magic = Load<MagicData>("Magic.json", m => m.Id);
        var items = Load<ItemData>("Items.json", i => i.Num);
        var row = magic[ResistCold];

        using var provider = CreateProvider(_ => { }, gameData =>
        {
            gameData.GetMagic(Arg.Any<int>()).Returns(c => magic.GetValueOrDefault(c.Arg<int>()));
            gameData.GetItem(Arg.Any<int>()).Returns(c => items.GetValueOrDefault(c.Arg<int>()));
            gameData.MagicType4Table.Returns(Load<MagicType4Data>("MagicType4.json", m => m.Id));
            gameData.GetCoefficient(Arg.Any<short>()).Returns(new CoefficientData { ClassId = 110, Ac = 1 });
        });

        var sessionManager = provider.GetRequiredService<SessionManager>();
        var caster = CreateCaster(provider, sessionManager, row, items);
        var before = caster.Stats.ColdR;

        var execution = provider.GetRequiredService<IMagicExecutionService>();
        await execution.ExecuteAsync(caster, row, ResistCold, caster.CharacterId, new int[7]);

        output.WriteLine($"ColdR {before} -> {caster.Stats.ColdR}, buffs {caster.ActiveBuffs.Count}");
        caster.Stats.ColdR.Should().BeGreaterThan(before, "Resist Cold adds cold resistance");
    }

    [Fact]
    public async Task IceOrbSlowsTheMonsterAndNotTheCaster()
    {
        var magic = Load<MagicData>("Magic.json", m => m.Id);
        var items = Load<ItemData>("Items.json", i => i.Num);
        var row = magic[IceOrb];

        using var provider = CreateProvider(_ => { }, gameData =>
        {
            gameData.GetMagic(Arg.Any<int>()).Returns(c => magic.GetValueOrDefault(c.Arg<int>()));
            gameData.GetItem(Arg.Any<int>()).Returns(c => items.GetValueOrDefault(c.Arg<int>()));
            gameData.MagicType3Table.Returns(Load<MagicType3Data>("MagicType3.json", m => m.Id));
            gameData.MagicType4Table.Returns(Load<MagicType4Data>("MagicType4.json", m => m.Id));
            gameData.GetCoefficient(Arg.Any<short>()).Returns(new CoefficientData { ClassId = 110, Ac = 1 });
        });

        var sessionManager = provider.GetRequiredService<SessionManager>();
        var caster = CreateCaster(provider, sessionManager, row, items);
        var monster = sessionManager.Regions.SpawnNpc(new NpcInstance
        {
            IsMonster = true,
            NpcId = 2000, NpcType = 0, ZoneId = 1, X = 50, Z = 50,
            MaxHp = 100000, Hp = 100000, Ac = 0, EvadeRate = 1
        });

        var execution = provider.GetRequiredService<IMagicExecutionService>();
        await execution.ExecuteAsync(caster, row, IceOrb, monster.UniqueId, new int[7]);

        output.WriteLine($"caster buffs after Ice Orb: {caster.ActiveBuffs.Count}, "
            + $"speed {caster.SpeedAmount}");

        caster.ActiveBuffs.Should().BeEmpty("a slow cast at a monster must not land on the caster");
        caster.SpeedAmount.Should().Be(100, "the caster's own speed must be untouched");
    }

    [Fact]
    public async Task ADisguiseScrollOpensTheTransformationListInsteadOfCasting()
    {
        const int DisguiseScroll = 472001;

        var magic = Load<MagicData>("Magic.json", m => m.Id);
        var items = Load<ItemData>("Items.json", i => i.Num);
        var row = magic[DisguiseScroll];

        row.Type1.Should().Be(0, "a list-opening scroll has no primary type");
        row.Type2.Should().NotBe(0);
        row.UseItem.Should().NotBe(0);

        using var provider = CreateProvider(_ => { }, gameData =>
        {
            gameData.GetMagic(Arg.Any<int>()).Returns(c => magic.GetValueOrDefault(c.Arg<int>()));
            gameData.GetItem(Arg.Any<int>()).Returns(c => items.GetValueOrDefault(c.Arg<int>()));
            gameData.MagicType1Table.Returns(Load<MagicType1Data>("MagicType1.json", m => m.Id));
            gameData.GetCoefficient(Arg.Any<short>()).Returns(new CoefficientData { ClassId = 110, Ac = 1 });
        });

        var sessionManager = provider.GetRequiredService<SessionManager>();
        var caster = CreateCaster(provider, sessionManager, row, items);
        for (var i = 0; i < caster.SkillPoints.Length; i++)
            caster.SkillPoints[i] = 99;

        var sent = new List<Packet>();
        caster.Client.SendPacket(Arg.Do<Packet>(sent.Add), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var coordinator = provider.GetRequiredService<IMagicPacketCoordinator>();
        var packet = new Packet(GameOpcodes.GS_MAGIC_PROCESS);
        packet.WriteByte((byte)MagicProcessOpcode.Effecting);
        packet.WriteInt(DisguiseScroll);
        packet.WriteInt(caster.CharacterId);
        packet.WriteInt(-1);
        for (var i = 0; i < 7; i++)
            packet.WriteInt(0);

        await coordinator.HandleAsync(caster.Client, packet);

        var reply = sent.Should().ContainSingle().Subject;
        reply.GetOpcode().Should().Be((byte)GameOpcodes.GS_MAGIC_PROCESS);
        reply.ReadByte().Should().Be(
            (byte)MagicProcessOpcode.TransformationList,
            "a scroll with no primary type opens the mob picker rather than casting");
        reply.ReadInt().Should().Be(DisguiseScroll);
        reply.RemainingBytes.Should().Be(0);
    }

    [Fact]
    public async Task TransformingEmitsTheSamePacketsAsTheWorkingReference()
    {
        const int Menissiah = 500506;

        var magic = Load<MagicData>("Magic.json", m => m.Id);
        var items = Load<ItemData>("Items.json", i => i.Num);
        var row = magic[Menissiah];

        using var provider = CreateProvider(_ => { }, gameData =>
        {
            gameData.GetMagic(Arg.Any<int>()).Returns(c => magic.GetValueOrDefault(c.Arg<int>()));
            gameData.GetItem(Arg.Any<int>()).Returns(c => items.GetValueOrDefault(c.Arg<int>()));
            gameData.MagicType4Table.Returns(Load<MagicType4Data>("MagicType4.json", m => m.Id));
            gameData.MagicType6Table.Returns(Load<MagicType6Data>("MagicType6.json", m => m.Id));
            gameData.GetCoefficient(Arg.Any<short>()).Returns(new CoefficientData { ClassId = 110, Ac = 1 });
        });

        var sessionManager = provider.GetRequiredService<SessionManager>();
        var caster = CreateCaster(provider, sessionManager, row, items);
        caster.Nation = AccountNation.ElMorad;
        for (var i = 0; i < caster.SkillPoints.Length; i++)
            caster.SkillPoints[i] = 99;

        var sent = new List<Packet>();
        caster.Client.SendPacket(Arg.Do<Packet>(sent.Add), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var coordinator = provider.GetRequiredService<IMagicPacketCoordinator>();
        var packet = new Packet(GameOpcodes.GS_MAGIC_PROCESS);
        packet.WriteByte((byte)MagicProcessOpcode.Effecting);
        packet.WriteInt(Menissiah);
        packet.WriteInt(caster.CharacterId);
        packet.WriteInt(caster.CharacterId);
        for (var i = 0; i < 7; i++)
            packet.WriteInt(0);

        await coordinator.HandleAsync(caster.Client, packet);

        foreach (var s in sent)
            output.WriteLine($"  0x{s.GetOpcode():X2} {Convert.ToHexString(s.GetBytes())}");
        output.WriteLine($"  transformId now {caster.TransformId}");

        var magicPackets = sent.Where(s => s.GetOpcode() == (byte)GameOpcodes.GS_MAGIC_PROCESS).ToList();
        magicPackets.Should().HaveCount(2,
            "this skill is a transformation that also buffs, and each of its two types announces "
            + "itself; it looked like one packet only while the buff half had no row in the seed");

        var state = sent.Should()
            .ContainSingle(s => s.GetOpcode() == (byte)GameOpcodes.GS_STATE_CHANGE).Subject;
        state.ReadInt().Should().Be(caster.CharacterId);
        state.ReadByte().Should().Be((byte)StateChangeType.Transformation);
        state.ReadInt().Should().Be(Menissiah);
        state.RemainingBytes.Should().Be(0);

        var effecting = magicPackets[0];
        effecting.ReadByte().Should().Be((byte)MagicProcessOpcode.Effecting);
        effecting.ReadInt().Should().Be(Menissiah);
        effecting.ReadInt().Should().Be(caster.CharacterId);
        effecting.ReadInt().Should().Be(caster.CharacterId);
        effecting.ReadInt().Should().Be(0);
        effecting.ReadInt().Should().Be(1, "data[1] flags the transformation as applied");
        effecting.ReadInt().Should().Be(0);
        effecting.ReadInt().Should().Be(3600, "data[3] carries the duration");

        caster.TransformId.Should().Be(26009);
    }

    [Fact]
    public async Task AMobPickedFromTheListTransformsWithOnlyTheDisguiseScroll()
    {
        const int RavenHarpy = 472250;

        var magic = Load<MagicData>("Magic.json", m => m.Id);
        var items = Load<ItemData>("Items.json", i => i.Num);
        var row = magic[RavenHarpy];

        row.UseItem.Should().Be(379091000, "the skill asks for a Transformation Gem");
        row.BeforeAction.Should().Be(381001000, "and names the Disguise Scroll that listed it");

        using var provider = CreateProvider(_ => { }, gameData =>
        {
            gameData.GetMagic(Arg.Any<int>()).Returns(c => magic.GetValueOrDefault(c.Arg<int>()));
            gameData.GetItem(Arg.Any<int>()).Returns(c => items.GetValueOrDefault(c.Arg<int>()));
            gameData.MagicType4Table.Returns(Load<MagicType4Data>("MagicType4.json", m => m.Id));
            gameData.MagicType6Table.Returns(Load<MagicType6Data>("MagicType6.json", m => m.Id));
            gameData.GetCoefficient(Arg.Any<short>()).Returns(new CoefficientData { ClassId = 110, Ac = 1 });
        });

        var sessionManager = provider.GetRequiredService<SessionManager>();
        var caster = CreateCaster(provider, sessionManager, row, items);
        for (var i = 0; i < caster.SkillPoints.Length; i++)
            caster.SkillPoints[i] = 99;

        for (var i = InventoryConstants.InventoryStart; i < caster.Inventory.Length; i++)
            caster.Inventory[i] = new ItemSlot();
        caster.Inventory[InventoryConstants.InventoryStart] = new ItemSlot
        {
            ItemId = 381001000, Count = 10, Durability = 1
        };

        var sent = new List<Packet>();
        caster.Client.SendPacket(Arg.Do<Packet>(sent.Add), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var coordinator = provider.GetRequiredService<IMagicPacketCoordinator>();
        var packet = new Packet(GameOpcodes.GS_MAGIC_PROCESS);
        packet.WriteByte((byte)MagicProcessOpcode.Effecting);
        packet.WriteInt(RavenHarpy);
        packet.WriteInt(caster.CharacterId);
        packet.WriteInt(caster.CharacterId);
        for (var i = 0; i < 7; i++)
            packet.WriteInt(0);

        await coordinator.HandleAsync(caster.Client, packet);

        foreach (var s in sent)
            output.WriteLine($"  0x{s.GetOpcode():X2} {Convert.ToHexString(s.GetBytes())}");

        caster.TransformId.Should().Be(
            2200,
            "carrying the Disguise Scroll is enough on its own: it stands in for both the scroll and the gem");
        sent.Should().ContainSingle(s => s.GetOpcode() == (byte)GameOpcodes.GS_STATE_CHANGE);
    }

    [Fact]
    public async Task LoggingBackInReTransformsACharacterWhoLoggedOutTransformed()
    {
        const int Menissiah = 500506;

        var magic = Load<MagicData>("Magic.json", m => m.Id);
        var items = Load<ItemData>("Items.json", i => i.Num);

        using var provider = CreateProvider(_ => { }, gameData =>
        {
            gameData.GetMagic(Arg.Any<int>()).Returns(c => magic.GetValueOrDefault(c.Arg<int>()));
            gameData.GetItem(Arg.Any<int>()).Returns(c => items.GetValueOrDefault(c.Arg<int>()));
            gameData.MagicType6Table.Returns(Load<MagicType6Data>("MagicType6.json", m => m.Id));
            gameData.GetCoefficient(Arg.Any<short>()).Returns(new CoefficientData { ClassId = 110, Ac = 1 });
        });

        var sessionManager = provider.GetRequiredService<SessionManager>();
        var caster = CreateCaster(provider, sessionManager, magic[Menissiah], items);
        caster.ActiveBuffs[Menissiah] = new ActiveBuff
        {
            MagicId = Menissiah,
            CasterId = caster.CharacterId,
            Duration = 3600,
            ExpireTicks = DateTime.UtcNow.AddSeconds(3000).Ticks,
        };
        caster.RebuildSpecialStates(provider.GetRequiredService<IGameDataService>());

        var sent = new List<Packet>();
        caster.Client.SendPacket(Arg.Do<Packet>(sent.Add), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        await provider.GetRequiredService<ISavedMagicService>().RecastAsync(caster);

        foreach (var s in sent)
            output.WriteLine($"  0x{s.GetOpcode():X2} {Convert.ToHexString(s.GetBytes())}");

        caster.TransformId.Should().Be(26009, "the saved buff still transforms the character");

        var state = sent.Should()
            .ContainSingle(s => s.GetOpcode() == (byte)GameOpcodes.GS_STATE_CHANGE).Subject;
        state.ReadInt().Should().Be(caster.CharacterId);
        state.ReadByte().Should().Be(
            (byte)StateChangeType.Transformation,
            "without this the client draws a normal character and cannot cancel it");
        state.ReadInt().Should().Be(Menissiah);

        sent.Should().Contain(
            s => s.GetOpcode() == (byte)GameOpcodes.GS_MAGIC_PROCESS,
            "and the buff bar needs the effecting packet to show the icon");
    }

    [Fact]
    public void HighIntelligenceDoesNotInflateTheDisplayedResistances()
    {
        var gameData = Substitute.For<IGameDataService>();
        var inventory = new ItemSlot[InventoryConstants.InventoryTotal];
        for (var slot = 0; slot < inventory.Length; slot++)
            inventory[slot] = new ItemSlot();

        var stats = AbilityCalculator.Calculate(
            level: 70, strength: 60, stamina: 60, dexterity: 60, intelligence: 160,
            classId: 110, new CoefficientData { ClassId = 110, Ac = 1 }, inventory, gameData);

        output.WriteLine($"int 160 -> FireR={stats.FireR} ColdR={stats.ColdR} "
            + $"ResistanceBonus={stats.ResistanceBonus}");

        stats.ColdR.Should().Be(0, "the intelligence bonus is not a per-element resistance");
        stats.FireR.Should().Be(0);
        stats.MagicR.Should().Be(0);
        stats.ResistanceBonus.Should().Be(
            30, "an intelligence of 160 is worth half the excess, applied when damage is computed");
    }

    private const int MagicShield = 108802;
    private const int Judgment = 112802;
    private const int MagicShieldScroll = 379064000;
    private const int StoneOfRogue = 379060000;
    private const int JudgmentScroll = 379066000;
    private const int StoneOfPriest = 379062000;

    private static void GiveStones(UserSession caster, int stone, ushort count) =>
        caster.Inventory[InventoryConstants.InventoryStart + 1] = new ItemSlot { ItemId = stone, Count = count };

    private static Packet EffectingPacket(int skillId, int casterId, int targetId)
    {
        var packet = new Packet(GameOpcodes.GS_MAGIC_PROCESS);
        packet.WriteByte((byte)MagicProcessOpcode.Effecting);
        packet.WriteInt(skillId);
        packet.WriteInt(casterId);
        packet.WriteInt(targetId);
        for (var i = 0; i < 7; i++)
            packet.WriteInt(0);
        return packet;
    }

    private ServiceProvider CreateMasterSkillProvider(Dictionary<int, MagicData> magic, Dictionary<int, ItemData> items) =>
        CreateProvider(_ => { }, gameData =>
        {
            gameData.GetMagic(Arg.Any<int>()).Returns(c => magic.GetValueOrDefault(c.Arg<int>()));
            gameData.GetItem(Arg.Any<int>()).Returns(c => items.GetValueOrDefault(c.Arg<int>()));
            gameData.MagicType1Table.Returns(Load<MagicType1Data>("MagicType1.json", m => m.Id));
            gameData.MagicType4Table.Returns(Load<MagicType4Data>("MagicType4.json", m => m.Id));
            gameData.GetCoefficient(Arg.Any<short>()).Returns(new CoefficientData { ClassId = 110, Ac = 1 });
        });

    [Fact]
    public async Task AMasterSkillKeepsItsScrollAndSpendsOneClassStone()
    {
        var magic = Load<MagicData>("Magic.json", m => m.Id);
        var items = Load<ItemData>("Items.json", i => i.Num);
        magic[MagicShield].ConsumedItem.Should().Be(StoneOfRogue);

        using var provider = CreateMasterSkillProvider(magic, items);
        var sessionManager = provider.GetRequiredService<SessionManager>();
        var caster = CreateCaster(provider, sessionManager, magic[MagicShield], items);
        for (var i = 0; i < caster.SkillPoints.Length; i++)
            caster.SkillPoints[i] = 99;
        GiveStones(caster, StoneOfRogue, 3);

        await provider.GetRequiredService<IMagicPacketCoordinator>()
            .HandleAsync(caster.Client, EffectingPacket(MagicShield, caster.CharacterId, caster.CharacterId));

        caster.ActiveBuffs.Should().ContainKey(MagicShield);
        caster.Inventory[InventoryConstants.InventoryStart].ItemId.Should().Be(MagicShieldScroll, "the scroll is the requirement, never the cost");
        caster.Inventory[InventoryConstants.InventoryStart + 1].Count.Should().Be(2);
    }

    [Fact]
    public async Task AMasterSkillIsRefusedWithoutItsStone()
    {
        var magic = Load<MagicData>("Magic.json", m => m.Id);
        var items = Load<ItemData>("Items.json", i => i.Num);

        using var provider = CreateMasterSkillProvider(magic, items);
        var sessionManager = provider.GetRequiredService<SessionManager>();
        var caster = CreateCaster(provider, sessionManager, magic[MagicShield], items);
        for (var i = 0; i < caster.SkillPoints.Length; i++)
            caster.SkillPoints[i] = 99;

        await provider.GetRequiredService<IMagicPacketCoordinator>()
            .HandleAsync(caster.Client, EffectingPacket(MagicShield, caster.CharacterId, caster.CharacterId));

        caster.ActiveBuffs.Should().NotContainKey(MagicShield);
        caster.Inventory[InventoryConstants.InventoryStart].ItemId.Should().Be(MagicShieldScroll);
    }

    [Fact]
    public async Task JudgmentSpendsAStoneOfPriestOnTheStrike()
    {
        var magic = Load<MagicData>("Magic.json", m => m.Id);
        var items = Load<ItemData>("Items.json", i => i.Num);
        magic[Judgment].ConsumedItem.Should().Be(StoneOfPriest);

        using var provider = CreateMasterSkillProvider(magic, items);
        var sessionManager = provider.GetRequiredService<SessionManager>();
        var caster = CreateCaster(provider, sessionManager, magic[Judgment], items);
        for (var i = 0; i < caster.SkillPoints.Length; i++)
            caster.SkillPoints[i] = 99;
        GiveStones(caster, StoneOfPriest, 2);
        var monster = sessionManager.Regions.SpawnNpc(new NpcInstance
        {
            IsMonster = true,
            NpcId = 2000, NpcType = 0, ZoneId = 1, X = 50, Z = 50,
            MaxHp = 100000, Hp = 100000, Ac = 0, EvadeRate = 1
        });

        await provider.GetRequiredService<IMagicPacketCoordinator>()
            .HandleAsync(caster.Client, EffectingPacket(Judgment, caster.CharacterId, monster.UniqueId));
        await Task.Delay(400);

        monster.Hp.Should().BeLessThan(100000);
        caster.Inventory[InventoryConstants.InventoryStart].ItemId.Should().Be(JudgmentScroll);
        caster.Inventory[InventoryConstants.InventoryStart + 1].Count.Should().Be(1);
    }

    private static UserSession CreateCaster(
        ServiceProvider provider, SessionManager sessionManager, MagicData row,
        Dictionary<int, ItemData> items)
    {
        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        client.SendPacket(Arg.Any<Packet>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);

        var caster = sessionManager.CreateSession(client, characterId: 505, accountId: 605);
        caster.Name = "Caster";
        caster.ZoneId = 1;
        caster.Nation = AccountNation.Karus;
        caster.Class = (short)(row.Id / 1000);
        caster.Level = 70;
        caster.Hp = 1000;
        caster.MaxHp = 1000;
        caster.Mp = 3000;
        caster.MaxMp = 3000;
        caster.Magic = 200;
        caster.X = 50;
        caster.Z = 50;
        caster.Stats.TotalHit = 300;
        caster.Stats.TotalHitrate = 100;
        caster.Stats.TotalEvasionrate = 1;
        sessionManager.Regions.AddToRegion(caster);

        if (row.UseItem != 0)
        {
            var proto = items[row.UseItem];
            caster.Inventory[InventoryConstants.InventoryStart] = new ItemSlot
            {
                ItemId = row.UseItem,
                Count = proto.Countable == 0 ? (ushort)1 : (ushort)500,
                Durability = proto.Countable == 0 ? proto.Duration : (short)0
            };
        }

        return caster;
    }

    [Fact]
    public async Task ResistColdRaisesColdResistanceThroughTheCastPipeline()
    {
        var magic = Load<MagicData>("Magic.json", m => m.Id);
        var items = Load<ItemData>("Items.json", i => i.Num);
        var row = magic[ResistCold];

        using var provider = CreateProvider(_ => { }, gameData =>
        {
            gameData.GetMagic(Arg.Any<int>()).Returns(c => magic.GetValueOrDefault(c.Arg<int>()));
            gameData.GetItem(Arg.Any<int>()).Returns(c => items.GetValueOrDefault(c.Arg<int>()));
            gameData.MagicType4Table.Returns(Load<MagicType4Data>("MagicType4.json", m => m.Id));
            gameData.GetCoefficient(Arg.Any<short>()).Returns(new CoefficientData { ClassId = 110, Ac = 1 });
        });

        var sessionManager = provider.GetRequiredService<SessionManager>();
        var caster = CreateCaster(provider, sessionManager, row, items);
        for (var i = 0; i < caster.SkillPoints.Length; i++)
            caster.SkillPoints[i] = 99;
        var before = caster.Stats.ColdR;

        var coordinator = provider.GetRequiredService<IMagicPacketCoordinator>();
        var packet = new Packet(GameOpcodes.GS_MAGIC_PROCESS);
        packet.WriteByte((byte)MagicProcessOpcode.Effecting);
        packet.WriteInt(ResistCold);
        packet.WriteInt(caster.CharacterId);
        packet.WriteInt(caster.CharacterId);
        for (var i = 0; i < 7; i++)
            packet.WriteInt(0);

        await coordinator.HandleAsync(caster.Client, packet);

        output.WriteLine($"pipeline ColdR {before} -> {caster.Stats.ColdR}, buffs {caster.ActiveBuffs.Count}");
        caster.Stats.ColdR.Should().BeGreaterThan(before);
    }

    [Theory]
    [InlineData(FireBlast, "Fire Blast")]
    [InlineData(FireThorn, "Fire Thorn")]
    [InlineData(FireImpact, "Fire Impact")]
    [InlineData(IceOrb, "Ice Orb")]
    public async Task ReportedAttackSkillsDamageAMonsterThroughTheCastPipeline(int skillId, string name)
    {
        var magic = Load<MagicData>("Magic.json", m => m.Id);
        var items = Load<ItemData>("Items.json", i => i.Num);
        var row = magic[skillId];

        using var provider = CreateProvider(_ => { }, gameData =>
        {
            gameData.GetMagic(Arg.Any<int>()).Returns(c => magic.GetValueOrDefault(c.Arg<int>()));
            gameData.GetItem(Arg.Any<int>()).Returns(c => items.GetValueOrDefault(c.Arg<int>()));
            gameData.MagicType2Table.Returns(Load<MagicType2Data>("MagicType2.json", m => m.Id));
            gameData.MagicType3Table.Returns(Load<MagicType3Data>("MagicType3.json", m => m.Id));
            gameData.MagicType4Table.Returns(Load<MagicType4Data>("MagicType4.json", m => m.Id));
            gameData.GetCoefficient(Arg.Any<short>()).Returns(new CoefficientData { ClassId = 110, Ac = 1 });
        });

        var sessionManager = provider.GetRequiredService<SessionManager>();
        var caster = CreateCaster(provider, sessionManager, row, items);
        for (var i = 0; i < caster.SkillPoints.Length; i++)
            caster.SkillPoints[i] = 99;

        var monster = sessionManager.Regions.SpawnNpc(new NpcInstance
        {
            IsMonster = true,
            NpcId = 2000, NpcType = 0, ZoneId = 1, X = 50, Z = 50,
            MaxHp = 100000, Hp = 100000, Ac = 0, EvadeRate = 1
        });

        var coordinator = provider.GetRequiredService<IMagicPacketCoordinator>();
        var before = monster.Hp;

        var packet = new Packet(GameOpcodes.GS_MAGIC_PROCESS);
        packet.WriteByte((byte)MagicProcessOpcode.Effecting);
        packet.WriteInt(skillId);
        packet.WriteInt(caster.CharacterId);
        packet.WriteInt(monster.UniqueId);
        for (var i = 0; i < 7; i++)
            packet.WriteInt(0);

        await coordinator.HandleAsync(caster.Client, packet);
        await Task.Delay(400);

        var damage = before - monster.Hp;
        output.WriteLine($"{name} through the pipeline: damage {damage}");
        damage.Should().BeGreaterThan(0, "{0} must damage a monster through the cast pipeline", name);
    }

    [Fact]
    public async Task AResistBuffIsRefusedWhileAnotherResistBuffIsRunning()
    {
        const int ResistFire = 109506;

        var magic = Load<MagicData>("Magic.json", m => m.Id);
        var items = Load<ItemData>("Items.json", i => i.Num);

        using var provider = CreateProvider(_ => { }, gameData =>
        {
            gameData.GetMagic(Arg.Any<int>()).Returns(c => magic.GetValueOrDefault(c.Arg<int>()));
            gameData.GetItem(Arg.Any<int>()).Returns(c => items.GetValueOrDefault(c.Arg<int>()));
            gameData.MagicType4Table.Returns(Load<MagicType4Data>("MagicType4.json", m => m.Id));
            gameData.GetCoefficient(Arg.Any<short>()).Returns(new CoefficientData { ClassId = 110, Ac = 1 });
        });

        var sessionManager = provider.GetRequiredService<SessionManager>();
        var caster = CreateCaster(provider, sessionManager, magic[ResistFire], items);
        var execution = provider.GetRequiredService<IMagicExecutionService>();

        await execution.ExecuteAsync(caster, magic[ResistFire], ResistFire, caster.CharacterId, new int[7]);
        var fireR = caster.Stats.FireR;

        await execution.ExecuteAsync(caster, magic[ResistCold], ResistCold, caster.CharacterId, new int[7]);

        output.WriteLine($"after Resist Fire then Resist Cold: FireR={caster.Stats.FireR} "
            + $"ColdR={caster.Stats.ColdR} buffs={caster.ActiveBuffs.Count}");

        fireR.Should().BeGreaterThan(0, "Resist Fire lands on a clean character");
        caster.Stats.ColdR.Should().Be(
            0,
            "one resistance buff at a time is the rule");
    }

    [Fact]
    public async Task FiringAnArrowSkillTakesOneArrowNotTheWholeStack()
    {
        var magic = Load<MagicData>("Magic.json", m => m.Id);
        var items = Load<ItemData>("Items.json", i => i.Num);
        var row = magic[Viper];

        using var provider = CreateProvider(_ => { }, gameData =>
        {
            gameData.GetMagic(Arg.Any<int>()).Returns(c => magic.GetValueOrDefault(c.Arg<int>()));
            gameData.GetItem(Arg.Any<int>()).Returns(c => items.GetValueOrDefault(c.Arg<int>()));
            gameData.MagicType2Table.Returns(Load<MagicType2Data>("MagicType2.json", m => m.Id));
            gameData.MagicType3Table.Returns(Load<MagicType3Data>("MagicType3.json", m => m.Id));
        });

        var sessionManager = provider.GetRequiredService<SessionManager>();
        var caster = CreateCaster(provider, sessionManager, row, items);

        var usage = provider.GetRequiredService<IMagicItemUsageService>();
        var consumed = await usage.TryConsumeItemAsync(caster, row.UseItem);

        var left = CountReagent(caster, row.UseItem);
        output.WriteLine($"arrows left after firing one: {left} (consumed={consumed})");

        consumed.Should().BeTrue();
        left.Should().Be(499, "firing one arrow must not destroy the rest of the stack");
    }

    private static int CountReagent(UserSession caster, int itemId)
    {
        if (itemId == 0)
            return -1;

        var total = 0;
        for (var i = InventoryConstants.InventoryStart; i < caster.Inventory.Length; i++)
            if (caster.Inventory[i].ItemId == itemId)
                total += caster.Inventory[i].Count;
        return total;
    }
}
