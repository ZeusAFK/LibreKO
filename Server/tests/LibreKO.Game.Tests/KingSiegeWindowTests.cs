using System.Text;
using FluentAssertions;
using LibreKO.Common.Domain.Entities;
using LibreKO.Common.Domain.Entities.GameData;
using LibreKO.Common.Domain.Services;
using LibreKO.Common.Enums;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Common.Infrastructure.Persistence;
using LibreKO.Game.Protocol;
using LibreKO.Game.World;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace LibreKO.Game.Tests;

public class KingSiegeWindowTests : GameTestBase
{
    private const byte Election = 1;
    private const byte Impeachment = 2;
    private const byte Tax = 3;
    private const byte ElectionOfficerWindow = 5;
    private const byte NationIntro = 6;
    private const byte WarfareNpc = 3;
    private const byte CastleManager = 4;
    private const short Accepted = 1;
    private const int KingsSceptor = 910074000;
    private const int ImpeachmentCost = 30_000_000;
    private const int CoinMax = 2_100_000_000;
    private const byte Delos = 30;
    private const short MoradonZone = 21;
    private const short DelosZone = 30;
    private const short CastleOwner = 300;
    private const short RulingClan = 400;
    private const short ElectionOfficerNpc = 14404;
    private const short GrandChamberlainNpc = 14405;
    private const short SiegeWarfareNpc = 521;
    private const short CastleManagerNpc = 522;
    private const byte NoTerm = 0;
    private const byte Nomination = 1;
    private const byte Voting = 3;
    private const byte TermStarted = 6;
    private const byte SenatorImpeachmentVote = 1;
    private const byte PublicImpeachmentVote = 3;
    private const byte CandidateList = 4;

    private static int _nextCharacterId = 7000;

    private sealed record Player(UserSession Session, IClient Client, List<Packet> Sent)
    {
        public Packet Only(GameOpcodes opcode)
        {
            var packet = Sent.Should().ContainSingle(sent => sent.GetOpcode() == (byte)opcode).Subject;
            packet.ResetOffset();
            return packet;
        }

        public bool Received(GameOpcodes opcode) => Sent.Any(sent => sent.GetOpcode() == (byte)opcode);
    }

    private static KingSystemData KarusKing(string kingName = "Ruler", byte type = TermStarted) => new()
    {
        Nation = (byte)AccountNation.Karus,
        Type = type,
        KingName = kingName,
        Year = 2099,
        Month = 6,
        Day = 15,
        Hour = 20,
        Minute = 30,
    };

    private static ServiceProvider CreateWorld(
        KingSystemData? karusKing = null,
        SiegeWarfareData? siege = null,
        Action<AppDbContext>? seed = null,
        Action<IGameDataService>? configureGameData = null)
    {
        var kings = new Dictionary<byte, KingSystemData>();
        if (karusKing != null)
            kings[(byte)AccountNation.Karus] = karusKing;

        return CreateProvider(
            db =>
            {
                if (karusKing != null)
                    db.Add(karusKing);
                seed?.Invoke(db);
            },
            gameData =>
            {
                gameData.KingSystemTable.Returns(kings);
                gameData.SiegeWarfare.Returns(siege);
                configureGameData?.Invoke(gameData);
            });
    }

    private static Player Join(
        ServiceProvider provider, string name, AccountNation nation = AccountNation.Karus, byte level = 60)
    {
        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        var sent = new List<Packet>();
        client.SendPacket(Arg.Do<Packet>(sent.Add), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);

        var characterId = Interlocked.Increment(ref _nextCharacterId);
        var session = provider.GetRequiredService<SessionManager>().CreateSession(client, characterId, characterId);
        session.Name = name;
        session.Nation = nation;
        session.Level = level;
        session.Hp = 100;
        session.ZoneId = Delos;
        session.X = 510;
        session.Z = 782;
        return new Player(session, client, sent);
    }

    private static void LeadClan(ServiceProvider provider, Player player, short clanId, string clanName = "Wardens")
    {
        player.Session.KnightsId = clanId;
        player.Session.KnightsFame = ClanRules.FameChief;
        AddClan(provider, clanId, clanName, player.Session.Name, (byte)player.Session.Nation);
    }

    private static void AddClan(ServiceProvider provider, short clanId, string name, string chief, byte nation, short members = 12) =>
        provider.GetRequiredService<SessionManager>().Knights.AddClan(clanId, new KnightsEntity
        {
            Id = clanId, Name = name, Chief = chief, Nation = nation, Members = members,
        });

    private static Packet Request(GameOpcodes opcode, params byte[] body)
    {
        var packet = new Packet(opcode);
        packet.WriteBytes(body);
        return packet;
    }

    private static Task SendKing(ServiceProvider provider, Player player, Packet packet) =>
        provider.GetRequiredService<INationSystemsPacketCoordinator>().HandleKingAsync(player.Client, packet);

    private static Task SendSiege(ServiceProvider provider, Player player, Packet packet) =>
        provider.GetRequiredService<INationSystemsPacketCoordinator>().HandleSiegeAsync(player.Client, packet);

    private static async Task ClickNpc(ServiceProvider provider, Player player, short npcId, byte npcType)
    {
        provider.GetRequiredService<IGameDataService>().GetNpc(npcId, Arg.Any<bool>())
            .Returns(new NpcData { Id = npcId, Name = "npc", NpcType = npcType, IsMonster = false });

        var sessions = provider.GetRequiredService<SessionManager>();
        sessions.Regions.AddToRegion(player.Session);
        var npc = sessions.Regions.SpawnNpc(new NpcInstance
        {
            NpcId = npcId, Name = "npc", NpcType = npcType, ZoneId = Delos,
            X = 510, Z = 782, SpawnX = 510, SpawnZ = 782, Hp = 100, MaxHp = 100,
        });

        var click = new Packet(GameOpcodes.GS_NPC_EVENT);
        click.WriteByte(1);
        click.WriteInt(npc.UniqueId);
        await provider.GetRequiredService<IQuestNpcInteractionService>().HandleNpcEventAsync(player.Client, click);
    }

    private static void ExpectResult(Packet packet, byte sub, byte subType, short result)
    {
        packet.ReadByte().Should().Be(sub);
        packet.ReadByte().Should().Be(subType);
        packet.ReadShort().Should().Be(result);
    }

    private static SiegeWarfareData Castle(short owner = CastleOwner) => new()
    {
        CastleIndex = 1,
        MasterKnights = owner,
        SiegeType = 2,
        WarDay = 7,
        WarTime = 20,
        WarMinute = 30,
        WarRequestDay = 2,
        WarRequestTime = 10,
        WarRequestMinute = 15,
        MoradonTariff = 10,
        DellosTariff = 12,
        DungeonCharge = 5_000,
        MoradonTax = 7_000,
        DungeonEntranceFee = 2_500,
    };

    [Fact]
    public async Task TheElectionOfficerOpensWithTheNationsKingName()
    {
        using var provider = CreateWorld(KarusKing("Ruler"));
        var player = Join(provider, "Citizen");

        await ClickNpc(provider, player, ElectionOfficerNpc, NpcData.TypeElectionOfficer);

        var reply = player.Only(GameOpcodes.GS_KING);
        reply.ReadByte().Should().Be(ElectionOfficerWindow);
        reply.ReadSByteString().Should().Be("Ruler");
        reply.RemainingBytes.Should().Be(0);
    }

    [Fact]
    public async Task TheElectionOfficerSendsAnEmptyNameWhenTheNationHasNoKing()
    {
        using var provider = CreateWorld(KarusKing(string.Empty));
        var player = Join(provider, "Citizen");

        await ClickNpc(provider, player, ElectionOfficerNpc, NpcData.TypeElectionOfficer);

        var reply = player.Only(GameOpcodes.GS_KING);
        reply.ReadByte().Should().Be(ElectionOfficerWindow);
        reply.ReadSByteString().Should().BeEmpty();
        reply.RemainingBytes.Should().Be(0);
    }

    [Fact]
    public async Task TheGrandChamberlainShowsTheKingHisFundAndTheTreasury()
    {
        var king = KarusKing();
        king.Tribute = 1_000;
        king.TerritoryTax = 500;
        king.NationalTreasury = 90_000;
        using var provider = CreateWorld(king);
        var player = Join(provider, "Ruler");

        await ClickNpc(provider, player, GrandChamberlainNpc, NpcData.TypeGrandChamberlain);

        var reply = player.Only(GameOpcodes.GS_KING);
        ExpectResult(reply, Tax, 1, 1);
        reply.ReadUInt().Should().Be(1_500);
        reply.ReadUInt().Should().Be(90_000);
        reply.RemainingBytes.Should().Be(0);
    }

    [Fact]
    public async Task TheGrandChamberlainShowsACitizenOnlyTheTreasury()
    {
        var king = KarusKing();
        king.Tribute = 1_000;
        king.NationalTreasury = 90_000;
        using var provider = CreateWorld(king);
        var player = Join(provider, "Citizen");

        await ClickNpc(provider, player, GrandChamberlainNpc, NpcData.TypeGrandChamberlain);

        var reply = player.Only(GameOpcodes.GS_KING);
        ExpectResult(reply, Tax, 1, 2);
        reply.ReadUInt().Should().Be(90_000);
        reply.RemainingBytes.Should().Be(0);
    }

    [Fact]
    public async Task TheSiegeWarfareNpcOpensTheCastleGuardWindowWithoutPayload()
    {
        using var provider = CreateWorld(siege: Castle());
        var player = Join(provider, "Visitor");

        await ClickNpc(provider, player, SiegeWarfareNpc, NpcData.TypeSiegeWarfare);

        var reply = player.Only(GameOpcodes.GS_SIEGE);
        reply.ReadByte().Should().Be(WarfareNpc);
        reply.ReadByte().Should().Be(7);
        reply.RemainingBytes.Should().Be(0);
    }

    [Fact]
    public async Task TheCastleManagerOpensForTheLeaderOfTheCastleOwningClan()
    {
        using var provider = CreateWorld(siege: Castle());
        var lord = Join(provider, "Lord");
        LeadClan(provider, lord, CastleOwner);

        await ClickNpc(provider, lord, CastleManagerNpc, NpcData.TypeCastleManager);

        var reply = lord.Only(GameOpcodes.GS_SIEGE);
        reply.ReadByte().Should().Be(CastleManager);
        reply.ReadByte().Should().Be(1);
        reply.ReadUInt().Should().Be(5_000);
        reply.ReadUInt().Should().Be(7_000);
        reply.RemainingBytes.Should().Be(0);
    }

    [Fact]
    public async Task TheCastleManagerOpensNothingForAMemberOfTheOwningClan()
    {
        using var provider = CreateWorld(siege: Castle());
        var member = Join(provider, "Member");
        member.Session.KnightsId = CastleOwner;
        member.Session.KnightsFame = ClanRules.FameOfficer;

        await ClickNpc(provider, member, CastleManagerNpc, NpcData.TypeCastleManager);

        member.Received(GameOpcodes.GS_SIEGE).Should().BeFalse();
    }

    [Fact]
    public async Task TheClientCannotOpenTheElectionOfficerWindowByItself()
    {
        using var provider = CreateWorld(KarusKing());
        var player = Join(provider, "Citizen");

        await SendKing(provider, player, Request(GameOpcodes.GS_KING, ElectionOfficerWindow));

        player.Sent.Should().BeEmpty();
    }

    [Fact]
    public async Task TheElectionDayIsAKingElectionScheduleWithMonthFirst()
    {
        using var provider = CreateWorld(KarusKing());
        var player = Join(provider, "Citizen");

        await SendKing(provider, player, Request(GameOpcodes.GS_KING, Election, 1));

        var reply = player.Only(GameOpcodes.GS_KING);
        reply.ReadByte().Should().Be(Election);
        reply.ReadByte().Should().Be(1);
        reply.ReadByte().Should().Be(1);
        reply.ReadByte().Should().Be(6);
        reply.ReadByte().Should().Be(15);
        reply.ReadByte().Should().Be(20);
        reply.ReadByte().Should().Be(30);
        reply.RemainingBytes.Should().Be(0);
    }

    [Fact]
    public async Task WithoutAnElectionDateTheScheduleCarriesTheTrailingByte()
    {
        var king = KarusKing();
        king.Month = 0;
        king.Day = 0;
        using var provider = CreateWorld(king);
        var player = Join(provider, "Citizen");

        await SendKing(provider, player, Request(GameOpcodes.GS_KING, Election, 1));

        player.Only(GameOpcodes.GS_KING).GetData().Should().Equal(Election, 1, 0, 0);
    }

    [Fact]
    public async Task DuringTheSenatorsImpeachmentVoteTheScheduleSaysSo()
    {
        var king = KarusKing();
        king.ImType = SenatorImpeachmentVote;
        using var provider = CreateWorld(king);
        var player = Join(provider, "Citizen");

        await SendKing(provider, player, Request(GameOpcodes.GS_KING, Election, 1));

        player.Only(GameOpcodes.GS_KING).GetData().Should().Equal(Election, 1, 0, 2);
    }

    [Fact]
    public async Task DuringThePublicImpeachmentVoteTheScheduleCarriesItsDate()
    {
        var king = KarusKing();
        king.ImType = PublicImpeachmentVote;
        king.ImMonth = 3;
        king.ImDay = 9;
        king.ImHour = 18;
        king.ImMinute = 5;
        using var provider = CreateWorld(king);
        var player = Join(provider, "Citizen");

        await SendKing(provider, player, Request(GameOpcodes.GS_KING, Election, 1));

        player.Only(GameOpcodes.GS_KING).GetData().Should().Equal(Election, 1, 3, 3, 9, 18, 5);
    }

    private static Packet Nominate(string name)
    {
        var packet = Request(GameOpcodes.GS_KING, Election, 2);
        packet.WriteSByteString(name);
        return packet;
    }

    private static async Task<short> NominateAs(ServiceProvider provider, Player senator, string nominee)
    {
        senator.Sent.Clear();
        await SendKing(provider, senator, Nominate(nominee));
        var reply = senator.Only(GameOpcodes.GS_KING);
        reply.ReadByte().Should().Be(Election);
        reply.ReadByte().Should().Be(2);
        return reply.ReadShort();
    }

    [Fact]
    public async Task NominatingANameNobodyHasAnswersIdDoesNotExist()
    {
        using var provider = CreateWorld(KarusKing(type: Nomination));
        var senator = Join(provider, "Senator");
        LeadClan(provider, senator, 501);

        (await NominateAs(provider, senator, "Nobody")).Should().Be(-1);
    }

    [Fact]
    public async Task NominatingAnotherNationsClanLeaderAnswersSameNationOnly()
    {
        using var provider = CreateWorld(KarusKing(type: Nomination));
        var senator = Join(provider, "Senator");
        LeadClan(provider, senator, 501);
        var foreigner = Join(provider, "Foreigner", AccountNation.ElMorad);
        LeadClan(provider, foreigner, 502);

        (await NominateAs(provider, senator, "Foreigner")).Should().Be(-4);
    }

    [Fact]
    public async Task NominatingTheSameLeaderTwiceAnswersAlreadyNominated()
    {
        using var provider = CreateWorld(KarusKing(type: Nomination));
        var senator = Join(provider, "Senator");
        LeadClan(provider, senator, 501);
        AddClan(provider, 503, "Riders", "Hopeful", (byte)AccountNation.Karus);

        (await NominateAs(provider, senator, "Hopeful")).Should().Be(Accepted);
        (await NominateAs(provider, senator, "Hopeful")).Should().Be(-5);
    }

    [Fact]
    public async Task AnOfflineClanLeaderCanBeNominated()
    {
        using var provider = CreateWorld(KarusKing(type: Nomination));
        var senator = Join(provider, "Senator");
        LeadClan(provider, senator, 501);
        AddClan(provider, 503, "Riders", "Hopeful", (byte)AccountNation.Karus);

        (await NominateAs(provider, senator, "Hopeful")).Should().Be(Accepted);

        using var scope = provider.CreateScope();
        var entry = await scope.ServiceProvider.GetRequiredService<AppDbContext>().KingElectionList.SingleAsync();
        entry.Name.Should().Be("Hopeful");
        entry.Knights.Should().Be(503);
    }

    [Fact]
    public async Task AnEleventhNomineeIsBeyondTheCandidateRange()
    {
        using var provider = CreateWorld(KarusKing(type: Nomination), seed: db =>
        {
            for (var i = 0; i < 10; i++)
                db.KingElectionList.Add(new KingElectionList
                {
                    Nation = (byte)AccountNation.Karus, Type = CandidateList, Name = $"Candidate{i}", Knights = (short)(600 + i),
                });
        });
        var senator = Join(provider, "Senator");
        LeadClan(provider, senator, 501);
        AddClan(provider, 503, "Riders", "Hopeful", (byte)AccountNation.Karus);

        (await NominateAs(provider, senator, "Hopeful")).Should().Be(-7);
    }

    [Fact]
    public async Task NominatingOutsideTheNominationPeriodIsRefused()
    {
        using var provider = CreateWorld(KarusKing(type: NoTerm));
        var senator = Join(provider, "Senator");
        LeadClan(provider, senator, 501);

        (await NominateAs(provider, senator, "Senator")).Should().Be(-2);
    }

    [Fact]
    public async Task OnlyAClanLeaderMayNominate()
    {
        using var provider = CreateWorld(KarusKing(type: Nomination));
        var member = Join(provider, "Member");
        member.Session.KnightsId = 501;
        member.Session.KnightsFame = ClanRules.FameOfficer;

        (await NominateAs(provider, member, "Member")).Should().Be(-3);
    }

    private static Packet WritePlan(byte[] plan)
    {
        var packet = Request(GameOpcodes.GS_KING, Election, 3, 1);
        packet.WriteUShort((ushort)plan.Length);
        packet.WriteBytes(plan);
        return packet;
    }

    private static Action<AppDbContext> Candidates(params string[] names) => db =>
    {
        foreach (var name in names)
            db.KingElectionList.Add(new KingElectionList
            {
                Nation = (byte)AccountNation.Karus, Type = CandidateList, Name = name, Knights = RulingClan,
            });
    };

    [Fact]
    public async Task ANomineesPlanIsReadWithAShortLengthPrefix()
    {
        using var provider = CreateWorld(KarusKing(type: Nomination), seed: Candidates("Hopeful"));
        var candidate = Join(provider, "Hopeful");
        var plan = Encoding.ASCII.GetBytes("Lower taxes for everyone");

        await SendKing(provider, candidate, WritePlan(plan));

        candidate.Only(GameOpcodes.GS_KING).GetData().Should().Equal(Election, 3, 1, 1, 0);
        using var scope = provider.CreateScope();
        var stored = await scope.ServiceProvider.GetRequiredService<AppDbContext>().KingCandidacyNoticeBoard.SingleAsync();
        stored.Notice.Should().Equal(plan);
    }

    [Fact]
    public async Task APlanLongerThanFiveHundredBytesIsTooLong()
    {
        using var provider = CreateWorld(KarusKing(type: Nomination), seed: Candidates("Hopeful"));
        var candidate = Join(provider, "Hopeful");

        await SendKing(provider, candidate, WritePlan(Enumerable.Repeat((byte)'a', 501).ToArray()));

        ExpectPlanWriteResult(candidate, -2);
    }

    [Fact]
    public async Task APlanOfExactlyFiveHundredBytesIsPosted()
    {
        using var provider = CreateWorld(KarusKing(type: Nomination), seed: Candidates("Hopeful"));
        var candidate = Join(provider, "Hopeful");

        await SendKing(provider, candidate, WritePlan(Enumerable.Repeat((byte)'a', 500).ToArray()));

        ExpectPlanWriteResult(candidate, Accepted);
    }

    [Fact]
    public async Task OnlyANomineeMayPostAPlan()
    {
        using var provider = CreateWorld(KarusKing(type: Nomination));
        var player = Join(provider, "Citizen");

        await SendKing(provider, player, WritePlan(Encoding.ASCII.GetBytes("Vote for me")));

        ExpectPlanWriteResult(player, -3);
    }

    private static void ExpectPlanWriteResult(Player player, short result)
    {
        var reply = player.Only(GameOpcodes.GS_KING);
        reply.ReadByte().Should().Be(Election);
        reply.ReadByte().Should().Be(3);
        reply.ReadByte().Should().Be(1);
        reply.ReadShort().Should().Be(result);
        reply.RemainingBytes.Should().Be(0);
    }

    private static Packet ReadPlan(string name)
    {
        var packet = Request(GameOpcodes.GS_KING, Election, 3, 2, 2);
        packet.WriteSByteString(name);
        return packet;
    }

    [Fact]
    public async Task APledgeIsAnsweredWithAResultBeforeItsText()
    {
        var plan = Encoding.ASCII.GetBytes("Peace and bread");
        using var provider = CreateWorld(KarusKing(type: Voting), seed: db =>
        {
            Candidates("Hopeful")(db);
            db.KingCandidacyNoticeBoard.Add(new KingCandidacyNoticeBoard
            {
                UserId = "Hopeful", Nation = (byte)AccountNation.Karus, NoticeLen = (short)plan.Length, Notice = plan,
            });
        });
        var voter = Join(provider, "Voter");

        await SendKing(provider, voter, ReadPlan("Hopeful"));

        var reply = voter.Only(GameOpcodes.GS_KING);
        reply.ReadByte().Should().Be(Election);
        reply.ReadByte().Should().Be(3);
        reply.ReadByte().Should().Be(2);
        reply.ReadByte().Should().Be(2);
        reply.ReadShort().Should().Be(Accepted);
        reply.ReadUShort().Should().Be((ushort)plan.Length);
        reply.ReadBytes(plan.Length).Should().Equal(plan);
        reply.RemainingBytes.Should().Be(0);
    }

    [Fact]
    public async Task ANomineeWithoutAPlanAnswersNoPlansPosted()
    {
        using var provider = CreateWorld(KarusKing(type: Voting), seed: Candidates("Hopeful"));
        var voter = Join(provider, "Voter");

        await SendKing(provider, voter, ReadPlan("Hopeful"));

        voter.Only(GameOpcodes.GS_KING).GetData().Should().Equal(Election, 3, 2, 2, 0xFE, 0xFF);
    }

    [Fact]
    public async Task AskingForThePlanOfSomeoneWhoIsNotANomineeAnswersNotANominee()
    {
        using var provider = CreateWorld(KarusKing(type: Voting));
        var voter = Join(provider, "Voter");

        await SendKing(provider, voter, ReadPlan("Stranger"));

        voter.Only(GameOpcodes.GS_KING).GetData().Should().Equal(Election, 3, 2, 2, 0xFF, 0xFF);
    }

    [Fact]
    public async Task TheCandidateListNumbersEveryNomineeAndNamesTheirClan()
    {
        using var provider = CreateWorld(KarusKing(type: Voting), seed: Candidates("First", "Second"));
        AddClan(provider, RulingClan, "Crowns", "First", (byte)AccountNation.Karus);
        var voter = Join(provider, "Voter");

        await SendKing(provider, voter, Request(GameOpcodes.GS_KING, Election, 4, 1));

        var reply = voter.Only(GameOpcodes.GS_KING);
        reply.ReadByte().Should().Be(Election);
        reply.ReadByte().Should().Be(4);
        reply.ReadByte().Should().Be(1);
        reply.ReadShort().Should().Be(Accepted);
        reply.ReadByte().Should().Be(2);
        reply.ReadByte().Should().Be(1);
        reply.ReadSByteString().Should().Be("First");
        reply.ReadSByteString().Should().Be("Crowns");
        reply.ReadByte().Should().Be(2);
        reply.ReadSByteString().Should().Be("Second");
        reply.ReadSByteString().Should().Be("Crowns");
        reply.RemainingBytes.Should().Be(0);
    }

    [Fact]
    public async Task ViewingThePlansOpensTheCandidateList()
    {
        using var provider = CreateWorld(KarusKing(type: Nomination), seed: Candidates("First"));
        var voter = Join(provider, "Voter");

        await SendKing(provider, voter, Request(GameOpcodes.GS_KING, Election, 3, 2, 1));

        var reply = voter.Only(GameOpcodes.GS_KING);
        reply.ReadByte().Should().Be(Election);
        reply.ReadByte().Should().Be(4);
        reply.ReadByte().Should().Be(1);
        reply.ReadShort().Should().Be(Accepted);
        reply.ReadByte().Should().Be(1);
    }

    [Fact]
    public async Task TheCandidateListOutsideAnElectionIsNotTimeForVoting()
    {
        using var provider = CreateWorld(KarusKing(type: TermStarted), seed: Candidates("First"));
        var voter = Join(provider, "Voter");

        await SendKing(provider, voter, Request(GameOpcodes.GS_KING, Election, 4, 1));

        voter.Only(GameOpcodes.GS_KING).GetData().Should().Equal(Election, 4, 1, 0xFF, 0xFF);
    }

    [Theory]
    [InlineData(49, -4)]
    [InlineData(50, 1)]
    public async Task VotingForAKingNeedsLevelFifty(byte level, short expected)
    {
        using var provider = CreateWorld(KarusKing(type: Voting), seed: Candidates("Hopeful"));
        var voter = Join(provider, "Voter", level: level);
        var vote = Request(GameOpcodes.GS_KING, Election, 4, 2);
        vote.WriteSByteString("Hopeful");

        await SendKing(provider, voter, vote);

        var reply = voter.Only(GameOpcodes.GS_KING);
        reply.ReadByte().Should().Be(Election);
        reply.ReadByte().Should().Be(4);
        reply.ReadByte().Should().Be(2);
        reply.ReadShort().Should().Be(expected);
    }

    [Theory]
    [InlineData(NoTerm, true, -2)]
    [InlineData(Nomination, false, -3)]
    [InlineData(Nomination, true, 1)]
    public async Task TurningDownANominationAnswersWithTheClientsCodes(byte stage, bool nominated, short expected)
    {
        using var provider = CreateWorld(KarusKing(type: stage), seed: nominated ? Candidates("Hopeful") : null);
        var candidate = Join(provider, "Hopeful");

        await SendKing(provider, candidate, Request(GameOpcodes.GS_KING, Election, 5));

        ExpectResult(candidate.Only(GameOpcodes.GS_KING), Election, 5, expected);
    }

    private static Player Senator(ServiceProvider provider, int coins = 40_000_000)
    {
        var senator = Join(provider, "Senator");
        LeadClan(provider, senator, 501);
        senator.Session.Money = coins;
        return senator;
    }

    [Fact]
    public async Task AnAcceptableProposalChangesNothingWhileImpeachmentVotesAreNotRun()
    {
        var king = KarusKing();
        using var provider = CreateWorld(king);
        var senator = Senator(provider);

        await SendKing(provider, senator, Request(GameOpcodes.GS_KING, Impeachment, 1));

        senator.Session.Money.Should().Be(40_000_000);
        senator.Received(GameOpcodes.GS_GOLD_CHANGE).Should().BeFalse();
        king.ImType.Should().NotBe(SenatorImpeachmentVote);
    }

    [Fact]
    public async Task OnlyASenatorMayProposeAnImpeachment()
    {
        using var provider = CreateWorld(KarusKing());
        var player = Join(provider, "Citizen");
        player.Session.Money = 40_000_000;

        await SendKing(provider, player, Request(GameOpcodes.GS_KING, Impeachment, 1));

        ExpectResult(player.Only(GameOpcodes.GS_KING), Impeachment, 1, -1);
        player.Session.Money.Should().Be(40_000_000);
    }

    [Fact]
    public async Task ProposingAnImpeachmentWithoutThirtyMillionIsNotEnoughCoins()
    {
        using var provider = CreateWorld(KarusKing());
        var senator = Senator(provider, ImpeachmentCost - 1);

        await SendKing(provider, senator, Request(GameOpcodes.GS_KING, Impeachment, 1));

        ExpectResult(senator.Only(GameOpcodes.GS_KING), Impeachment, 1, -2);
    }

    [Fact]
    public async Task ThereIsNoImpeachmentWithoutAKing()
    {
        using var provider = CreateWorld(KarusKing(string.Empty));
        var senator = Senator(provider);

        await SendKing(provider, senator, Request(GameOpcodes.GS_KING, Impeachment, 1));

        ExpectResult(senator.Only(GameOpcodes.GS_KING), Impeachment, 1, -3);
    }

    [Fact]
    public async Task ASecondProposalWhileTheSenatorsVoteAnswersVoteInProcess()
    {
        var king = KarusKing();
        king.ImType = SenatorImpeachmentVote;
        using var provider = CreateWorld(king);
        var senator = Senator(provider);

        await SendKing(provider, senator, Request(GameOpcodes.GS_KING, Impeachment, 1));

        ExpectResult(senator.Only(GameOpcodes.GS_KING), Impeachment, 1, -4);
    }

    [Fact]
    public async Task NoImpeachmentInTheLastFiveDaysBeforeTheElection()
    {
        var king = KarusKing();
        var election = DateTime.Now.AddDays(2);
        king.Year = (short)election.Year;
        king.Month = (byte)election.Month;
        king.Day = (byte)election.Day;
        king.Hour = (byte)election.Hour;
        king.Minute = (byte)election.Minute;
        using var provider = CreateWorld(king);
        var senator = Senator(provider);

        await SendKing(provider, senator, Request(GameOpcodes.GS_KING, Impeachment, 1));

        ExpectResult(senator.Only(GameOpcodes.GS_KING), Impeachment, 1, -5);
        senator.Session.Money.Should().Be(40_000_000);
    }

    [Theory]
    [InlineData(0, true, -2)]
    [InlineData(SenatorImpeachmentVote, false, -1)]
    [InlineData(SenatorImpeachmentVote, true, 1)]
    public async Task TheSenatorsImpeachmentVoteAnswersWithTheClientsCodes(byte stage, bool isSenator, short expected)
    {
        var king = KarusKing();
        king.ImType = stage;
        using var provider = CreateWorld(king);
        var voter = isSenator ? Senator(provider) : Join(provider, "Citizen");

        await SendKing(provider, voter, Request(GameOpcodes.GS_KING, Impeachment, 2, 1));

        ExpectResult(voter.Only(GameOpcodes.GS_KING), Impeachment, 2, expected);
    }

    [Theory]
    [InlineData(0, 60, -1)]
    [InlineData(PublicImpeachmentVote, 49, -2)]
    [InlineData(PublicImpeachmentVote, 60, 1)]
    public async Task ThePublicImpeachmentVoteAnswersWithTheClientsCodes(byte stage, byte level, short expected)
    {
        var king = KarusKing();
        king.ImType = stage;
        using var provider = CreateWorld(king);
        var voter = Join(provider, "Voter", level: level);
        voter.Session.Loyalty = 10_000;

        await SendKing(provider, voter, Request(GameOpcodes.GS_KING, Impeachment, 4, 2));

        ExpectResult(voter.Only(GameOpcodes.GS_KING), Impeachment, 4, expected);
    }

    [Fact]
    public async Task TheSenatorsInFavourAreListedByName()
    {
        var king = KarusKing();
        king.ImType = SenatorImpeachmentVote;
        king.ImRequestId = "Accuser";
        using var provider = CreateWorld(king);
        var player = Join(provider, "Citizen");

        await SendKing(provider, player, Request(GameOpcodes.GS_KING, Impeachment, 3));

        var reply = player.Only(GameOpcodes.GS_KING);
        ExpectResult(reply, Impeachment, 3, Accepted);
        reply.ReadByte().Should().Be(1);
        reply.ReadSByteString().Should().Be("Accuser");
        reply.RemainingBytes.Should().Be(0);
    }

    [Fact]
    public async Task WithoutAnImpeachmentTheSenatorListIsNotTimeForVoting()
    {
        using var provider = CreateWorld(KarusKing());
        var player = Join(provider, "Citizen");

        await SendKing(provider, player, Request(GameOpcodes.GS_KING, Impeachment, 3));

        player.Only(GameOpcodes.GS_KING).GetData().Should().Equal(Impeachment, 3, 0xFF, 0xFF);
    }

    [Theory]
    [InlineData(SenatorImpeachmentVote, 8, 1)]
    [InlineData(SenatorImpeachmentVote, 9, -1)]
    [InlineData(PublicImpeachmentVote, 8, -1)]
    [InlineData(PublicImpeachmentVote, 9, 1)]
    public async Task EachImpeachmentVoteOpensOnlyDuringItsOwnStage(byte stage, byte request, short expected)
    {
        var king = KarusKing();
        king.ImType = stage;
        using var provider = CreateWorld(king);
        var player = Join(provider, "Citizen");

        await SendKing(provider, player, Request(GameOpcodes.GS_KING, Impeachment, request));

        ExpectResult(player.Only(GameOpcodes.GS_KING), Impeachment, request, expected);
    }

    [Fact]
    public async Task TheKingsFundPaysTributeAndTerritoryTaxAndReportsTheNewCoins()
    {
        var king = KarusKing();
        king.Tribute = 1_000;
        king.TerritoryTax = 500;
        using var provider = CreateWorld(king);
        var ruler = Join(provider, "Ruler");
        ruler.Session.Money = 100;

        await SendKing(provider, ruler, Request(GameOpcodes.GS_KING, Tax, 2));

        var reply = ruler.Only(GameOpcodes.GS_KING);
        ExpectResult(reply, Tax, 2, Accepted);
        reply.ReadUInt().Should().Be(1_600);
        reply.ReadUInt().Should().Be(1_500);
        reply.RemainingBytes.Should().Be(0);
        ruler.Session.Money.Should().Be(1_600);
        king.Tribute.Should().Be(0);
        king.TerritoryTax.Should().Be(0);
    }

    [Fact]
    public async Task TheKingsFundStopsAtTheCoinCapAndKeepsTheRest()
    {
        var king = KarusKing();
        king.Tribute = 1_000;
        king.TerritoryTax = 500;
        using var provider = CreateWorld(king);
        var ruler = Join(provider, "Ruler");
        ruler.Session.Money = CoinMax - 700;

        await SendKing(provider, ruler, Request(GameOpcodes.GS_KING, Tax, 2));

        var reply = ruler.Only(GameOpcodes.GS_KING);
        ExpectResult(reply, Tax, 2, Accepted);
        reply.ReadUInt().Should().Be((uint)CoinMax);
        reply.ReadUInt().Should().Be(700);
        king.TerritoryTax.Should().Be(0);
        king.Tribute.Should().Be(800);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public async Task TaxRequestsFromSomeoneOtherThanTheKingAreOnlyForKings(byte taxOpcode)
    {
        var king = KarusKing();
        king.Tribute = 1_000;
        using var provider = CreateWorld(king);
        var player = Join(provider, "Citizen");

        await SendKing(provider, player, Request(GameOpcodes.GS_KING, Tax, taxOpcode, 3));

        player.Only(GameOpcodes.GS_KING).GetData().Should().Equal(Tax, taxOpcode, 0xFF, 0xFF);
        king.Tribute.Should().Be(1_000);
    }

    [Fact]
    public async Task TheKingSeesTheCurrentTariff()
    {
        var king = KarusKing();
        king.TerritoryTariff = 3;
        using var provider = CreateWorld(king);
        var ruler = Join(provider, "Ruler");

        await SendKing(provider, ruler, Request(GameOpcodes.GS_KING, Tax, 3));

        ruler.Only(GameOpcodes.GS_KING).GetData().Should().Equal(Tax, 3, 1, 0, 3);
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(5, true)]
    [InlineData(6, false)]
    public async Task TheKingSetsATariffFromZeroToFive(byte tariff, bool accepted)
    {
        var king = KarusKing();
        king.TerritoryTariff = 2;
        using var provider = CreateWorld(king);
        var ruler = Join(provider, "Ruler");

        await SendKing(provider, ruler, Request(GameOpcodes.GS_KING, Tax, 4, tariff));

        var reply = ruler.Only(GameOpcodes.GS_KING).GetData();
        if (accepted)
        {
            reply.Should().Equal(Tax, 4, 1, 0, tariff);
            king.TerritoryTariff.Should().Be(tariff);
        }
        else
        {
            reply.Should().Equal(Tax, 4, 0xFF, 0xFF);
            king.TerritoryTariff.Should().Be(2);
        }
    }

    private static ServiceProvider SceptorWorld() => CreateWorld(KarusKing(), configureGameData: gameData =>
        gameData.GetItem(KingsSceptor).Returns(new ItemData { Num = KingsSceptor, Name = "King's Sceptor", Duration = 12000 }));

    [Fact]
    public async Task TheKingReceivesTheSceptorTheClientKnows()
    {
        using var provider = SceptorWorld();
        var ruler = Join(provider, "Ruler");

        await SendKing(provider, ruler, Request(GameOpcodes.GS_KING, Tax, 7));

        ExpectResult(ruler.Only(GameOpcodes.GS_KING), Tax, 7, Accepted);
        ruler.Session.Inventory.Should().Contain(slot => slot.ItemId == KingsSceptor);
    }

    [Fact]
    public async Task AKingWhoCarriesTheSceptorAlreadyHasIt()
    {
        using var provider = SceptorWorld();
        var ruler = Join(provider, "Ruler");
        ruler.Session.Inventory[InventoryConstants.InventoryStart + 3].ItemId = KingsSceptor;

        await SendKing(provider, ruler, Request(GameOpcodes.GS_KING, Tax, 7));

        ExpectResult(ruler.Only(GameOpcodes.GS_KING), Tax, 7, -1);
        ruler.Session.Inventory.Count(slot => slot.ItemId == KingsSceptor).Should().Be(1);
    }

    [Fact]
    public async Task AKingWithAFullBagHasNoSlotForTheSceptor()
    {
        using var provider = SceptorWorld();
        var ruler = Join(provider, "Ruler");
        for (var slot = InventoryConstants.InventoryStart; slot < InventoryConstants.InventoryStart + InventoryConstants.HaveMax; slot++)
        {
            ruler.Session.Inventory[slot].ItemId = 100_000_000;
            ruler.Session.Inventory[slot].Count = 1;
        }

        await SendKing(provider, ruler, Request(GameOpcodes.GS_KING, Tax, 7));

        ExpectResult(ruler.Only(GameOpcodes.GS_KING), Tax, 7, -2);
    }

    [Fact]
    public async Task OnlyTheKingIsGivenTheSceptor()
    {
        using var provider = SceptorWorld();
        var player = Join(provider, "Citizen");

        await SendKing(provider, player, Request(GameOpcodes.GS_KING, Tax, 7));

        player.Sent.Should().BeEmpty();
        player.Session.Inventory.Should().NotContain(slot => slot.ItemId == KingsSceptor);
    }

    private static Packet WriteIntro(string intro)
    {
        var packet = Request(GameOpcodes.GS_KING, NationIntro, 2);
        packet.WriteString(intro);
        return packet;
    }

    [Fact]
    public async Task TheKingsNationIntroductionIsStoredAndReadBack()
    {
        var king = KarusKing();
        using var provider = CreateWorld(king);
        var ruler = Join(provider, "Ruler");
        var citizen = Join(provider, "Citizen");

        await SendKing(provider, citizen, Request(GameOpcodes.GS_KING, NationIntro, 1));
        citizen.Only(GameOpcodes.GS_KING).GetData().Should().Equal(NationIntro, 1, 0, 0);
        citizen.Sent.Clear();

        await SendKing(provider, ruler, WriteIntro("Glory to Karus"));
        ruler.Only(GameOpcodes.GS_KING).GetData().Should().Equal(NationIntro, 2, 1);

        await SendKing(provider, citizen, Request(GameOpcodes.GS_KING, NationIntro, 1));
        var reply = citizen.Only(GameOpcodes.GS_KING);
        reply.ReadByte().Should().Be(NationIntro);
        reply.ReadByte().Should().Be(1);
        reply.ReadString().Should().Be("Glory to Karus");
        reply.RemainingBytes.Should().Be(0);

        using var scope = provider.CreateScope();
        (await scope.ServiceProvider.GetRequiredService<AppDbContext>().KingSystem.SingleAsync())
            .IntroMessage.Should().Be("Glory to Karus");
    }

    [Fact]
    public async Task OnlyTheKingMayWriteTheNationIntroduction()
    {
        var king = KarusKing();
        using var provider = CreateWorld(king);
        var citizen = Join(provider, "Citizen");

        await SendKing(provider, citizen, WriteIntro("Usurped"));

        citizen.Only(GameOpcodes.GS_KING).GetData().Should().Equal(NationIntro, 2, 0);
        king.IntroMessage.Should().BeEmpty();
    }

    [Theory]
    [InlineData(200, 1)]
    [InlineData(201, 0)]
    public async Task TheNationIntroductionHoldsAtMostTwoHundredCharacters(int length, byte expected)
    {
        using var provider = CreateWorld(KarusKing());
        var ruler = Join(provider, "Ruler");

        await SendKing(provider, ruler, WriteIntro(new string('k', length)));

        ruler.Only(GameOpcodes.GS_KING).GetData().Should().Equal(NationIntro, 2, expected);
    }

    [Fact]
    public async Task TheCastleGuardScheduleListsTheWarAsTypeWeekdayHourMinute()
    {
        using var provider = CreateWorld(siege: Castle());
        var player = Join(provider, "Visitor");

        await SendSiege(provider, player, Request(GameOpcodes.GS_SIEGE, WarfareNpc, 2));

        player.Only(GameOpcodes.GS_SIEGE).GetData().Should().Equal(WarfareNpc, 2, 1, 0, 1, 2, 7, 20, 30);
    }

    [Fact]
    public async Task ACastleWithoutAWarDayHasAnEmptySchedule()
    {
        var castle = Castle();
        castle.WarDay = 0;
        using var provider = CreateWorld(siege: castle);
        var player = Join(provider, "Visitor");

        await SendSiege(provider, player, Request(GameOpcodes.GS_SIEGE, WarfareNpc, 2));

        player.Only(GameOpcodes.GS_SIEGE).GetData().Should().Equal(WarfareNpc, 2, 1, 0, 0);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task SigningUpForTheSiegeIsNotPossibleRightNow(byte action)
    {
        using var provider = CreateWorld(siege: Castle());
        var player = Join(provider, "Visitor");

        await SendSiege(provider, player, Request(GameOpcodes.GS_SIEGE, WarfareNpc, 1, action));

        player.Only(GameOpcodes.GS_SIEGE).GetData().Should().Equal(WarfareNpc, 1, 0xFE, 0xFF);
    }

    [Fact]
    public async Task AssaultIsLoggedAndNotAnswered()
    {
        using var provider = CreateWorld(siege: Castle());
        var player = Join(provider, "Visitor");

        await SendSiege(provider, player, Request(GameOpcodes.GS_SIEGE, WarfareNpc, 3));

        player.Sent.Should().BeEmpty();
    }

    [Fact]
    public async Task TheChallengerListUsesByteLengthNamesAndEndsWithTheRegistrationPeriod()
    {
        var castle = Castle();
        castle.RequestList1 = 701;
        using var provider = CreateWorld(siege: castle);
        AddClan(provider, 701, "Raiders", "Chief", (byte)AccountNation.ElMorad, members: 21);
        var player = Join(provider, "Visitor");

        await SendSiege(provider, player, Request(GameOpcodes.GS_SIEGE, WarfareNpc, 4));

        var reply = player.Only(GameOpcodes.GS_SIEGE);
        ExpectResult(reply, WarfareNpc, 4, Accepted);
        reply.ReadByte().Should().Be(1);
        reply.ReadSByteString().Should().Be("Raiders");
        reply.ReadByte().Should().Be((byte)AccountNation.ElMorad);
        reply.ReadByte().Should().Be(21);
        reply.ReadByte().Should().Be(1);
        reply.ReadByte().Should().Be(10);
        reply.ReadUInt().Should().Be(0);
        reply.ReadByte().Should().Be(2);
        reply.ReadByte().Should().Be(10);
        reply.ReadByte().Should().Be(15);
        reply.ReadByte().Should().Be(6);
        reply.RemainingBytes.Should().Be(0);
    }

    [Fact]
    public async Task WithoutARegistrationPeriodTheChallengerListCannotBeOpened()
    {
        var castle = Castle();
        castle.WarRequestDay = 0;
        using var provider = CreateWorld(siege: castle);
        var player = Join(provider, "Visitor");

        await SendSiege(provider, player, Request(GameOpcodes.GS_SIEGE, WarfareNpc, 4));

        player.Only(GameOpcodes.GS_SIEGE).GetData().Should().Equal(WarfareNpc, 4, 0xFE, 0xFF);
    }

    [Fact]
    public async Task TheDefendingUnionListsTheCastleOwner()
    {
        using var provider = CreateWorld(siege: Castle());
        AddClan(provider, CastleOwner, "Wardens", "Lord", (byte)AccountNation.Karus, members: 30);
        var player = Join(provider, "Visitor");

        await SendSiege(provider, player, Request(GameOpcodes.GS_SIEGE, WarfareNpc, 5));

        var reply = player.Only(GameOpcodes.GS_SIEGE);
        ExpectResult(reply, WarfareNpc, 5, Accepted);
        reply.ReadByte().Should().Be(1);
        reply.ReadSByteString().Should().Be("Wardens");
        reply.ReadByte().Should().Be((byte)AccountNation.Karus);
        reply.ReadByte().Should().Be(30);
        reply.RemainingBytes.Should().Be(0);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    public async Task OnlyTheCastleLordMayUseTheCastleManager(byte subType)
    {
        var castle = Castle();
        using var provider = CreateWorld(siege: castle);
        var player = Join(provider, "Visitor");
        player.Session.Money = 100;

        await SendSiege(provider, player, Request(GameOpcodes.GS_SIEGE, CastleManager, subType, 5, 0, 0, 0));

        player.Only(GameOpcodes.GS_SIEGE).GetData().Should().Equal(CastleManager, subType, 0xFD, 0xFF);
        player.Session.Money.Should().Be(100);
        castle.DungeonCharge.Should().Be(5_000);
        castle.MoradonTariff.Should().Be(10);
        castle.DellosTariff.Should().Be(12);
        castle.DungeonEntranceFee.Should().Be(2_500);
    }

    private static Player Lord(ServiceProvider provider)
    {
        var lord = Join(provider, "Lord");
        LeadClan(provider, lord, CastleOwner);
        return lord;
    }

    [Fact]
    public async Task TheCastleLordCollectsTheDungeonCharge()
    {
        var castle = Castle();
        using var provider = CreateWorld(siege: castle);
        var lord = Lord(provider);
        lord.Session.Money = 1_000;

        await SendSiege(provider, lord, Request(GameOpcodes.GS_SIEGE, CastleManager, 2));

        var reply = lord.Only(GameOpcodes.GS_SIEGE);
        ExpectResult(reply, CastleManager, 2, Accepted);
        reply.ReadInt().Should().Be(6_000);
        reply.ReadInt().Should().Be(5_000);
        reply.RemainingBytes.Should().Be(0);
        lord.Session.Money.Should().Be(6_000);
        castle.DungeonCharge.Should().Be(0);
    }

    [Fact]
    public async Task ACollectionPastTheCoinCapAsksToEmptyTheBagFirst()
    {
        var castle = Castle();
        using var provider = CreateWorld(siege: castle);
        var lord = Lord(provider);
        lord.Session.Money = CoinMax - 1;

        await SendSiege(provider, lord, Request(GameOpcodes.GS_SIEGE, CastleManager, 2));

        lord.Only(GameOpcodes.GS_SIEGE).GetData().Should().Equal(CastleManager, 2, 0xFB, 0xFF);
        castle.DungeonCharge.Should().Be(5_000);
    }

    [Fact]
    public async Task TheCastleLordSeesBothTariffsAndTheDungeonFee()
    {
        using var provider = CreateWorld(siege: Castle());
        var lord = Lord(provider);

        await SendSiege(provider, lord, Request(GameOpcodes.GS_SIEGE, CastleManager, 3));

        var reply = lord.Only(GameOpcodes.GS_SIEGE);
        ExpectResult(reply, CastleManager, 3, Accepted);
        reply.ReadShort().Should().Be(10);
        reply.ReadShort().Should().Be(12);
        reply.ReadInt().Should().Be(2_500);
        reply.RemainingBytes.Should().Be(0);
    }

    [Theory]
    [InlineData(4, MoradonZone)]
    [InlineData(5, DelosZone)]
    public async Task ATariffChangeIsBroadcastWithTheZoneAsAShort(byte subType, short zone)
    {
        var castle = Castle();
        using var provider = CreateWorld(siege: castle);
        var lord = Lord(provider);
        var bystander = Join(provider, "Bystander");
        var change = Request(GameOpcodes.GS_SIEGE, CastleManager, subType);
        change.WriteUShort(20);

        await SendSiege(provider, lord, change);

        foreach (var player in new[] { lord, bystander })
        {
            var reply = player.Only(GameOpcodes.GS_SIEGE);
            ExpectResult(reply, CastleManager, subType, Accepted);
            reply.ReadShort().Should().Be(20);
            reply.ReadShort().Should().Be(zone);
            reply.RemainingBytes.Should().Be(0);
        }

        (zone == MoradonZone ? castle.MoradonTariff : castle.DellosTariff).Should().Be(20);
    }

    [Fact]
    public async Task ATariffAboveTheCapIsNotAllowed()
    {
        var castle = Castle();
        using var provider = CreateWorld(siege: castle);
        var lord = Lord(provider);
        var change = Request(GameOpcodes.GS_SIEGE, CastleManager, 4);
        change.WriteUShort(21);

        await SendSiege(provider, lord, change);

        lord.Only(GameOpcodes.GS_SIEGE).GetData().Should().Equal(CastleManager, 4, 0xFB, 0xFF);
        castle.MoradonTariff.Should().Be(10);
    }

    [Fact]
    public async Task TheCastleLordSetsTheDungeonEntranceFee()
    {
        var castle = Castle();
        using var provider = CreateWorld(siege: castle);
        var lord = Lord(provider);
        var change = Request(GameOpcodes.GS_SIEGE, CastleManager, 6);
        change.WriteUInt(40_000);

        await SendSiege(provider, lord, change);

        var reply = lord.Only(GameOpcodes.GS_SIEGE);
        ExpectResult(reply, CastleManager, 6, Accepted);
        reply.ReadInt().Should().Be(40_000);
        reply.RemainingBytes.Should().Be(0);
        castle.DungeonEntranceFee.Should().Be(40_000);
        castle.DungeonCharge.Should().Be(5_000);
    }

    [Fact]
    public async Task ADungeonFeeBeyondTheCoinCapIsNotAllowed()
    {
        var castle = Castle();
        using var provider = CreateWorld(siege: castle);
        var lord = Lord(provider);
        var change = Request(GameOpcodes.GS_SIEGE, CastleManager, 6);
        change.WriteUInt((uint)CoinMax + 1);

        await SendSiege(provider, lord, change);

        lord.Only(GameOpcodes.GS_SIEGE).GetData().Should().Equal(CastleManager, 6, 0xFB, 0xFF);
        castle.DungeonEntranceFee.Should().Be(2_500);
    }
}
