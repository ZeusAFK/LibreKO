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

public class MagicWarpTests : GameTestBase
{
    private const int EscapeId = 209035;
    private const int SummonFriendId = 209004;
    private const int DescentId = 205650;
    private const int WildAdventId = 208770;
    private const int BlinkId = 210774;
    private const short BlinkRadius = 20;
    private const byte Moradon = 21;
    private const short BindEventIndex = 7;

    [Fact]
    public async Task EscapeSendsTheCasterToTheirBindPoint()
    {
        using var provider = CreateProvider(_ => { }, gameData =>
        {
            Warp(gameData, EscapeId, SkillMoral.PartyAll, MagicWarpType.BindPoint);
            StartAt(gameData, karusX: 900, elmoradX: 900);
        });

        var (sessionManager, caster, client) = CreateCaster(provider);
        sessionManager.Maps = CreateMapManagerWithObjectEvent(Moradon, new ObjectEvent
        {
            Index = BindEventIndex,
            PosX = 300,
            PosZ = 400,
            Life = 1
        });
        caster.Quest.BindPoint = BindEventIndex;

        await Cast(provider, client, EscapeId, caster, caster.CharacterId);

        caster.X.Should().BeApproximately(300, 0.5f);
        caster.Z.Should().BeApproximately(400, 0.5f);
    }

    [Fact]
    public async Task EscapeFallsBackToTheNationStartPositionWhenNothingIsBound()
    {
        using var provider = CreateProvider(_ => { }, gameData =>
        {
            Warp(gameData, EscapeId, SkillMoral.PartyAll, MagicWarpType.BindPoint);
            StartAt(gameData, karusX: 512, elmoradX: 640);
        });

        var (_, caster, client) = CreateCaster(provider);
        caster.Nation = AccountNation.ElMorad;

        await Cast(provider, client, EscapeId, caster, caster.CharacterId);

        caster.X.Should().BeApproximately(640, 0.5f);
    }

    [Fact]
    public async Task EscapeIgnoresADestinationSuppliedByTheClient()
    {
        using var provider = CreateProvider(_ => { }, gameData =>
        {
            Warp(gameData, EscapeId, SkillMoral.PartyAll, MagicWarpType.BindPoint);
            StartAt(gameData, karusX: 512, elmoradX: 512);
        });

        var (_, caster, client) = CreateCaster(provider);

        await Cast(provider, client, EscapeId, caster, caster.CharacterId,
            [8000, 0, 9000, 0, 0, 0, 0]);

        caster.X.Should().BeApproximately(512, 0.5f, "the destination is the server's, never the sender's");
        caster.Z.Should().NotBe(900);
    }

    [Fact]
    public async Task EscapeTakesTheWholePartyHomeNotJustTheCaster()
    {
        using var provider = CreateProvider(_ => { }, gameData =>
        {
            Warp(gameData, EscapeId, SkillMoral.PartyAll, MagicWarpType.BindPoint);
            StartAt(gameData, karusX: 512, elmoradX: 512);
        });

        var (sessionManager, caster, client) = CreateCaster(provider);
        var friend = CreateOther(sessionManager, AccountNation.Karus, x: 800, z: 800);

        var party = sessionManager.Parties.CreateParty((short)caster.CharacterId);
        party.MemberIds[1] = (short)friend.CharacterId;
        caster.PartyIndex = party.Index;
        caster.IsPartyLeader = true;
        friend.PartyIndex = party.Index;

        await Cast(provider, client, EscapeId, caster, caster.CharacterId);

        caster.X.Should().BeApproximately(512, 0.5f);
        friend.X.Should().BeApproximately(512, 0.5f, "escape takes the party, not only the caster");
    }

    [Fact]
    public async Task SummonFriendPullsAPartyMemberToTheCaster()
    {
        using var provider = CreateProvider(_ => { }, gameData =>
            Warp(gameData, SummonFriendId, SkillMoral.Party, MagicWarpType.SummonInZone));

        var (sessionManager, caster, client) = CreateCaster(provider);
        caster.X = 250;
        caster.Z = 260;

        var friend = CreateOther(sessionManager, AccountNation.Karus, x: 700, z: 700);

        await Cast(provider, client, SummonFriendId, caster, friend.CharacterId);

        friend.X.Should().BeApproximately(250, 0.5f);
        friend.Z.Should().BeApproximately(260, 0.5f);
        caster.X.Should().BeApproximately(250, 0.5f, "the caster does not move");
    }

    [Fact]
    public async Task DescentMovesTheCasterToTheTarget()
    {
        using var provider = CreateProvider(_ => { }, gameData =>
            Warp(gameData, DescentId, SkillMoral.Party, MagicWarpType.MoveToTarget));

        var (sessionManager, caster, client) = CreateCaster(provider);
        var friend = CreateOther(sessionManager, AccountNation.Karus, x: 640, z: 480);

        await Cast(provider, client, DescentId, caster, friend.CharacterId);

        caster.X.Should().BeApproximately(640, 0.5f);
        caster.Z.Should().BeApproximately(480, 0.5f);
        friend.X.Should().BeApproximately(640, 0.5f, "the target does not move");
    }

    [Fact]
    public async Task DescentRefusesAnEnemyWhereWildAdventAcceptsOne()
    {
        using var provider = CreateProvider(_ => { }, gameData =>
        {
            Warp(gameData, DescentId, SkillMoral.Party, MagicWarpType.MoveToTarget);
            Warp(gameData, WildAdventId, SkillMoral.Enemy, MagicWarpType.MoveToTarget);
        });

        var (sessionManager, caster, client) = CreateCaster(provider);
        var foe = CreateOther(sessionManager, AccountNation.ElMorad, x: 640, z: 480);

        await Cast(provider, client, DescentId, caster, foe.CharacterId);
        caster.X.Should().BeApproximately(100, 0.5f, "a party warp does not reach across nations");

        await Cast(provider, client, WildAdventId, caster, foe.CharacterId);
        caster.X.Should().BeApproximately(640, 0.5f);
    }

    [Fact]
    public async Task BlinkMovesTheCasterToThePointItSentAhead()
    {
        using var provider = CreateProvider(_ => { }, gameData =>
            Warp(gameData, BlinkId, SkillMoral.Self, MagicWarpType.Blink, BlinkRadius));

        var (_, caster, client) = CreateCaster(provider);

        await Cast(provider, client, BlinkId, caster, caster.CharacterId, [1180, 0, 1060, 0, 0, 0, 0]);

        caster.X.Should().BeApproximately(118, 0.5f);
        caster.Z.Should().BeApproximately(106, 0.5f);
    }

    [Fact]
    public async Task BlinkRefusesAPointBeyondItsRadius()
    {
        using var provider = CreateProvider(_ => { }, gameData =>
            Warp(gameData, BlinkId, SkillMoral.Self, MagicWarpType.Blink, BlinkRadius));

        var (_, caster, client) = CreateCaster(provider);

        await Cast(provider, client, BlinkId, caster, caster.CharacterId, [4000, 0, 1000, 0, 0, 0, 0]);

        caster.X.Should().BeApproximately(100, 0.5f, "a blink reaches only its radius");
        caster.Z.Should().BeApproximately(100, 0.5f);
    }

    [Fact]
    public async Task BlinkRefusesAPointOffTheMap()
    {
        using var provider = CreateProvider(_ => { }, gameData =>
            Warp(gameData, BlinkId, SkillMoral.Self, MagicWarpType.Blink, BlinkRadius));

        var (_, caster, client) = CreateCaster(provider);

        await Cast(provider, client, BlinkId, caster, caster.CharacterId, [-50, 0, 1000, 0, 0, 0, 0]);

        caster.X.Should().BeApproximately(100, 0.5f);
    }

    [Fact]
    public async Task BlinkReadsACoordinateBeyondAShortAsItsLowSixteenBits()
    {
        using var provider = CreateProvider(_ => { }, gameData =>
            Warp(gameData, BlinkId, SkillMoral.Self, MagicWarpType.Blink, BlinkRadius));

        var (_, caster, client) = CreateCaster(provider);
        caster.X = 3300;

        await Cast(provider, client, BlinkId, caster, caster.CharacterId, [unchecked((short)33150), 0, 1000, 0, 0, 0, 0]);

        caster.X.Should().BeApproximately(3315, 0.5f, "the client sends each coordinate as a sign-extended short");
    }

    private static void Warp(
        IGameDataService gameData, int skillId, SkillMoral moral, MagicWarpType warpType, short radius = 0)
    {
        gameData.GetMagic(skillId).Returns(new MagicData
        {
            Id = skillId,
            Type1 = 8,
            Moral = (byte)moral,
            Range = 10000
        });

        var rows = new Dictionary<int, MagicType8Data>(
            gameData.MagicType8Table ?? new Dictionary<int, MagicType8Data>())
        {
            [skillId] = new MagicType8Data { Id = skillId, WarpType = (byte)warpType, Radius = radius }
        };
        gameData.MagicType8Table.Returns(rows);
    }

    private static void StartAt(IGameDataService gameData, short karusX, short elmoradX)
        => gameData.GetStartPosition(Moradon).Returns(new StartPositionData
        {
            ZoneId = Moradon,
            KarusX = karusX,
            KarusZ = karusX,
            ElmoradX = elmoradX,
            ElmoradZ = elmoradX
        });

    private static (SessionManager Sessions, UserSession Caster, IClient Client) CreateCaster(ServiceProvider provider)
    {
        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        client.SendPacket(Arg.Any<Packet>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);

        var sessionManager = provider.GetRequiredService<SessionManager>();
        var caster = sessionManager.CreateSession(client, characterId: 900, accountId: 950);
        caster.Name = "Caster";
        caster.Class = 205;
        caster.Level = 70;
        caster.Nation = AccountNation.Karus;
        caster.ZoneId = Moradon;
        caster.X = 100;
        caster.Z = 100;
        caster.Hp = 500;
        caster.MaxHp = 500;
        caster.Mp = 500;
        caster.MaxMp = 500;
        sessionManager.Regions.AddToRegion(caster);
        return (sessionManager, caster, client);
    }

    private static UserSession CreateOther(SessionManager sessionManager, AccountNation nation, float x, float z)
    {
        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        client.SendPacket(Arg.Any<Packet>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);

        var other = sessionManager.CreateSession(client, characterId: 901, accountId: 951);
        other.Name = "Other";
        other.Class = 205;
        other.Level = 70;
        other.Nation = nation;
        other.ZoneId = Moradon;
        other.X = x;
        other.Z = z;
        other.Hp = 400;
        other.MaxHp = 400;
        sessionManager.Regions.AddToRegion(other);
        return other;
    }

    private static Task Cast(
        ServiceProvider provider, IClient client, int skillId, UserSession caster, int targetId)
        => Cast(provider, client, skillId, caster, targetId, new int[7]);

    private static async Task Cast(
        ServiceProvider provider, IClient client, int skillId, UserSession caster, int targetId, int[] data)
    {
        var packet = new Packet(GameOpcodes.GS_MAGIC_PROCESS);
        packet.WriteByte((byte)MagicProcessOpcode.Effecting);
        packet.WriteInt(skillId);
        packet.WriteInt(caster.CharacterId);
        packet.WriteInt(targetId);
        foreach (var value in data)
            packet.WriteInt(value);

        await provider.GetRequiredService<IMagicPacketCoordinator>().HandleAsync(client, packet);
    }
}
