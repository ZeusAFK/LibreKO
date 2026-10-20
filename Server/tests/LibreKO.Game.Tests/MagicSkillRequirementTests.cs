using FluentAssertions;
using LibreKO.Common.Domain.Entities.GameData;
using LibreKO.Common.Domain.Services;
using LibreKO.Common.Enums;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.Protocol;
using LibreKO.Game.World;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

using LibreKO.Game.Protocol.Writers;

namespace LibreKO.Game.Tests;

public class MagicSkillRequirementTests : GameTestBase
{
    private const int Freeze = 110603;
    private const int Blizzard = 110645;
    private const int BlizzardCastTenths = 15;
    private const int Slash = 101003;
    private const int LogosHorns = 106019;
    private const int SweepingPotion = 490201;
    private const int Endure = 105620;
    private const int Awakening = 115802;
    private const int ElmoradAwakening = 215802;
    private const int Shouting = 130013;
    private const int ElmoradAssault = 230014;
    private const int MonsterStone = 610064;
    private const int EventTreeSkill = 491330;
    private const int Adamant = 491068;
    private const int Fear = 492027;
    private const int AttackScroll = 500610;
    private const int StompGesture = 490049;
    private const int HealthCoins = 490076;
    private const int CommanderForward = 492066;
    private const int OfficialList = 500126;
    private const int GuardTowerArrow = 450018;
    private const int DrawbridgeDestruction = 120011;
    private const int ChaosSpiritSword = 490219;
    private const int Warrior = 105;
    private const int RamLook = 30040;
    private const int EventItem = 978006000;
    private const AccountNation Karus = AccountNation.Karus;
    private const AccountNation ElMorad = AccountNation.ElMorad;
    private const int KurianMaster = 115;

    private const int IceTree = 6;
    private const int LightningTree = 7;
    private const int MasterTree = 8;

    private static MagicData Magic(
        int id, int tree, int skillLevel, byte type1 = 3, int recastTenths = 0, int msp = 0, int castTenths = 0) =>
        new()
        {
            Id = id,
            Skill = (short)tree,
            SkillLevel = (short)skillLevel,
            Type1 = type1,
            ReCastTime = (short)recastTenths,
            Msp = (short)msp,
            CastTime = (byte)castTenths
        };

    private static byte[] Mastery(int fire = 0, int ice = 0, int lightning = 0, int master = 0)
    {
        var points = new byte[MagicSkillRequirement.MasteryPointSlots];
        points[5] = (byte)fire;
        points[6] = (byte)ice;
        points[7] = (byte)lightning;
        points[8] = (byte)master;
        return points;
    }

    [Fact]
    public void AMasterySkillNeedsPointsInItsOwnTree()
    {
        var freeze = Magic(Freeze, tree: 1106, skillLevel: 3);

        MagicSkillRequirement.MasteryTreeOf(freeze.Skill).Should().Be(IceTree);
        MagicSkillRequirement.IsMet(freeze, level: 60, Mastery(ice: 0)).Should().BeFalse();
        MagicSkillRequirement.IsMet(freeze, level: 60, Mastery(ice: 2)).Should().BeFalse();
        MagicSkillRequirement.IsMet(freeze, level: 60, Mastery(ice: 3)).Should().BeTrue();
    }

    [Fact]
    public void PointsInAnotherTreeDoNotUnlockASkill()
    {
        var blizzard = Magic(Blizzard, tree: 1106, skillLevel: 45);

        MagicSkillRequirement.IsMet(blizzard, level: 80, Mastery(fire: 60, lightning: 60)).Should().BeFalse();
        MagicSkillRequirement.IsMet(blizzard, level: 80, Mastery(ice: 45)).Should().BeTrue();
    }

    [Fact]
    public void ABasicTreeSkillIsGatedOnCharacterLevel()
    {
        var slash = Magic(Slash, tree: 1010, skillLevel: 3, type1: 1);

        MagicSkillRequirement.MasteryTreeOf(slash.Skill).Should().Be(0);
        MagicSkillRequirement.IsMet(slash, level: 2, Mastery()).Should().BeFalse();
        MagicSkillRequirement.IsMet(slash, level: 3, Mastery()).Should().BeTrue();
    }

    [Fact]
    public void ANationBuffTreeIsNotAMasteryTree()
    {
        var logosHorns = Magic(LogosHorns, tree: 7106, skillLevel: 1);

        MagicSkillRequirement.MasteryTreeOf(logosHorns.Skill).Should().Be(0);
        MagicSkillRequirement.IsMet(logosHorns, level: 1, Mastery()).Should().BeTrue();
    }

    [Theory]
    [InlineData(4110, 0, 0, false)]
    [InlineData(7106, 0, 0, false)]
    [InlineData(4110, 1, 0, true)]
    [InlineData(5110, 1, 0, false)]
    [InlineData(8110, 2, 4, true)]
    [InlineData(7110, 2, 4, false)]
    [InlineData(9110, 2, 0, true)]
    public void ANationBuffNeedsTheNationRoleThatUnlocksItsBand(int tree, int role, int line, bool granted)
    {
        MagicSkillRequirement.IsGranted(Magic(LogosHorns, tree, skillLevel: 1), transformId: 0, role, line)
            .Should().Be(granted);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(30010, false)]
    [InlineData(31501, true)]
    [InlineData(20008, true)]
    public void ACommandTreeSkillNeedsACommandForm(int transformId, bool granted)
    {
        MagicSkillRequirement.IsGranted(Magic(LogosHorns, tree: 1019, skillLevel: 1), transformId).Should().Be(granted);
    }

    [Fact]
    public void AnOrdinaryTreeNeedsNoRoleOrForm()
    {
        MagicSkillRequirement.IsGranted(Magic(LogosHorns, tree: 1106, skillLevel: 3), transformId: 0).Should().BeTrue();
        MagicSkillRequirement.IsGranted(Magic(LogosHorns, tree: 1010, skillLevel: 1), transformId: 0).Should().BeTrue();
    }

    [Fact]
    public void ASkillWithNoRequirementIsAlwaysAvailable()
    {
        var potion = Magic(SweepingPotion, tree: 0, skillLevel: 0);

        MagicSkillRequirement.IsMet(potion, level: 1, Mastery()).Should().BeTrue();
    }

    [Theory]
    [InlineData(Awakening, 1158, 0, true)]
    [InlineData(ElmoradAwakening, 2158, 0, true)]
    [InlineData(Endure, 1056, 0, true)]
    [InlineData(Slash, 1010, 1, false)]
    [InlineData(Shouting, 0, 0, false)]
    [InlineData(ElmoradAssault, 1000, 0, false)]
    [InlineData(MonsterStone, 0, 0, false)]
    [InlineData(EventTreeSkill, 3200, 0, false)]
    public void OnlyAPassiveSkillOfAClassTreeIsRefused(int skillId, int tree, int type1, bool passive)
    {
        MagicSkillRequirement.IsClassPassive(Magic(skillId, tree, skillLevel: 1, (byte)type1)).Should().Be(passive);
    }

    [Theory]
    [InlineData(Adamant, 0)]
    [InlineData(Fear, 0)]
    [InlineData(AttackScroll, 0)]
    [InlineData(MonsterStone, 0)]
    [InlineData(DrawbridgeDestruction, 0)]
    [InlineData(OfficialList, 0)]
    [InlineData(GuardTowerArrow, 0)]
    public void ARowOfNoClassWithoutAnItemIsNotForPlayers(int skillId, int tree)
    {
        MagicSkillRequirement.IsCastableBy(Magic(skillId, tree, skillLevel: 0), Warrior, transformId: 0, Karus).Should().BeFalse();
    }

    [Theory]
    [InlineData(StompGesture)]
    [InlineData(HealthCoins)]
    [InlineData(CommanderForward)]
    [InlineData(Shouting)]
    public void TheRowsTheClientCastsWithoutAnItemStayOpen(int skillId)
    {
        MagicSkillRequirement.IsCastableBy(Magic(skillId, tree: 0, skillLevel: 0), Warrior, transformId: 0, Karus).Should().BeTrue();
    }

    [Fact]
    public void AShoutBelongsToItsOwnNation()
    {
        MagicSkillRequirement.IsCastableBy(Magic(ElmoradAssault, tree: 0, skillLevel: 0), Warrior, transformId: 0, Karus)
            .Should().BeFalse();
        MagicSkillRequirement.IsCastableBy(Magic(ElmoradAssault, tree: 0, skillLevel: 0), Warrior, transformId: 0, ElMorad)
            .Should().BeTrue();
    }

    [Fact]
    public void ARamStrikeNeedsTheRamFormOfItsOwnNation()
    {
        var strike = Magic(DrawbridgeDestruction, tree: 0, skillLevel: 0);

        MagicSkillRequirement.IsCastableBy(strike, Warrior, RamLook, Karus).Should().BeTrue();
        MagicSkillRequirement.IsCastableBy(strike, Warrior, transformId: 0, Karus).Should().BeFalse();
        MagicSkillRequirement.IsCastableBy(strike, Warrior, RamLook, ElMorad).Should().BeFalse();
    }

    [Fact]
    public void AnEventRowAndAnItemRowAreLeftToTheirOwnRules()
    {
        MagicSkillRequirement.IsCastableBy(Magic(ChaosSpiritSword, ChaosRules.ChaosSkillTree, 0), Warrior, 0, Karus).Should().BeTrue();
        var scroll = Magic(Adamant, tree: 0, skillLevel: 0);
        scroll.UseItem = EventItem;
        MagicSkillRequirement.IsCastableBy(scroll, Warrior, 0, Karus).Should().BeTrue();
    }

    [Fact]
    public async Task TheCoordinatorRefusesALearnedPassiveSkillAndKeepsTheMana()
    {
        using var provider = CreateProvider(
            _ => { },
            gameData => gameData.GetMagic(Awakening)
                .Returns(Magic(Awakening, tree: 1158, skillLevel: 2, type1: 0, msp: 300)));

        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        client.SendPacket(Arg.Any<Packet>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);

        var sessionManager = provider.GetRequiredService<SessionManager>();
        var kurian = sessionManager.CreateSession(client, characterId: 903, accountId: 904);
        kurian.Name = "Brann";
        kurian.Class = KurianMaster;
        kurian.Level = 80;
        kurian.ZoneId = 21;
        kurian.Hp = 1000;
        kurian.MaxHp = 1000;
        kurian.Mp = 1000;
        kurian.MaxMp = 1000;
        kurian.SkillPoints[MasterTree] = 2;
        sessionManager.Regions.AddToRegion(kurian);

        var coordinator = provider.GetRequiredService<IMagicPacketCoordinator>();
        await coordinator.HandleAsync(client, CastPacket(Awakening, kurian.CharacterId, MagicProcessOpcode.Effecting));

        kurian.Mp.Should().Be(1000);
    }

    [Fact]
    public async Task TheCoordinatorDropsACastOfAnUnlearnedSkill()
    {
        using var provider = CreateProvider(
            _ => { },
            gameData => gameData.GetMagic(Blizzard)
                .Returns(Magic(Blizzard, tree: 1106, skillLevel: 45, recastTenths: 153, castTenths: BlizzardCastTenths)));

        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        client.SendPacket(Arg.Any<Packet>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);

        var sessionManager = provider.GetRequiredService<SessionManager>();
        var mage = sessionManager.CreateSession(client, characterId: 901, accountId: 902);
        mage.Name = "Aria";
        mage.Class = 110;
        mage.Level = 80;
        mage.ZoneId = 21;
        mage.Hp = 1000;
        mage.MaxHp = 1000;
        mage.Mp = 1000;
        mage.MaxMp = 1000;
        sessionManager.Regions.AddToRegion(mage);

        var coordinator = provider.GetRequiredService<IMagicPacketCoordinator>();
        await coordinator.HandleAsync(client, CastPacket(Blizzard, mage.CharacterId));

        mage.SkillCooldowns.Should().BeEmpty();
        mage.CastingSkillId.Should().Be(0);

        mage.SkillPoints[IceTree] = 45;
        await coordinator.HandleAsync(client, CastPacket(Blizzard, mage.CharacterId));

        mage.SkillCooldowns.Should().NotBeEmpty();
    }

    private static Packet CastPacket(int skillId, int casterId, MagicProcessOpcode stage = MagicProcessOpcode.Casting)
    {
        var packet = new Packet(GameOpcodes.GS_MAGIC_PROCESS);
        packet.WriteByte((byte)stage);
        packet.WriteInt(skillId);
        packet.WriteInt(casterId);
        packet.WriteInt(casterId);
        for (var index = 0; index < 7; index++)
            packet.WriteInt(0);
        return packet;
    }
}
