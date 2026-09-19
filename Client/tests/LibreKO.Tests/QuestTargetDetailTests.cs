using LibreKO.Network;
using Xunit;

namespace LibreKO.Tests;

public class QuestTargetDetailTests
{
    private const string TargetHex =
        "0B013E001500000078020000D10100000E0042616E6469636F6F742068756E74090042616E6469636F6F74640042616E"
        + "6469636F6F740A4120726F64656E7420776869636820677265772061626E6F726D616C6C792062792066656564696E67"
        + "206F6E20636F7270736573206F66206576696C20726163657320696E2074686520576172207769746820506174686F73"
        + "2E1900496E6861626974732053656173686F7265206F6620486F7065";

    private const string ReceiptHex = "0A013E0002E8ECA4357701000000E9A4358C0A0000";

    private static Packet Frame(byte[] bytes, byte sub)
    {
        var packet = new Packet(GameOpcodes.GS_QUEST);
        packet.WriteBytes(bytes);
        Assert.Equal(sub, packet.ReadByte());
        return packet;
    }

    [Fact]
    public void TargetCarriesQuestIdentityLoreAndCoordinates()
    {
        var target = QuestTargetDetail.Read(Frame(Convert.FromHexString(TargetHex), 11));
        Assert.Equal(62, target.QuestId);
        Assert.Equal(21, target.ZoneId);
        Assert.Equal((632, 465), (target.X, target.Z));
        Assert.Equal("Bandicoot hunt", target.QuestTitle);
        Assert.Equal("Bandicoot", target.Target);
        Assert.StartsWith("Bandicoot\nA rodent which grew", target.About);
        Assert.Equal("Inhabits Seashore of Hope", target.Where);
        Assert.True(target.HasCoordinates);
    }

    [Fact]
    public void TruncatedAndExtendedTargetsAreRejected()
    {
        var fixture = Convert.FromHexString(TargetHex);
        for (var length = 2; length < fixture.Length; length++)
            Assert.ThrowsAny<Exception>(() => QuestTargetDetail.Read(Frame(fixture[..length], 11)));
        Assert.Throws<InvalidDataException>(() => QuestTargetDetail.Read(Frame([.. fixture, 0], 11)));
    }

    [Fact]
    public void ReceiptListsWhatTheClaimActuallyGranted()
    {
        var receipt = QuestReceipt.Read(Frame(Convert.FromHexString(ReceiptHex), 10));
        Assert.Equal(62, receipt.QuestId);
        Assert.Equal(
            [new QuestReceiptEntry(900001000, 375), new QuestReceiptEntry(900000000, 2700)],
            receipt.Granted);
    }

    [Fact]
    public void TruncatedAndExtendedReceiptsAreRejected()
    {
        var fixture = Convert.FromHexString(ReceiptHex);
        for (var length = 2; length < fixture.Length; length++)
            Assert.ThrowsAny<Exception>(() => QuestReceipt.Read(Frame(fixture[..length], 10)));
        Assert.Throws<InvalidDataException>(() => QuestReceipt.Read(Frame([.. fixture, 0], 10)));
    }
}
