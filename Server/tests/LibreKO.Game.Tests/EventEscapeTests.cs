using FluentAssertions;
using LibreKO.Common.Domain.Entities.GameData;
using LibreKO.Common.Enums;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.Protocol;
using LibreKO.Game.Scripting;
using LibreKO.Game.World;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace LibreKO.Game.Tests;

public class EventEscapeTests : GameTestBase
{
    private const byte TempleEventDisband = 9;
    private const byte SnowWar = (byte)ZoneId.SnowBattle;
    private const int Coliseum = (int)ZoneId.CaitharosArena;
    private const short PlayerHp = 1000;
    private const int StunRow = 110560;

    [Theory]
    [InlineData(false, false, true)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    public async Task LeavingAnEventTakesAPlayerOutOnlyWhenFreeAndOutOfAFight(bool fighting, bool stunned, bool leaves)
    {
        using var provider = CreateProvider(_ => { });
        var player = Player(provider.GetRequiredService<SessionManager>(), 1, SnowWar);
        if (fighting)
            player.MarkCombat();
        if (stunned)
            player.SetBuff(StunRow, new ActiveBuff
            {
                MagicId = StunRow, CasterId = 2, BuffType = BuffType.Stun, Duration = 2, ExpireTicks = DateTime.UtcNow.AddSeconds(2).Ticks,
            });
        var leave = new Packet(GameOpcodes.GS_EVENT);
        leave.WriteByte(TempleEventDisband);
        leave.ResetOffset();

        await provider.GetRequiredService<IEventSystemsPacketCoordinator>().HandleEventAsync(player.Client, leave);

        (player.ZoneId == (byte)ZoneId.Moradon).Should().Be(leaves, "the leave button is not a way out of a fight");
        var replies = Sent(player);
        replies.Any(packet => packet.GetOpcode() == (byte)GameOpcodes.GS_EVENT && packet.ReadByte() == TempleEventDisband)
            .Should().Be(leaves, "the client takes any leave reply to mean the player left");
        replies.Any(packet => packet.GetOpcode() == (byte)GameOpcodes.GS_CHAT).Should().Be(!leaves);
    }

    public enum Tie { InPrison, Trading, Stunned, Fighting }

    [Theory]
    [InlineData(Tie.InPrison)]
    [InlineData(Tie.Trading)]
    [InlineData(Tie.Stunned)]
    [InlineData(Tie.Fighting)]
    public async Task AScriptedPartyMoveLeavesAMemberWhoIsTiedUp(Tie tie)
    {
        var sessions = new SessionManager();
        var leader = Player(sessions, 1, (byte)ZoneId.Moradon);
        var member = Player(sessions, 2, tie == Tie.InPrison ? (byte)ZoneId.Prison : (byte)ZoneId.Moradon);
        TieUp(member, tie, leader);
        var party = sessions.Parties.CreateParty((short)leader.CharacterId);
        party.MemberIds[1] = (short)member.CharacterId;
        leader.PartyIndex = member.PartyIndex = party.Index;
        var gameData = Substitute.For<Common.Domain.Services.IGameDataService>();
        gameData.ZoneInfoTable.Returns(new Dictionary<short, ZoneInfoData>
        {
            [(short)Coliseum] = new() { ZoneNo = (short)Coliseum, InitX = 1390, InitZ = 1390 },
        });
        var transitions = Substitute.For<IZoneTransitionService>();
        var provider = new ServiceCollection()
            .AddSingleton(sessions)
            .AddSingleton(transitions)
            .AddSingleton(Substitute.For<IWorldPacketCoordinator>())
            .BuildServiceProvider();
        var context = new QuestScriptContext(leader, null, gameData, sessions, Substitute.For<ILogger>(), 1);
        context.RequestPartyZoneChange(Coliseum, 150, 150);

        await new ScriptEffectApplier(gameData, Substitute.For<ICharacterStatePersister>(), provider,
            Substitute.For<ILogger<ScriptEffectApplier>>()).ApplyAsync(leader, context, "krowaz.quest");

        await transitions.Received(1).ChangeZoneAsync(leader, (byte)Coliseum, Arg.Any<float>(), Arg.Any<float>());
        await transitions.DidNotReceive().ChangeZoneAsync(member, Arg.Any<byte>(), Arg.Any<float>(), Arg.Any<float>());
    }

    [Theory]
    [InlineData(Tie.InPrison)]
    [InlineData(Tie.Trading)]
    [InlineData(Tie.Fighting)]
    public async Task AClanMatchLeavesAMemberWhoIsTiedUp(Tie tie)
    {
        const short clan = 40;
        var sessions = new SessionManager();
        sessions.Knights.AddClan(clan, new Common.Domain.Entities.KnightsEntity { Id = clan, Name = "Wolves" });
        sessions.Knights.AddClan(clan + 1, new Common.Domain.Entities.KnightsEntity { Id = clan + 1, Name = "Lions" });
        var free = Player(sessions, 1, (byte)ZoneId.Moradon);
        var tied = Player(sessions, 2, tie == Tie.InPrison ? (byte)ZoneId.Prison : (byte)ZoneId.Moradon);
        free.KnightsId = tied.KnightsId = clan;
        TieUp(tied, tie, free);
        var transitions = Substitute.For<IZoneTransitionService>();

        await new ClanMatchService(sessions, transitions, Microsoft.Extensions.Logging.Abstractions.NullLogger<ClanMatchService>.Instance)
            .StartAsync("Wolves", "Lions");

        await transitions.Received(1).ChangeZoneAsync(free, ClanMatchService.Zone, Arg.Any<float>(), Arg.Any<float>());
        await transitions.DidNotReceive().ChangeZoneAsync(tied, Arg.Any<byte>(), Arg.Any<float>(), Arg.Any<float>());
    }

    [Theory]
    [InlineData(Tie.Trading)]
    [InlineData(Tie.Fighting)]
    public async Task AFullMoonPartyWithAMemberWhoIsTiedUpDoesNotEnter(Tie tie)
    {
        var transitions = Substitute.For<IZoneTransitionService>();
        var items = Substitute.For<IMagicItemUsageService>();
        items.TryConsumeItemAsync(Arg.Any<UserSession>(), FullMoonRules.VoucherItem).Returns(true);
        using var provider = CreateProvider(_ => { }, configureServices: services =>
        {
            services.AddSingleton(transitions);
            services.AddSingleton(items);
        });
        var (leader, member) = FullMoonParty(provider.GetRequiredService<SessionManager>());
        TieUp(member, tie, leader);

        var entered = await provider.GetRequiredService<IFullMoonRiftService>().HandlePartyEntryAsync(leader);

        entered.Should().BeFalse();
        await transitions.DidNotReceiveWithAnyArgs().ChangeZoneAsync(default!, default, default, default);
        await items.DidNotReceiveWithAnyArgs().TryConsumeItemAsync(default!, default);
    }

    [Fact]
    public async Task AClanFighterTiedUpWhileOthersAreMovedStaysBehind()
    {
        const short clan = 40;
        var sessions = new SessionManager();
        sessions.Knights.AddClan(clan, new Common.Domain.Entities.KnightsEntity { Id = clan, Name = "Wolves" });
        sessions.Knights.AddClan(clan + 1, new Common.Domain.Entities.KnightsEntity { Id = clan + 1, Name = "Lions" });
        var fighters = new[] { Player(sessions, 1, (byte)ZoneId.Moradon), Player(sessions, 2, (byte)ZoneId.Moradon) };
        foreach (var fighter in fighters)
            fighter.KnightsId = clan;
        var transitions = Substitute.For<IZoneTransitionService>();
        transitions.When(t => t.ChangeZoneAsync(Arg.Any<UserSession>(), ClanMatchService.Zone, Arg.Any<float>(), Arg.Any<float>()))
            .Do(call => fighters.First(fighter => fighter != call.Arg<UserSession>()).Trade.ExchangeUser = call.Arg<UserSession>().CharacterId);

        await new ClanMatchService(sessions, transitions, Microsoft.Extensions.Logging.Abstractions.NullLogger<ClanMatchService>.Instance)
            .StartAsync("Wolves", "Lions");

        await transitions.Received(1).ChangeZoneAsync(Arg.Any<UserSession>(), ClanMatchService.Zone, Arg.Any<float>(), Arg.Any<float>());
    }

    [Fact]
    public async Task AFullMoonMemberTiedUpBeforePayingPaysNothingAndStaysBehind()
    {
        var transitions = Substitute.For<IZoneTransitionService>();
        var items = Substitute.For<IMagicItemUsageService>();
        using var provider = CreateProvider(_ => { }, configureServices: services =>
        {
            services.AddSingleton(transitions);
            services.AddSingleton(items);
        });
        var (leader, member) = FullMoonParty(provider.GetRequiredService<SessionManager>());
        items.TryConsumeItemAsync(Arg.Any<UserSession>(), FullMoonRules.VoucherItem).Returns(true);
        items.When(usage => usage.TryConsumeItemAsync(leader, FullMoonRules.VoucherItem)).Do(_ => TieUp(member, Tie.Trading, leader));

        await provider.GetRequiredService<IFullMoonRiftService>().HandlePartyEntryAsync(leader);

        await items.DidNotReceive().TryConsumeItemAsync(member, Arg.Any<int>());
        await transitions.DidNotReceive().ChangeZoneAsync(member, Arg.Any<byte>(), Arg.Any<float>(), Arg.Any<float>());
        await transitions.Received(1).ChangeZoneAsync(leader, (byte)ZoneId.DungeonDefence, Arg.Any<float>(), Arg.Any<float>());
    }

    [Fact]
    public async Task AFullMoonMemberTiedUpAfterPayingStaysBehindWithTheVoucherBack()
    {
        var transitions = Substitute.For<IZoneTransitionService>();
        var items = Substitute.For<IMagicItemUsageService>();
        var grants = Substitute.For<IItemGrantService>();
        using var provider = CreateProvider(_ => { },
            gameData => gameData.GetItem(FullMoonRules.VoucherItem).Returns(new ItemData { Num = FullMoonRules.VoucherItem }),
            configureServices: services =>
            {
                services.AddSingleton(transitions);
                services.AddSingleton(items);
                services.AddSingleton(grants);
            });
        var (leader, member) = FullMoonParty(provider.GetRequiredService<SessionManager>());
        items.TryConsumeItemAsync(Arg.Any<UserSession>(), FullMoonRules.VoucherItem).Returns(true);
        transitions.When(t => t.ChangeZoneAsync(leader, Arg.Any<byte>(), Arg.Any<float>(), Arg.Any<float>()))
            .Do(_ => TieUp(member, Tie.Trading, leader));

        await provider.GetRequiredService<IFullMoonRiftService>().HandlePartyEntryAsync(leader);

        await transitions.DidNotReceive().ChangeZoneAsync(member, Arg.Any<byte>(), Arg.Any<float>(), Arg.Any<float>());
        await grants.Received(1).GrantAsync(member, Arg.Is<ItemData>(item => item.Num == FullMoonRules.VoucherItem), 1);
        FullMoonRules.HasEnteredToday(member).Should().BeFalse("a member who did not go in used up no entry");
        FullMoonRules.HasEnteredToday(leader).Should().BeTrue();
    }

    [Fact]
    public async Task ADungeonLeavesATiedUpMemberBehindAndSaysSo()
    {
        var transitions = Substitute.For<IZoneTransitionService>();
        using var provider = CreateProvider(_ => { }, configureServices: services => services.AddSingleton(transitions));
        var (leader, member) = FullMoonParty(provider.GetRequiredService<SessionManager>());
        TieUp(member, Tie.Fighting, leader);

        await provider.GetRequiredService<IInstanceEntryService>().EnterAsync(leader, Coliseum, 1, 0f, 0f);

        await transitions.Received(1).ChangeZoneAsync(leader, Coliseum, Arg.Any<float>(), Arg.Any<float>());
        await transitions.DidNotReceive().ChangeZoneAsync(member, Arg.Any<byte>(), Arg.Any<float>(), Arg.Any<float>());
        Notices(member).Should().ContainSingle(notice => notice.Contains("stayed behind"));
        Notices(leader).Should().Contain(notice => notice.Contains(member.Name));
    }

    [Fact]
    public async Task ADungeonChecksEachMemberAgainJustBeforeTheMove()
    {
        var transitions = Substitute.For<IZoneTransitionService>();
        using var provider = CreateProvider(_ => { }, configureServices: services => services.AddSingleton(transitions));
        var (leader, member) = FullMoonParty(provider.GetRequiredService<SessionManager>());
        transitions.When(t => t.ChangeZoneAsync(leader, Arg.Any<byte>(), Arg.Any<float>(), Arg.Any<float>()))
            .Do(_ => TieUp(member, Tie.Trading, leader));

        await provider.GetRequiredService<IInstanceEntryService>().EnterAsync(leader, Coliseum, 1, 0f, 0f);

        await transitions.DidNotReceive().ChangeZoneAsync(member, Arg.Any<byte>(), Arg.Any<float>(), Arg.Any<float>());
        member.Room.Should().Be(0);
    }

    private static List<string> Notices(UserSession player) =>
        [.. Sent(player).Where(packet => packet.GetOpcode() == (byte)GameOpcodes.GS_CHAT).Select(packet =>
        {
            var bytes = packet.GetBytes();
            return System.Text.Encoding.ASCII.GetString(bytes);
        })];

    private static (UserSession Leader, UserSession Member) FullMoonParty(SessionManager sessions)
    {
        var leader = Player(sessions, 1, (byte)ZoneId.Moradon);
        var member = Player(sessions, 2, (byte)ZoneId.Moradon);
        var party = sessions.Parties.CreateParty((short)leader.CharacterId);
        party.BoardType = FullMoonRules.FullMoonBoard;
        party.TryJoin((short)member.CharacterId);
        leader.PartyIndex = member.PartyIndex = party.Index;
        foreach (var player in new[] { leader, member })
        {
            player.Level = FullMoonRules.MinEntryLevel;
            player.Inventory[InventoryConstants.InventoryStart].ItemId = FullMoonRules.VoucherItem;
            player.Inventory[InventoryConstants.InventoryStart].Count = 1;
        }

        return (leader, member);
    }

    [Fact]
    public async Task OnlyFullMoonMembersWhoseVoucherWasSpentGoIn()
    {
        var transitions = Substitute.For<IZoneTransitionService>();
        var items = Substitute.For<IMagicItemUsageService>();
        using var provider = CreateProvider(_ => { }, configureServices: services =>
        {
            services.AddSingleton(transitions);
            services.AddSingleton(items);
        });
        var (leader, member) = FullMoonParty(provider.GetRequiredService<SessionManager>());
        items.TryConsumeItemAsync(leader, FullMoonRules.VoucherItem).Returns(true);
        items.TryConsumeItemAsync(member, FullMoonRules.VoucherItem).Returns(false);

        await provider.GetRequiredService<IFullMoonRiftService>().HandlePartyEntryAsync(leader);

        await transitions.Received(1).ChangeZoneAsync(leader, (byte)ZoneId.DungeonDefence, Arg.Any<float>(), Arg.Any<float>());
        await transitions.DidNotReceive().ChangeZoneAsync(member, Arg.Any<byte>(), Arg.Any<float>(), Arg.Any<float>());
        FullMoonRules.HasEnteredToday(member).Should().BeFalse("a member who spent nothing used up no entry");
    }

    private static void TieUp(UserSession player, Tie tie, UserSession partner)
    {
        switch (tie)
        {
            case Tie.Trading:
                player.Trade.ExchangeUser = partner.CharacterId;
                break;
            case Tie.Stunned:
                player.SetBuff(StunRow, new ActiveBuff
                {
                    MagicId = StunRow, CasterId = partner.CharacterId, BuffType = BuffType.Stun, Duration = 2,
                    ExpireTicks = DateTime.UtcNow.AddSeconds(2).Ticks,
                });
                break;
            case Tie.Fighting:
                player.MarkCombat();
                break;
        }
    }

    private static List<Packet> Sent(UserSession player) =>
        player.Client.ReceivedCalls()
            .Where(call => call.GetMethodInfo().Name == nameof(IClient.SendPacket))
            .Select(call =>
            {
                var packet = (Packet)call.GetArguments()[0]!;
                packet.ResetOffset();
                return packet;
            })
            .ToList();

    private static UserSession Player(SessionManager sessions, int id, byte zone)
    {
        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        client.SendPacket(Arg.Any<Packet>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        var session = sessions.CreateSession(client, id, id);
        session.Name = $"Player{id}";
        session.ZoneId = zone;
        session.Hp = session.MaxHp = PlayerHp;
        return session;
    }
}
