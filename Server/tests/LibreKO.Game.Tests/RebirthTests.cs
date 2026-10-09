using FluentAssertions;
using LibreKO.Common.Domain.Entities.GameData;
using LibreKO.Common.Domain.Services;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.Protocol;
using LibreKO.Game.Protocol.Writers;
using LibreKO.Game.World;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace LibreKO.Game.Tests;

public class RebirthTests
{
    private const int QualificationOfRebirth = 900_579_000;
    private const int TradePartner = 2;
    private const short AliveHp = 100;
    private const long LevelExperience = 1_000;
    private const byte RebirthStatChange = 7;

    private static (CharacterDevelopmentPacketCoordinator Coordinator, UserSession Session, IClient Client, IPlayerProgressionService Progression)
        Arrange(byte level, short rebirthLevel = 0)
    {
        var sessions = new SessionManager();
        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        var session = sessions.CreateSession(client, 1, 1);
        session.Level = level;
        session.Hp = AliveHp;
        session.RebirthLevel = rebirthLevel;
        session.Experience = RebirthBonus.RequiredExperience(LevelExperience, rebirthLevel);
        var slot = session.Inventory[InventoryConstants.SlotMax];
        slot.ItemId = QualificationOfRebirth;
        slot.Count = 1;
        var progression = Substitute.For<IPlayerProgressionService>();
        var gameData = Substitute.For<IGameDataService>();
        gameData.GetMaxExpForLevel(Arg.Any<byte>()).Returns(LevelExperience);
        var coordinator = new CharacterDevelopmentPacketCoordinator(
            sessions,
            gameData,
            Substitute.For<IJobChangeService>(),
            progression,
            Substitute.For<IUserNotificationService>(),
            Substitute.For<ILogger<CharacterDevelopmentPacketCoordinator>>());
        return (coordinator, session, client, progression);
    }

    private static Packet RebirthRequest(byte str, byte sta, byte dex, byte intel, byte cha)
    {
        var packet = new Packet(GameOpcodes.GS_CLASS_CHANGE);
        packet.WriteByte((byte)ClassChangeSubOpcode.RebirthStatChange);
        packet.WriteByte(str);
        packet.WriteByte(sta);
        packet.WriteByte(dex);
        packet.WriteByte(intel);
        packet.WriteByte(cha);
        packet.ResetOffset();
        return packet;
    }

    private static Task Replied(IClient client, RebirthResult result) =>
        client.Received(1).SendPacket(Arg.Is<Packet>(packet =>
            packet.GetOpcode() == (byte)GameOpcodes.GS_CLASS_CHANGE
            && packet.GetData().SequenceEqual(RebirthPacketWriter.Result(result).GetData())));

    [Theory]
    [InlineData(RebirthResult.Success, new byte[] { RebirthStatChange, 0x01, 0x00 })]
    [InlineData(RebirthResult.NoQualification, new byte[] { RebirthStatChange, 0xFE, 0xFF })]
    [InlineData(RebirthResult.LevelTooLow, new byte[] { RebirthStatChange, 0xFD, 0xFF })]
    [InlineData(RebirthResult.ExperienceNotFull, new byte[] { RebirthStatChange, 0xFB, 0xFF })]
    [InlineData(RebirthResult.Unavailable, new byte[] { RebirthStatChange, 0xF9, 0xFF })]
    public void TheRebirthReplyIsTheSubOpcodeAndASigned16BitResult(RebirthResult result, byte[] body)
    {
        var packet = RebirthPacketWriter.Result(result);

        packet.GetOpcode().Should().Be((byte)GameOpcodes.GS_CLASS_CHANGE);
        packet.GetData().Should().Equal(body);
    }

    [Fact]
    public async Task ARebirthAtLevel83ConsumesTheQualificationAddsTwoPointsAndCompletes()
    {
        var (coordinator, session, client, progression) = Arrange(ProgressionTable.MaxLevel);

        await coordinator.HandleClassChangeAsync(client, RebirthRequest(1, 0, 1, 0, 0));

        session.RebirthLevel.Should().Be(1);
        session.RebStr.Should().Be(1);
        session.RebDex.Should().Be(1);
        session.Inventory[InventoryConstants.SlotMax].Count.Should().Be(0, "the Qualification of Rebirth is spent");
        await progression.Received(1).CompleteRebirthAsync(session);
        await Replied(client, RebirthResult.Success);
    }

    [Theory]
    [InlineData(82)]
    [InlineData(60)]
    public async Task ARebirthBelowLevel83IsRefusedAndKeepsTheQualification(byte level)
    {
        var (coordinator, session, client, progression) = Arrange(level);

        await coordinator.HandleClassChangeAsync(client, RebirthRequest(2, 0, 0, 0, 0));

        session.RebirthLevel.Should().Be(0);
        session.RebStr.Should().Be(0);
        session.Inventory[InventoryConstants.SlotMax].Count.Should().Be(1);
        await progression.DidNotReceive().CompleteRebirthAsync(Arg.Any<UserSession>());
        await Replied(client, RebirthResult.LevelTooLow);
    }

    [Theory]
    [InlineData(1, 0, 0, 0, 0)]
    [InlineData(2, 1, 0, 0, 0)]
    public async Task ARebirthMustPlaceExactlyTwoPoints(byte str, byte sta, byte dex, byte intel, byte cha)
    {
        var (coordinator, session, client, progression) = Arrange(ProgressionTable.MaxLevel);

        await coordinator.HandleClassChangeAsync(client, RebirthRequest(str, sta, dex, intel, cha));

        session.RebirthLevel.Should().Be(0);
        session.Inventory[InventoryConstants.SlotMax].Count.Should().Be(1);
        await progression.DidNotReceive().CompleteRebirthAsync(Arg.Any<UserSession>());
        await Replied(client, RebirthResult.Unavailable);
    }

    [Fact]
    public async Task TheFifteenthRebirthIsTheLast()
    {
        var (coordinator, session, client, progression) = Arrange(ProgressionTable.MaxLevel, RebirthBonus.MaxRebirthLevel);

        await coordinator.HandleClassChangeAsync(client, RebirthRequest(0, 2, 0, 0, 0));

        session.RebirthLevel.Should().Be(RebirthBonus.MaxRebirthLevel);
        session.Inventory[InventoryConstants.SlotMax].Count.Should().Be(1);
        await progression.DidNotReceive().CompleteRebirthAsync(Arg.Any<UserSession>());
        await Replied(client, RebirthResult.Unavailable);
    }

    [Theory]
    [InlineData("dead")]
    [InlineData("trade")]
    [InlineData("merchant")]
    [InlineData("preparing")]
    [InlineData("gathering")]
    public async Task ConflictingActionsCannotConsumeARebirthQualification(string state)
    {
        var (coordinator, session, client, progression) = Arrange(ProgressionTable.MaxLevel);
        switch (state)
        {
            case "dead": session.Hp = 0; break;
            case "trade": session.Trade.ExchangeUser = TradePartner; break;
            case "merchant": session.Trade.MerchantState = MerchantMode.Selling; break;
            case "preparing": session.Trade.IsSellingMerchantPreparing = true; break;
            case "gathering": session.IsMining = true; break;
        }

        await coordinator.HandleClassChangeAsync(client, RebirthRequest(2, 0, 0, 0, 0));

        session.RebirthLevel.Should().Be(0);
        session.RebStr.Should().Be(0);
        session.Inventory[InventoryConstants.SlotMax].Count.Should().Be(1);
        await progression.DidNotReceive().CompleteRebirthAsync(Arg.Any<UserSession>());
        await Replied(client, RebirthResult.Unavailable);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(RebirthBonus.StatCount - 1)]
    [InlineData(RebirthBonus.StatCount + 1)]
    public async Task AMalformedAllocationKeepsTheQualification(int length)
    {
        var (coordinator, session, client, progression) = Arrange(ProgressionTable.MaxLevel);
        var packet = new Packet(GameOpcodes.GS_CLASS_CHANGE);
        packet.WriteByte((byte)ClassChangeSubOpcode.RebirthStatChange);
        for (int index = 0; index < length; index++)
            packet.WriteByte(index == 0 ? (byte)RebirthBonus.PointsPerRebirth : (byte)0);
        packet.ResetOffset();

        await coordinator.HandleClassChangeAsync(client, packet);

        session.RebirthLevel.Should().Be(0);
        session.Inventory[InventoryConstants.SlotMax].Count.Should().Be(1);
        await progression.DidNotReceive().CompleteRebirthAsync(Arg.Any<UserSession>());
        await Replied(client, RebirthResult.Unavailable);
    }

    [Fact]
    public async Task ARepeatedRequestCannotSpendTheSameQualificationTwice()
    {
        var (coordinator, session, client, progression) = Arrange(ProgressionTable.MaxLevel);
        session.Experience = RebirthBonus.RequiredExperience(LevelExperience, RebirthBonus.MaxRebirthLevel);

        await coordinator.HandleClassChangeAsync(client, RebirthRequest(2, 0, 0, 0, 0));
        await coordinator.HandleClassChangeAsync(client, RebirthRequest(2, 0, 0, 0, 0));

        session.RebirthLevel.Should().Be(1);
        session.RebStr.Should().Be(2);
        await progression.Received(1).CompleteRebirthAsync(session);
        await Replied(client, RebirthResult.Success);
        await Replied(client, RebirthResult.NoQualification);
    }

    [Fact]
    public async Task ARebirthWithoutTheQualificationIsRefused()
    {
        var (coordinator, session, client, progression) = Arrange(ProgressionTable.MaxLevel);
        session.Inventory[InventoryConstants.SlotMax].Clear();

        await coordinator.HandleClassChangeAsync(client, RebirthRequest(2, 0, 0, 0, 0));

        session.RebirthLevel.Should().Be(0);
        session.RebStr.Should().Be(0);
        await progression.DidNotReceive().CompleteRebirthAsync(Arg.Any<UserSession>());
        await Replied(client, RebirthResult.NoQualification);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public async Task ARebirthNeedsAFullExperienceBar(short rebirthLevel)
    {
        var (coordinator, session, client, progression) = Arrange(ProgressionTable.MaxLevel, rebirthLevel);
        session.Experience = RebirthBonus.RequiredExperience(LevelExperience, rebirthLevel) - 1;

        await coordinator.HandleClassChangeAsync(client, RebirthRequest(2, 0, 0, 0, 0));

        session.RebirthLevel.Should().Be(rebirthLevel);
        session.Inventory[InventoryConstants.SlotMax].Count.Should().Be(1);
        await progression.DidNotReceive().CompleteRebirthAsync(Arg.Any<UserSession>());
        await Replied(client, RebirthResult.ExperienceNotFull);
    }
}
