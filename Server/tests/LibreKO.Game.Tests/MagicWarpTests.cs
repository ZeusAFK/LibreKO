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
    private const int RetrieveClanMemberId = 490042;
    private const short Wolves = 71;
    private const short Hounds = 72;
    private const int OtherRoom = 3;
    private const int DescentId = 205650;
    private const short DescentRadius = 60;
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

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, true)]
    public async Task EscapeLeavesABusyPartyMemberWhereTheyAre(bool trading, bool selling, bool mining)
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
        if (trading)
            friend.Trade.ExchangeUser = caster.CharacterId;
        if (selling)
            friend.Trade.MerchantState = MerchantMode.Selling;
        friend.IsMining = mining;

        await Cast(provider, client, EscapeId, caster, caster.CharacterId);

        var busy = trading || selling || mining;
        friend.X.Should().BeApproximately(busy ? 800 : 512, 0.5f, "a trade, a stall or gathering counts on the player staying put");
        caster.X.Should().BeApproximately(512, 0.5f);
    }

    [Fact]
    public async Task EscapeTakesABusyCasterHome()
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
        caster.Trade.ExchangeUser = friend.CharacterId;

        await Cast(provider, client, EscapeId, caster, caster.CharacterId);

        caster.X.Should().BeApproximately(512, 0.5f, "the caster chose to go");
    }

    [Fact]
    public async Task AZoneChangeEndsGathering()
    {
        using var provider = CreateProvider(_ => { });
        var (_, caster, _) = CreateCaster(provider);
        caster.IsMining = true;

        await provider.GetRequiredService<IZoneTransitionService>().ChangeZoneAsync(caster, caster.ZoneId, caster.X + 10, caster.Z);

        caster.IsMining.Should().BeFalse("gathering belongs to the spot it started on");
    }

    [Fact]
    public async Task APullEndsGathering()
    {
        using var provider = CreateProvider(_ => { });
        var (_, caster, _) = CreateCaster(provider);
        caster.IsFishing = true;

        await provider.GetRequiredService<IWorldMovementService>().DisplaceAsync(caster, caster.X + 5, caster.Z);

        caster.IsFishing.Should().BeFalse("gathering belongs to the spot it started on");
    }

    [Fact]
    public async Task AWarpEndsGathering()
    {
        using var provider = CreateProvider(_ => { }, gameData =>
        {
            Warp(gameData, EscapeId, SkillMoral.PartyAll, MagicWarpType.BindPoint);
            StartAt(gameData, karusX: 512, elmoradX: 512);
        });
        var (_, caster, client) = CreateCaster(provider);
        caster.IsMining = true;

        await Cast(provider, client, EscapeId, caster, caster.CharacterId);

        caster.X.Should().BeApproximately(512, 0.5f);
        caster.IsMining.Should().BeFalse("gathering belongs to the spot it started on");
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
        caster.PartyIndex = friend.PartyIndex = 1;

        await Cast(provider, client, SummonFriendId, caster, friend.CharacterId);

        friend.X.Should().BeApproximately(250, 0.5f);
        friend.Z.Should().BeApproximately(260, 0.5f);
        caster.X.Should().BeApproximately(250, 0.5f, "the caster does not move");
    }

    [Theory]
    [InlineData(false, 0)]
    [InlineData(true, OtherRoom)]
    public async Task SummonFriendLeavesAnyoneOutsideThePartyOrTheRoomWhereTheyAre(bool sameParty, int room)
    {
        using var provider = CreateProvider(_ => { }, gameData =>
            Warp(gameData, SummonFriendId, SkillMoral.Party, MagicWarpType.SummonInZone));
        var (sessionManager, caster, client) = CreateCaster(provider);
        var stranger = CreateOther(sessionManager, AccountNation.Karus, x: 700, z: 700);
        if (sameParty)
            caster.PartyIndex = stranger.PartyIndex = 1;
        stranger.Room = (ushort)room;

        await Cast(provider, client, SummonFriendId, caster, stranger.CharacterId);

        stranger.X.Should().BeApproximately(700, 0.5f);
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, true)]
    public async Task SummonFriendLeavesABusyPartyMemberWhereTheyAre(bool trading, bool selling, bool mining)
    {
        using var provider = CreateProvider(_ => { }, gameData =>
            Warp(gameData, SummonFriendId, SkillMoral.Party, MagicWarpType.SummonInZone));
        var (sessionManager, caster, client) = CreateCaster(provider);
        var friend = CreateOther(sessionManager, AccountNation.Karus, x: 700, z: 700);
        caster.PartyIndex = friend.PartyIndex = 1;
        if (trading)
            friend.Trade.ExchangeUser = caster.CharacterId;
        if (selling)
            friend.Trade.MerchantState = MerchantMode.Selling;
        friend.IsMining = mining;

        await Cast(provider, client, SummonFriendId, caster, friend.CharacterId);

        var busy = trading || selling || mining;
        friend.X.Should().BeApproximately(busy ? 700 : caster.X, 0.5f, "a trade, a stall or gathering counts on the player staying put");
    }

    [Theory]
    [InlineData(Wolves, true)]
    [InlineData(Hounds, false)]
    public async Task RetrievingAClanMemberPullsOnlySomeoneOfTheCastersClan(short clan, bool pulled)
    {
        using var provider = CreateProvider(_ => { }, gameData =>
            Warp(gameData, RetrieveClanMemberId, SkillMoral.ClanAll, MagicWarpType.SummonInZone));
        var (sessionManager, caster, client) = CreateCaster(provider);
        var member = CreateOther(sessionManager, AccountNation.Karus, x: 700, z: 700);
        caster.KnightsId = Wolves;
        member.KnightsId = clan;

        await Cast(provider, client, RetrieveClanMemberId, caster, member.CharacterId);

        member.X.Should().BeApproximately(pulled ? caster.X : 700, 0.5f);
    }

    [Fact]
    public async Task DescentMovesTheCasterToThePartyMember()
    {
        using var provider = CreateProvider(_ => { }, gameData =>
            Warp(gameData, DescentId, SkillMoral.Party, MagicWarpType.MoveToTarget, DescentRadius));

        var (sessionManager, caster, client) = CreateCaster(provider);
        var friend = CreateOther(sessionManager, AccountNation.Karus, x: 130, z: 140);
        caster.PartyIndex = friend.PartyIndex = 1;

        await Cast(provider, client, DescentId, caster, friend.CharacterId);

        caster.X.Should().BeApproximately(130, 0.5f);
        caster.Z.Should().BeApproximately(140, 0.5f);
        friend.X.Should().BeApproximately(130, 0.5f, "the target does not move");
    }

    [Fact]
    public async Task DescentRefusesAPlayerOutsideTheParty()
    {
        using var provider = CreateProvider(_ => { }, gameData =>
            Warp(gameData, DescentId, SkillMoral.Party, MagicWarpType.MoveToTarget, DescentRadius));

        var (sessionManager, caster, client) = CreateCaster(provider);
        var stranger = CreateOther(sessionManager, AccountNation.Karus, x: 130, z: 140);

        await Cast(provider, client, DescentId, caster, stranger.CharacterId);

        caster.X.Should().BeApproximately(100, 0.5f);
    }

    [Fact]
    public async Task DescentRefusesAPartyMemberBeyondItsRadius()
    {
        using var provider = CreateProvider(_ => { }, gameData =>
            Warp(gameData, DescentId, SkillMoral.Party, MagicWarpType.MoveToTarget, DescentRadius));

        var (sessionManager, caster, client) = CreateCaster(provider);
        var friend = CreateOther(sessionManager, AccountNation.Karus, x: 640, z: 480);
        caster.PartyIndex = friend.PartyIndex = 1;

        await Cast(provider, client, DescentId, caster, friend.CharacterId);

        caster.X.Should().BeApproximately(100, 0.5f, "the member stands far beyond the skill's reach");
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

    private const int RushId = 114509;
    private const int PullId = 115570;
    private const short DashRange = 10;
    private const int EffectingResultSlot = 1;

    [Fact]
    public async Task RushMovesTheCasterBesideAnEnemyAndReportsSuccess()
    {
        using var provider = CreateProvider(_ => { }, gameData =>
            Warp(gameData, RushId, SkillMoral.Enemy, MagicWarpType.Rush, range: DashRange));

        var (sessionManager, caster, client) = CreateCaster(provider);
        var foe = CreateOther(sessionManager, AccountNation.ElMorad, x: 108, z: 100);
        InRonarkLand(sessionManager, caster, foe);
        var sent = Capture(client);

        await Cast(provider, client, RushId, caster, foe.CharacterId, [1069, 0, 1000, 0, 0, 0, 0]);

        caster.X.Should().BeApproximately(106.9f, 0.05f);
        caster.Z.Should().BeApproximately(100f, 0.05f);
        EffectingResult(sent, RushId).Should().Be(1);
    }

    [Fact]
    public async Task RushRefusesALandingAwayFromTheTarget()
    {
        using var provider = CreateProvider(_ => { }, gameData =>
            Warp(gameData, RushId, SkillMoral.Enemy, MagicWarpType.Rush, range: DashRange));

        var (sessionManager, caster, client) = CreateCaster(provider);
        var foe = CreateOther(sessionManager, AccountNation.ElMorad, x: 108, z: 100);
        InRonarkLand(sessionManager, caster, foe);

        await Cast(provider, client, RushId, caster, foe.CharacterId, [1080, 0, 1100, 0, 0, 0, 0]);

        caster.X.Should().BeApproximately(100f, 0.05f, "the landing must sit beside the target");
    }

    [Fact]
    public async Task RushRefusesAFriendlyTarget()
    {
        using var provider = CreateProvider(_ => { }, gameData =>
            Warp(gameData, RushId, SkillMoral.Enemy, MagicWarpType.Rush, range: DashRange));

        var (sessionManager, caster, client) = CreateCaster(provider);
        var friend = CreateOther(sessionManager, AccountNation.Karus, x: 108, z: 100);
        InRonarkLand(sessionManager, caster, friend);

        await Cast(provider, client, RushId, caster, friend.CharacterId, [1069, 0, 1000, 0, 0, 0, 0]);

        caster.X.Should().BeApproximately(100f, 0.05f);
    }

    [Fact]
    public async Task PullEitherDragsTheEnemyBesideTheCasterOrReportsFailure()
    {
        using var provider = CreateProvider(_ => { }, gameData =>
            Warp(gameData, PullId, SkillMoral.Enemy, MagicWarpType.Pull, range: DashRange));

        var (sessionManager, caster, client) = CreateCaster(provider);
        var foe = CreateOther(sessionManager, AccountNation.ElMorad, x: 110, z: 100);
        InRonarkLand(sessionManager, caster, foe);
        var sent = Capture(client);

        await Cast(provider, client, PullId, caster, foe.CharacterId, [1011, 0, 1000, 0, 0, 0, 0]);

        var result = EffectingResult(sent, PullId);
        if (result == 1)
            foe.X.Should().BeApproximately(101.1f, 0.05f);
        else
        {
            result.Should().Be(0);
            foe.X.Should().BeApproximately(110f, 0.05f, "a failed pull leaves the target in place");
        }
        caster.X.Should().BeApproximately(100f, 0.05f, "the caster never moves on a pull");
    }

    [Fact]
    public async Task PullRefusesALandingAwayFromTheCaster()
    {
        using var provider = CreateProvider(_ => { }, gameData =>
            Warp(gameData, PullId, SkillMoral.Enemy, MagicWarpType.Pull, range: DashRange));

        var (sessionManager, caster, client) = CreateCaster(provider);
        var foe = CreateOther(sessionManager, AccountNation.ElMorad, x: 110, z: 100);
        InRonarkLand(sessionManager, caster, foe);
        var sent = Capture(client);

        await Cast(provider, client, PullId, caster, foe.CharacterId, [1090, 0, 1000, 0, 0, 0, 0]);

        foe.X.Should().BeApproximately(110f, 0.05f);
        sent.Should().NotContain(p => IsEffecting(p, PullId));
    }

    private static void InRonarkLand(SessionManager sessionManager, params UserSession[] sessions)
    {
        foreach (var session in sessions)
        {
            sessionManager.Regions.RemoveFromRegion(session);
            session.ZoneId = (byte)ZoneId.RonarkLand;
            sessionManager.Regions.AddToRegion(session);
        }
    }

    private static List<Packet> Capture(IClient client)
    {
        var sent = new List<Packet>();
        client.SendPacket(Arg.Do<Packet>(sent.Add), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        return sent;
    }

    private static bool IsEffecting(Packet packet, int skillId)
    {
        if (packet.GetOpcode() != (byte)GameOpcodes.GS_MAGIC_PROCESS) return false;
        packet.ResetOffset();
        return packet.ReadByte() == (byte)MagicProcessOpcode.Effecting && packet.ReadInt() == skillId;
    }

    private static int EffectingResult(List<Packet> sent, int skillId)
    {
        var packet = sent.Single(p => IsEffecting(p, skillId));
        packet.ResetOffset();
        packet.ReadByte();
        packet.ReadInt();
        packet.ReadInt();
        packet.ReadInt();
        for (var slot = 0; slot < EffectingResultSlot; slot++)
            packet.ReadInt();
        return packet.ReadInt();
    }

    private static void Warp(
        IGameDataService gameData, int skillId, SkillMoral moral, MagicWarpType warpType, short radius = 0,
        short range = 10000)
    {
        gameData.GetMagic(skillId).Returns(new MagicData
        {
            Id = skillId,
            Type1 = 8,
            Moral = (byte)moral,
            Range = range
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
        TakeTheClassOf(caster, skillId);
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
