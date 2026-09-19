using LibreKO.Network;
using Xunit;

namespace LibreKO.Tests;

public class QuestObjectivesTests
{
    private const string Fixture = "0E090301020700026400000065000000030001C8000000";

    private static Packet PacketFrom(string hex)
    {
        var packet = new Packet(GameOpcodes.GS_QUEST);
        packet.WriteBytes(Convert.FromHexString(hex));
        Assert.Equal((byte)QuestSub.Objectives, packet.ReadByte());
        return packet;
    }

    [Fact]
    public void ServerObjectivePacketPreservesGroupsAndCompletionRule()
    {
        var data = QuestObjectives.Read(PacketFrom(Fixture));
        Assert.Equal(777, data.QuestId);
        Assert.True(data.AnyWillDo);
        Assert.Equal(7, data.Groups[0].Count);
        Assert.Equal(new[] { 100, 101 }, data.Groups[0].Monsters);
        Assert.Equal(3, data.Groups[1].Count);
        Assert.Equal(new[] { 200 }, data.Groups[1].Monsters);
    }

    [Fact]
    public void AnyObjectiveProgressUsesTheMostCompleteGroup()
    {
        var data = QuestObjectives.Read(PacketFrom(Fixture));
        Assert.Equal((3, 3), data.Progress(new ushort[] { 1, 3 }));
        Assert.Equal((1, 3), data.Progress(new ushort[] { 1, 1 }));
        Assert.Equal((0, 7), data.Progress(Array.Empty<ushort>()));
        Assert.Equal((7, 7), data.Progress(new ushort[] { 99, 1 }));
        Assert.Equal((4, 10), (data with { AnyWillDo = false }).Progress(new ushort[] { 1, 3 }));
    }

    [Theory]
    [InlineData("0E09030105")]
    [InlineData("0E09030201")]
    [InlineData("0E0903010100000164000000")]
    [InlineData("0E0903010107000564000000")]
    [InlineData("0E090301010700016400")]
    public void MalformedOrTruncatedObjectivesAreRejected(string hex)
    {
        Assert.Throws<InvalidDataException>(() => QuestObjectives.Read(PacketFrom(hex)));
    }
}
