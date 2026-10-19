using FluentAssertions;
using LibreKO.Common.Domain.Entities;
using LibreKO.Common.Domain.Services;
using LibreKO.Common.Enums;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.Protocol.Writers;
using LibreKO.Game.World;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace LibreKO.Game.Tests;

public class DailyRankTests
{
    private const short Warrior = 101;
    private const short Mage = 103;
    private const short Wolves = 10;
    private const short Bears = 11;

    private static readonly DateOnly Day1 = new(2026, 10, 8);
    private static readonly DateOnly Day2 = new(2026, 10, 9);

    private sealed class FakeRepository : IDailyRankRepository
    {
        public List<int> Merchant = [];
        public List<int> Loyalty = [];
        public List<int> Blessed = [];
        public List<DrakiClimber> Draki = [];
        public Dictionary<int, string> Names = [];
        public List<DailyRankPlace> Places = [];
        public Dictionary<int, long> MerchantGold = [];
        public int Resets;

        public Task AddMerchantGoldAsync(int characterId, long gold)
        {
            MerchantGold[characterId] = MerchantGold.GetValueOrDefault(characterId) + gold;
            return Task.CompletedTask;
        }

        public Task AddBlessedUpgradeAsync(int characterId) => Task.CompletedTask;

        public Task ResetCountersAsync()
        {
            Resets++;
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<int>> GetMerchantOrderAsync() => Task.FromResult<IReadOnlyList<int>>(Merchant.ToList());
        public Task<IReadOnlyList<int>> GetBlessedUpgradeOrderAsync() => Task.FromResult<IReadOnlyList<int>>(Blessed.ToList());
        public Task<IReadOnlyList<int>> GetMonthlyLoyaltyOrderAsync() => Task.FromResult<IReadOnlyList<int>>(Loyalty.ToList());
        public Task<IReadOnlyList<DrakiClimber>> GetDrakiClimbersAsync() => Task.FromResult<IReadOnlyList<DrakiClimber>>(Draki.ToList());

        public Task<IReadOnlyDictionary<int, string>> GetNamesAsync(IReadOnlyCollection<int> characterIds) =>
            Task.FromResult<IReadOnlyDictionary<int, string>>(
                characterIds.Where(Names.ContainsKey).ToDictionary(id => id, id => Names[id]));

        public Task<IReadOnlyList<DailyRankPlace>> GetPlacesAsync() =>
            Task.FromResult<IReadOnlyList<DailyRankPlace>>(Places.Select(Copy).ToList());

        public Task ReplacePlacesAsync(IReadOnlyList<DailyRankPlace> places)
        {
            Places = places.Select(Copy).ToList();
            return Task.CompletedTask;
        }

        private static DailyRankPlace Copy(DailyRankPlace place) => new()
        {
            Board = place.Board, SubjectId = place.SubjectId, Place = place.Place,
            PreviousPlace = place.PreviousPlace, RankedOn = place.RankedOn,
        };
    }

    private sealed record Player(UserSession Session, IClient Client, List<Packet> Sent);

    private sealed record BoardReply(byte Board, int MyPlace, int? Change, List<string> Names);

    private readonly SessionManager sessions = new();
    private readonly FakeRepository repository = new();
    private readonly DailyRankService service;

    public DailyRankTests()
    {
        var provider = new ServiceCollection().AddSingleton<IDailyRankRepository>(repository).BuildServiceProvider();
        service = new DailyRankService(sessions, provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<DailyRankService>.Instance);
        repository.Names = new Dictionary<int, string> { [1] = "Alpha", [2] = "Bravo", [3] = "Charlie" };
    }

    private Player Join(int id, short classId = Warrior, short clan = 0, bool gm = false)
    {
        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        var sent = new List<Packet>();
        client.SendPacket(Arg.Do<Packet>(p => sent.Add(p)), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        var session = sessions.CreateSession(client, id, id);
        session.Name = repository.Names.GetValueOrDefault(id, $"Player{id}");
        session.Class = classId;
        session.KnightsId = clan;
        session.Nation = AccountNation.Karus;
        session.IsGM = gm;
        return new Player(session, client, sent);
    }

    private static Packet Request(byte board)
    {
        var packet = new Packet(GameOpcodes.GS_DAILY_RANK);
        packet.WriteByte(DailyRankPacketWriter.BoardSub);
        packet.WriteByte(board);
        packet.ResetOffset();
        return packet;
    }

    private static BoardReply Read(Packet packet)
    {
        packet.ResetOffset();
        packet.ReadByte().Should().Be(DailyRankPacketWriter.BoardSub);
        var board = packet.ReadByte();
        var myPlace = packet.ReadInt();
        int? change = board == (byte)DailyRankBoard.ConquerorOfDraki ? null : packet.ReadInt();
        var count = packet.ReadShort();
        var names = new List<string>();
        for (var i = 0; i < count; i++)
            names.Add(packet.ReadSByteString());
        packet.RemainingBytes.Should().Be(0);
        return new BoardReply(board, myPlace, change, names);
    }

    private async Task<BoardReply> AskAsync(Player player, DailyRankBoard board)
    {
        player.Sent.Clear();
        await service.HandleAsync(player.Client, Request((byte)board));
        player.Sent.Should().ContainSingle();
        return Read(player.Sent[0]);
    }

    [Fact]
    public void TheDrakiBoardCarriesNoChange()
    {
        var other = DailyRankPacketWriter.Board(DailyRankBoard.GrandMerchant, 2, 1, ["Alpha", "Bravo"]);
        var draki = DailyRankPacketWriter.Board(DailyRankBoard.ConquerorOfDraki, 2, 1, ["Alpha", "Bravo"]);

        Read(other).Should().BeEquivalentTo(new BoardReply(0, 2, 1, ["Alpha", "Bravo"]));
        Read(draki).Should().BeEquivalentTo(new BoardReply(5, 2, null, ["Alpha", "Bravo"]));
    }

    [Fact]
    public void TheListStopsAtAHundredNames()
    {
        var names = Enumerable.Range(1, DailyRankPacketWriter.MaxNames + 5).Select(i => $"N{i}").ToList();

        Read(DailyRankPacketWriter.Board(DailyRankBoard.KnightOfAdonis, 0, 0, names)).Names
            .Should().HaveCount(DailyRankPacketWriter.MaxNames);
    }

    [Theory]
    [InlineData(3, 5, 2)]
    [InlineData(5, 3, -2)]
    [InlineData(4, 4, 0)]
    [InlineData(1, 0, DailyRankPacketWriter.NewEntryChange)]
    [InlineData(0, 3, 0)]
    public void ChangeIsYesterdaysPlaceMinusTodays(int place, int previous, int change)
    {
        DailyRankPacketWriter.Change(place, previous).Should().Be(change);
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(1, true)]
    [InlineData(2, true)]
    [InlineData(3, false)]
    [InlineData(4, true)]
    [InlineData(5, true)]
    [InlineData(6, true)]
    [InlineData(7, false)]
    [InlineData(8, true)]
    [InlineData(9, false)]
    public void OnlyThe2619BoardsAreServed(byte board, bool served)
    {
        DailyRankPacketWriter.IsBoard(board).Should().Be(served);
    }

    [Fact]
    public async Task ARollOverRanksAgainstYesterdayAndTellsEveryoneToDropTheirLists()
    {
        repository.Merchant = [1, 2];
        await service.LoadAsync(Day1);
        var alpha = Join(1);
        var bravo = Join(2);
        (await AskAsync(alpha, DailyRankBoard.GrandMerchant)).Should()
            .BeEquivalentTo(new BoardReply(0, 1, DailyRankPacketWriter.NewEntryChange, ["Alpha", "Bravo"]));

        repository.Merchant = [2, 1, 3];
        await service.RollOverAsync(Day2);

        bravo.Sent.Should().ContainSingle();
        bravo.Sent[0].ResetOffset();
        bravo.Sent[0].ReadByte().Should().Be(DailyRankPacketWriter.ResetSub);
        (await AskAsync(bravo, DailyRankBoard.GrandMerchant)).Should()
            .BeEquivalentTo(new BoardReply(0, 1, 1, ["Bravo", "Alpha", "Charlie"]));
    }

    [Fact]
    public async Task ASecondRankingOnTheSameDayStillComparesWithYesterday()
    {
        repository.Merchant = [1, 2];
        await service.LoadAsync(Day1);
        repository.Merchant = [2, 1];
        await service.RollOverAsync(Day2);
        await service.RollOverAsync(Day2);

        repository.Places.Single(p => p.SubjectId == 2).PreviousPlace.Should().Be(2);
        (await AskAsync(Join(2), DailyRankBoard.GrandMerchant)).Change.Should().Be(1);
    }

    [Fact]
    public async Task StartingOnTheSameDayReusesTheStoredPlaces()
    {
        repository.Merchant = [1, 2];
        await service.LoadAsync(Day2);
        repository.Merchant = [2, 1];
        await service.LoadAsync(Day2);

        (await AskAsync(Join(1), DailyRankBoard.GrandMerchant)).MyPlace.Should().Be(1);
    }

    [Fact]
    public async Task TheCountersResetWhenTheMonthTurns()
    {
        repository.Merchant = [1];
        await service.LoadAsync(new DateOnly(2026, 9, 30));
        await service.RollOverAsync(new DateOnly(2026, 10, 1));
        await service.RollOverAsync(new DateOnly(2026, 10, 2));

        repository.Resets.Should().Be(1);
    }

    [Fact]
    public async Task TheDrakiBoardListsOnlyTheRequestersClassLive()
    {
        repository.Draki =
        [
            new DrakiClimber(1, "Alpha", Warrior, 9, 2),
            new DrakiClimber(2, "Bravo", Mage, 8, 5),
            new DrakiClimber(3, "Charlie", Mage, 7, 1),
        ];
        await service.LoadAsync(Day2);

        (await AskAsync(Join(3, Mage), DailyRankBoard.ConquerorOfDraki)).Should()
            .BeEquivalentTo(new BoardReply(5, 2, null, ["Bravo", "Charlie"]));
    }

    [Fact]
    public async Task TheClanBoardNamesClansAndPlacesMyClan()
    {
        sessions.Knights.AddClan(Wolves, new KnightsEntity { Id = Wolves, Name = "Wolves", Points = 300 });
        sessions.Knights.AddClan(Bears, new KnightsEntity { Id = Bears, Name = "Bears", Points = 500 });
        await service.LoadAsync(Day2);

        (await AskAsync(Join(1, clan: Wolves), DailyRankBoard.KnightsOfFelankor)).Should()
            .BeEquivalentTo(new BoardReply(8, 2, DailyRankPacketWriter.NewEntryChange, ["Bears", "Wolves"]));
        (await AskAsync(Join(2), DailyRankBoard.KnightsOfFelankor)).MyPlace.Should().Be(0);
    }

    [Fact]
    public async Task BoardsWithoutAFeedAnswerEmpty()
    {
        await service.LoadAsync(Day2);

        (await AskAsync(Join(1), DailyRankBoard.HeroOfChaos)).Should()
            .BeEquivalentTo(new BoardReply(2, 0, 0, []));
    }

    [Fact]
    public async Task ASecondRequestInsideASecondIsDropped()
    {
        await service.LoadAsync(Day2);
        var alpha = Join(1);
        await service.HandleAsync(alpha.Client, Request((byte)DailyRankBoard.GrandMerchant));
        await service.HandleAsync(alpha.Client, Request((byte)DailyRankBoard.KnightOfAdonis));

        alpha.Sent.Should().ContainSingle();
    }

    [Fact]
    public async Task UnknownBoardsGetNoAnswer()
    {
        await service.LoadAsync(Day2);
        var alpha = Join(1);
        await service.HandleAsync(alpha.Client, Request(3));

        alpha.Sent.Should().BeEmpty();
    }

    [Fact]
    public async Task GameMasterSalesAreNotCounted()
    {
        await service.CountMerchantSaleAsync(Join(1, gm: true).Session, 500);
        await service.CountMerchantSaleAsync(Join(2).Session, 700);

        repository.MerchantGold.Should().Equal(new Dictionary<int, long> { [2] = 700 });
    }
}
