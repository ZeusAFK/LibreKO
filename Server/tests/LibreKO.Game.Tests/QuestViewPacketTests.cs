using FluentAssertions;
using LibreKO.Game.Protocol.Writers;
using LibreKO.Quests.Binding;
using LibreKO.Quests.Runtime;

namespace LibreKO.Game.Tests;

public class QuestViewPacketTests
{
    private const string GodotViewFixture =
        "1002DB062D7F0000470000000D0301808AA86A000000000D00456C697465205069726174657310004465666561742061207069"
        + "726174652E0A0057656C6C20646F6E652100010100010002302A0000312A00001300456C69746520506972617465204D656D62"
        + "657201020000020000000040420F00000000000003000000006400000000000000000001000A00446972656374696F6E73";

    private const string GodotViewWithOptionsFixture =
        "1002DB062D7F0000470000000D0301808AA86A000000000D00456C697465205069726174657310004465666561742061207069"
        + "726174652E0A0057656C6C20646F6E652100010100010002302A0000312A00001300456C69746520506972617465204D656D62"
        + "657201020000020000000040420F0000000000000300000000640000000000000002000000A1D3F33901000000000000000000"
        + "69E5F439010000000000000001000A00446972656374696F6E73";

    private const string GodotTargetFixture =
        "0B013E001500000078020000D10100000E0042616E6469636F6F742068756E74090042616E6469636F6F74640042616E646963"
        + "6F6F740A4120726F64656E7420776869636820677265772061626E6F726D616C6C792062792066656564696E67206F6E20636F"
        + "7270736573206F66206576696C20726163657320696E2074686520576172207769746820506174686F732E1900496E68616269"
        + "74732053656173686F7265206F6620486F7065";

    private static QuestLocation Bandicoot() => new(
        "bandicoot", "Bandicoot", "Inhabits Seashore of Hope", 632, 465, 21,
        "Bandicoot\nA rodent which grew abnormally by feeding on corpses of evil races in the War with Pathos.",
        1018, 765);

    [Fact]
    public void TargetDetailMatchesGodotFixtureAndCarriesTheQuestAndLore()
    {
        Convert.ToHexString(QuestPacketWriter.TargetDetail(
            Bandicoot(), 1, 1, 62, "Bandicoot hunt", t => t).GetData())
            .Should().Be(GodotTargetFixture);
    }

    [Fact]
    public void TargetDetailSendsTheNationsOwnCoordinates()
    {
        var elmorad = QuestPacketWriter.TargetDetail(Bandicoot(), 1, 2, 62, "Bandicoot hunt", t => t).GetData();
        BitConverter.ToInt32(elmorad, 8).Should().Be(1018);
        BitConverter.ToInt32(elmorad, 12).Should().Be(765);
    }

    [Fact]
    public void TargetDetailReportsAMissingCoordinateRatherThanGuessing()
    {
        var missing = QuestPacketWriter.TargetDetail(
            Bandicoot() with { ElMoradX = null, ElMoradY = null }, 1, 2, 62, "Bandicoot hunt", t => t).GetData();
        BitConverter.ToInt32(missing, 8).Should().Be(QuestPacketWriter.NoCoordinate);
        BitConverter.ToInt32(missing, 12).Should().Be(QuestPacketWriter.NoCoordinate);
    }

    [Fact]
    public void RewardReceiptListsWhatTheClaimGranted()
    {
        Convert.ToHexString(QuestPacketWriter.RewardReceipt(62, [(900001000, 375), (900000000, 2700)]).GetData())
            .Should().Be("0A013E0002E8ECA4357701000000E9A4358C0A0000");
    }

    [Fact]
    public void ViewMatchesGodotFixtureAndUsesTheDeclaredRewardPlan()
    {
        Convert.ToHexString(QuestPacketWriter.View(Pirates(QuestPageKind.Quest), 32557, true, 1789430400,
            t => t, _ => "Elite Pirate Member").GetData())
            .Should().Be(GodotViewFixture);
    }

    [Fact]
    public void ViewCarriesTheRewardOptionsAfterTheFixedTransfers()
    {
        var pirates = Pirates(QuestPageKind.Quest);
        var view = pirates with
        {
            Rewards = pirates.Rewards with
            {
                Options =
                [
                    new(default, QuestActionKind.GiveItem, new ArgumentSet(new Dictionary<string, long> { ["item"] = 972280737, ["count"] = 1 })),
                    new(default, QuestActionKind.GiveItem, new ArgumentSet(new Dictionary<string, long> { ["item"] = 972350825, ["count"] = 1 }))
                ]
            }
        };
        Convert.ToHexString(QuestPacketWriter.View(view, 32557, true, 1789430400, t => t, _ => "Elite Pirate Member").GetData())
            .Should().Be(GodotViewWithOptionsFixture);
    }

    [Fact]
    public void AnAutoAcceptedQuestCarriesNoJournalTurnInOrClaimControls()
    {
        var rescue = Pirates(QuestPageKind.Quest) with
        {
            AutoAccepted = true,
            Rewards = new QuestRewards(1755, []),
        };
        var closed = QuestPacketWriter.View(rescue, 0, false, 0, t => t).GetData();
        (closed[12] & 32).Should().Be(32);
        (closed[12] & 64).Should().Be(0);
        (closed[12] & 4).Should().Be(0);
    }

    [Fact]
    public void ConversationPagesCarryNoAcceptOrClaimControls()
    {
        var conversation = QuestPacketWriter.View(Pirates(QuestPageKind.Conversation), 32557, true, 0,
            t => t, _ => "Elite Pirate Member").GetData();
        var page = QuestPacketWriter.View(Pirates(QuestPageKind.Quest), 32557, true, 0,
            t => t, _ => "Elite Pirate Member").GetData();
        (conversation[12] & 6).Should().Be(0);
        (page[12] & 4).Should().Be(4);
        conversation[14].Should().Be((byte)QuestPageKind.Conversation);
        page[14].Should().Be((byte)QuestPageKind.Quest);
    }

    private static QuestView Pirates(QuestPageKind page) => new(
        new QuestText(1755, "Elite Pirates", "Defeat a pirate.", true), 71,
        QuestViewState.Claimable, new QuestObjectives(1755, [new(1, [10800, 10801], 0)]), [1],
        new QuestRewards(1755, [
            new(default, QuestActionKind.GiveExperience, new ArgumentSet(new Dictionary<string, long> { ["amount"] = 1000000 })),
            new(default, QuestActionKind.GiveNationalPoints, new ArgumentSet(new Dictionary<string, long> { ["amount"] = 100 }))]),
        DialogLine.FromText("Well done!"), [new(DialogLine.FromText("Directions"), 1234)], page);
}
