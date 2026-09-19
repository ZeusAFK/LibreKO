using LibreKO.Network;
using Xunit;

namespace LibreKO.Tests;

public class NpcDialogTests
{
    private const string DialogFixture = "640000000209030000FFFFFFFFFFFFFFFF0A000000FFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFF07612E7175657374444C473101060043686F6F736502050053776F72640000";
    private const string SpeechFixture = "FFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFF444C473101020400486F6C610300427965";

    private const string LargeDialogFixture = "640000000209030000FFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFF07612E7175657374444C473201060043686F6F73650D00FFFFFFFF010041FFFFFFFF010042FFFFFFFF010043FFFFFFFF010044FFFFFFFF010045FFFFFFFF010046FFFFFFFF010047FFFFFFFF010048FFFFFFFF010049FFFFFFFF01004AFFFFFFFF01004BFFFFFFFF01004CFFFFFFFF01004D";

    private static Packet FromHex(string hex)
    {
        var packet = new Packet(GameOpcodes.GS_SELECT_MSG);
        packet.WriteBytes(Convert.FromHexString(hex));
        return packet;
    }

    [Fact]
    public void LargeMenusKeepButtonsBeyondTheLegacySlots()
    {
        var packet = FromHex(LargeDialogFixture);
        var dialog = NpcDialog.Read(packet);
        Assert.Equal("Choose", dialog.HeaderText);
        Assert.Equal(13, dialog.Buttons.Count);
        Assert.Equal((12, -1, "M"), dialog.Buttons[12]);
        Assert.Equal(0, packet.RemainingBytes);
    }

    [Fact]
    public void TruncatedLargeMenusAreRejected()
    {
        Assert.Throws<InvalidDataException>(() => NpcDialog.Read(FromHex(LargeDialogFixture[..^2])));
    }

    [Fact]
    public void TheLastByteAddressableButtonSurvivesDecoding()
    {
        var packet = new Packet(GameOpcodes.GS_SELECT_MSG);
        packet.WriteBytes(Convert.FromHexString(LargeDialogFixture)[..82]);
        packet.WriteShort(256);
        for (var index = 0; index < 256; index++)
        {
            packet.WriteInt(-1);
            packet.WriteUtf8String($"Topic {index}");
        }
        var dialog = NpcDialog.Read(packet);
        Assert.Equal(256, dialog.Buttons.Count);
        Assert.Equal((255, -1, "Topic 255"), dialog.Buttons[^1]);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(257)]
    public void InvalidLargeMenuCountsAreRejected(int count)
    {
        var bytes = Convert.FromHexString(LargeDialogFixture);
        bytes[82] = (byte)count;
        bytes[83] = (byte)(count >> 8);
        Assert.Throws<InvalidDataException>(() => NpcDialog.Read(FromHex(Convert.ToHexString(bytes))));
    }

    [Fact]
    public void ServerInlineButtonsSurviveWithoutBakedTextIds()
    {
        var dialog = NpcDialog.Read(FromHex(DialogFixture));
        Assert.Equal("Choose", dialog.HeaderText);
        Assert.Equal("a.quest", dialog.ScriptFile);
        Assert.Equal(2, dialog.Buttons.Count);
        Assert.Equal((0, -1, "Sword"), dialog.Buttons[0]);
        Assert.Equal((1, 10, ""), dialog.Buttons[1]);
    }

    [Fact]
    public void InlineSpeechHasNoDialogHeaderField()
    {
        var packet = FromHex(SpeechFixture);
        for (var index = 0; index < 10; index++) packet.ReadInt();
        var lines = NpcDialog.ReadText(packet, false, out var header);
        Assert.Null(header);
        Assert.Equal(new[] { "Hola", "Bye" }, lines);
        Assert.Equal(0, packet.RemainingBytes);
    }

    [Fact]
    public void TruncatedInlineTextIsRejected()
    {
        Assert.Throws<InvalidDataException>(() => NpcDialog.Read(FromHex(DialogFixture[..^2])));
    }
}
