using LibreKO.Domain;
using LibreKO.Network;
using Xunit;

namespace LibreKO.Tests;

public class QuestViewTests
{
    private const string Fixture = "1002DB062D7F0000470000000D0301808AA86A000000000D00456C697465205069726174657310004465666561742061207069726174652E0A0057656C6C20646F6E652100010100010002302A0000312A00001300456C69746520506972617465204D656D62657201020000020000000040420F00000000000003000000006400000000000000000001000A00446972656374696F6E73";

    private const string FixtureWithOptions = "1002DB062D7F0000470000000D0301808AA86A000000000D00456C697465205069726174657310004465666561742061207069726174652E0A0057656C6C20646F6E652100010100010002302A0000312A00001300456C69746520506972617465204D656D62657201020000020000000040420F0000000000000300000000640000000000000002000000A1D3F3390100000000000000000069E5F439010000000000000001000A00446972656374696F6E73";

    private static QuestView Read(byte[] bytes)
    {
        var packet = new Packet(GameOpcodes.GS_QUEST);
        packet.WriteBytes(bytes);
        Assert.Equal((byte)QuestSub.View, packet.ReadByte());
        return QuestView.Read(packet);
    }

    [Fact]
    public void ServerViewCarriesStateGoalsRewardsAndSubOptions()
    {
        var view = Read(Convert.FromHexString(Fixture));
        Assert.Equal(1755, view.QuestId);
        Assert.Equal(32557, view.NpcId);
        Assert.Equal(71, view.ZoneId);
        Assert.True(view.Open);
        Assert.True(view.CanClaim);
        Assert.False(view.CanAccept);
        Assert.True(view.Daily);
        Assert.Equal(QuestViewState.Claimable, view.State);
        Assert.Equal("Elite Pirates", view.Title);
        Assert.Equal("Defeat a pirate.", view.Journal);
        Assert.Equal("Well done!", view.Dialogue);
        Assert.Equal((1, 1), view.Objectives.Progress(view.Counts));
        Assert.Equal(new[] { 10800, 10801 }, view.Objectives.Groups[0].Monsters);
        Assert.Equal("Elite Pirate Member", view.Objectives.Groups[0].Name);
        Assert.True(view.Objectives.Groups[0].HasTarget);
        Assert.Equal(QuestPageKind.Quest, view.Page);
        Assert.Equal(1000000, view.Transfers[0].Count);
        Assert.Equal(900001000, view.Transfers[0].DisplayItemId);
        Assert.Equal(100, view.Transfers[1].Count);
        Assert.Equal(900003000, view.Transfers[1].DisplayItemId);
        Assert.Equal(new[] { "Directions" }, view.Topics);
    }

    private static string Standing(QuestViewState state, string journal) =>
        new QuestView(1, 0, 0, true, false, false, false, state, 0, "", journal, "",
            new QuestObjectives(1, false, []), [], [], []).StandingObjective;

    [Fact]
    public void AQuestWithNothingToHuntOrCollectNamesTheJournalAsItsObjective()
    {
        Assert.Equal("You must talk to [Hunter] Halon.",
            Standing(QuestViewState.Available, "You must talk to [Hunter] Halon."));
        Assert.Equal("Ready to turn in.",
            Standing(QuestViewState.Claimable, "You must talk to [Hunter] Halon."));
        Assert.Equal("Follow the quest instructions.",
            Standing(QuestViewState.InProgress, ""));
    }

    [Theory]
    [InlineData(1, 0, QuestData.CoinItemId)]
    [InlineData(2, 0, QuestData.ExpItemId)]
    [InlineData(3, 0, QuestData.LadderPointItemId)]
    [InlineData(4, 0, QuestData.JobChangeItemId)]
    [InlineData(5, 0, QuestData.JobChangeItemId)]
    [InlineData(0, 810090000, 810090000)]
    public void EveryTransferKindNamesAnIconItem(byte kind, int itemId, int expected)
    {
        var transfer = new QuestTransfer(false, kind, itemId, 1, 0);
        Assert.Equal(expected, transfer.DisplayItemId);
        Assert.Equal(kind != 0, QuestData.IsVirtualReward(transfer.DisplayItemId));
    }

    [Fact]
    public void AvailabilityNotificationKeepsTextAndTopicsWithoutNpcActions()
    {
        var bytes = Convert.FromHexString(Fixture);
        bytes[12] = 17;
        bytes[14] = 0;
        var view = Read(bytes);
        Assert.True(view.Notification);
        Assert.Equal(QuestPageKind.Conversation, view.Page);
        Assert.True(view.Open);
        Assert.False(view.CanAccept);
        Assert.False(view.CanClaim);
        Assert.Equal("Well done!", view.Dialogue);
        Assert.Equal(new[] { "Directions" }, view.Topics);
    }

    [Theory]
    [InlineData(16, 3)]
    [InlineData(19, 1)]
    [InlineData(21, 3)]
    [InlineData(17, 2)]
    public void InvalidNotificationModesAreRejected(byte flags, byte state)
    {
        var bytes = Convert.FromHexString(Fixture);
        bytes[12] = flags;
        bytes[13] = state;
        Assert.Throws<InvalidDataException>(() => Read(bytes));
    }

    [Fact]
    public void ConversationPagesNeverCarryAcceptOrClaimControls()
    {
        var bytes = Convert.FromHexString(Fixture);
        bytes[14] = 0;
        Assert.Throws<InvalidDataException>(() => Read(bytes));
    }

    [Theory]
    [InlineData(2)]
    [InlineData(4)]
    public void AutoAcceptedQuestCanNotifyItsStartAndCompletion(byte state)
    {
        var bytes = Convert.FromHexString(Fixture);
        bytes[12] = 49;
        bytes[13] = state;
        bytes[14] = 0;
        var view = Read(bytes);
        Assert.True(view.AutoAccepted);
        Assert.True(view.Notification);
        Assert.False(view.CanAccept);
        Assert.False(view.CanClaim);
    }

    [Fact]
    public void EveryTruncatedViewIsRejected()
    {
        var bytes = Convert.FromHexString(Fixture);
        for (var length = 1; length < bytes.Length; length++)
            Assert.Throws<InvalidDataException>(() => Read(bytes[..length]));
        Assert.Throws<InvalidDataException>(() => Read([.. bytes, 0]));
    }

    [Fact]
    public void AChoiceOfRewardsArrivesAfterTheFixedRewards()
    {
        var view = Read(Convert.FromHexString(FixtureWithOptions));
        Assert.Equal(2, view.Transfers.Length);
        Assert.Equal(new[] { 972280737, 972350825 }, view.Options.Select(o => o.ItemId));
        Assert.All(view.Options, o => Assert.False(o.Take));
        Assert.Empty(Read(Convert.FromHexString(Fixture)).Options);
    }

    [Theory]
    [InlineData(137, 1)]
    [InlineData(138, 4)]
    public void AnOptionIsNeverACostOrAPromotion(int offset, byte value)
    {
        var bytes = Convert.FromHexString(FixtureWithOptions);
        bytes[offset] = value;
        Assert.Throws<InvalidDataException>(() => Read(bytes));
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(12, 15)]
    [InlineData(13, 99)]
    [InlineData(14, 2)]
    public void UnsupportedVersionAndInconsistentActionsAreRejected(int offset, byte value)
    {
        var bytes = Convert.FromHexString(Fixture);
        bytes[offset] = value;
        Assert.Throws<InvalidDataException>(() => Read(bytes));
    }
}
