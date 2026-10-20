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
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task AScriptedPartyMoveLeavesAPrisonerAndABusyMember(bool inPrison, bool trading)
    {
        var sessions = new SessionManager();
        var leader = Player(sessions, 1, (byte)ZoneId.Moradon);
        var member = Player(sessions, 2, inPrison ? (byte)ZoneId.Prison : (byte)ZoneId.Moradon);
        if (trading)
            member.Trade.ExchangeUser = leader.CharacterId;
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
