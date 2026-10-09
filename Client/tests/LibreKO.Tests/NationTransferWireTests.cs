using LibreKO.Domain;
using LibreKO.Network;
using Xunit;

namespace LibreKO.Tests;

public class NationTransferWireTests
{
    private const int KarusRogue = 108;
    private const int KarusPriest = 111;
    private const int KarusTuarek = 2;
    private const int KarusPuriTuarek = 4;
    private const int ElMoradMale = 12;
    private const byte UnknownResult = 200;
    private const int InInvestigationTeamText = 16704;
    private const int MakeOverActiveText = 18906;
    private const int NotEnoughCoinsText = 11303;
    private const int InClanText = 16702;
    private const int NoItemText = 16710;

    private static Packet Candidates(params (short Slot, string Name, byte Race, byte Nation, short Class)[] rows)
    {
        var p = new Packet(GameOpcodes.GS_NATION_TRANSFER);
        p.WriteByte((byte)rows.Length);
        foreach (var row in rows)
        {
            p.WriteShort(row.Slot);
            p.WriteString(row.Name);
            p.WriteByte(row.Race);
            p.WriteByte(row.Nation);
            p.WriteShort(row.Class);
            p.WriteByte(1);
            p.WriteInt(1);
        }
        p.ResetOffset();
        return p;
    }

    private static readonly (short, string, byte, byte, short) Rover = (0, "Rover", KarusTuarek, Nations.Karus, KarusRogue);
    private static readonly (short, string, byte, byte, short) Healer = (1, "Healer", KarusPuriTuarek, Nations.Karus, KarusPriest);

    [Fact]
    public void EveryCandidateOfTheAccountIsRead()
    {
        var list = NationTransferWire.ReadCandidates(Candidates(Rover, Healer));

        Assert.NotNull(list);
        Assert.Equal(["Rover", "Healer"], list!.Select(candidate => candidate.Name));
        Assert.All(list, candidate => Assert.Equal(Nations.Karus, candidate.Nation));
    }

    [Fact]
    public void ATruncatedOrPaddedListIsRejected()
    {
        var data = Candidates(Rover, Healer).GetData();
        for (int length = 0; length < data.Length; length++)
        {
            var p = new Packet(GameOpcodes.GS_NATION_TRANSFER);
            p.WriteBytes(data[..length]);
            p.ResetOffset();
            Assert.Null(NationTransferWire.ReadCandidates(p));
        }
        var padded = new Packet(GameOpcodes.GS_NATION_TRANSFER);
        padded.WriteBytes([.. data, 0]);
        padded.ResetOffset();
        Assert.Null(NationTransferWire.ReadCandidates(padded));
    }

    [Fact]
    public void InconsistentCandidatesAreRejected()
    {
        Assert.Null(NationTransferWire.ReadCandidates(Candidates(Rover, Rover)));
        Assert.Null(NationTransferWire.ReadCandidates(Candidates(Rover, (1, "rover", KarusPuriTuarek, Nations.Karus, KarusPriest))));
        Assert.Null(NationTransferWire.ReadCandidates(Candidates((0, "Rover", KarusTuarek, Nations.ElMorad, KarusRogue))));
        Assert.Null(NationTransferWire.ReadCandidates(Candidates((0, "Rover", ElMoradMale, Nations.Karus, KarusRogue))));
        Assert.Null(NationTransferWire.ReadCandidates(Candidates((0, "", KarusTuarek, Nations.Karus, KarusRogue))));
    }

    [Fact]
    public void PicksMustCoverEveryCandidateWithABodyItsClassAllows()
    {
        var candidates = NationTransferWire.ReadCandidates(Candidates(Rover, Healer))!;
        var rover = new NationTransferPick(0, "Rover", KarusTuarek, 1, 1);
        var healer = new NationTransferPick(1, "Healer", KarusPuriTuarek, 1, 1);

        Assert.True(NationTransferWire.PicksMatch(candidates, [rover, healer]));
        Assert.False(NationTransferWire.PicksMatch(candidates, [rover]));
        Assert.False(NationTransferWire.PicksMatch(candidates, [rover, rover]));
        Assert.False(NationTransferWire.PicksMatch(candidates, [rover, healer with { Name = "Other" }]));
        Assert.False(NationTransferWire.PicksMatch(candidates, [rover, healer with { Race = ElMoradMale }]));
        Assert.False(NationTransferWire.PicksMatch(candidates, [rover, healer with { Face = byte.MaxValue + 1 }]));
    }

    [Fact]
    public void OnlyKnownRefusalsAreReported()
    {
        Assert.True(NationTransferWire.IsRefusal(NationTransferWire.NoItem));
        Assert.True(NationTransferWire.IsRefusal(NationTransferWire.InClan));
        Assert.False(NationTransferWire.IsRefusal(Net.NationTransferAccepted));
        Assert.False(NationTransferWire.IsRefusal(Net.NationTransferWarRunning));
    }

    private static (byte Sub, byte Result) Reply(byte sub, byte result)
    {
        var p = new Packet(GameOpcodes.GS_NATION_TRANSFER);
        p.WriteByte(sub);
        p.WriteByte(result);
        p.ResetOffset();
        return (p.ReadByte(), p.ReadByte());
    }

    [Theory]
    [InlineData(Net.NationTransferAccepted)]
    [InlineData(NationTransferWire.SubmitCompleted)]
    public void BothSuccessResultsOfTheSubmitReplyComplete(byte result)
    {
        var (sub, read) = Reply(Net.NationTransferSubmit, result);

        Assert.True(NationTransferWire.IsSubmitSuccess(read));
        Assert.False(NationTransferWire.IsRefusal(sub, read));
    }

    [Fact]
    public void TheInClanResultRefusesOnlyTheOpenReply()
    {
        var (sub, read) = Reply(Net.NationTransferOpenBox, NationTransferWire.InClan);

        Assert.True(NationTransferWire.IsRefusal(sub, read));
        Assert.Equal(InClanText, NationTransferWire.RefusalText(sub, read));
    }

    [Theory]
    [InlineData(NationTransferWire.InInvestigationTeam, InInvestigationTeamText)]
    [InlineData(NationTransferWire.MakeOverActive, MakeOverActiveText)]
    [InlineData(NationTransferWire.NotEnoughCoins, NotEnoughCoinsText)]
    [InlineData(NationTransferWire.NoItem, NoItemText)]
    [InlineData(NationTransferWire.Failed, NationTransferWire.FailedText)]
    [InlineData(Net.NationTransferWarRunning, NationTransferWire.FailedText)]
    [InlineData(UnknownResult, NationTransferWire.FailedText)]
    public void EveryOtherSubmitResultIsARefusalWithItsText(byte result, int text)
    {
        var (sub, read) = Reply(Net.NationTransferSubmit, result);

        Assert.False(NationTransferWire.IsSubmitSuccess(read));
        Assert.True(NationTransferWire.IsRefusal(sub, read));
        Assert.Equal(text, NationTransferWire.RefusalText(sub, read));
    }

    [Theory]
    [InlineData(UnknownResult)]
    [InlineData(NationTransferWire.MakeOverActive)]
    public void UnknownResultsOutsideTheSubmitReplyAreIgnored(byte result)
    {
        var (sub, read) = Reply(Net.NationTransferOpenBox, result);

        Assert.False(NationTransferWire.IsRefusal(sub, read));
    }
}
