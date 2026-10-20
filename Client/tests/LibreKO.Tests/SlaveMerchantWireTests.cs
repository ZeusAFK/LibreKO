using LibreKO.Domain;
using LibreKO.Network;
using LibreKO.Session;
using Xunit;

namespace LibreKO.Tests;

public class SlaveMerchantWireTests
{
    private const int Scroll = 379080000;
    private const int StallSlots = 12;

    private static Packet Reply(System.Action<Packet> write)
    {
        var p = new Packet(GameOpcodes.GS_SLAVE_MERCHANT);
        write(p);
        p.ResetOffset();
        return p;
    }

    [Fact]
    public void TheStatusCarriesTheStallTheCoinsAndTheHoursLeft()
    {
        var p = Reply(w =>
        {
            w.WriteByte(1);
            w.WriteUShort(23);
            w.WriteUInt(4000);
            w.WriteByte(StallSlots);
            w.WriteInt(Scroll);
            w.WriteUShort(3);
            w.WriteInt(2000);
            for (int i = 1; i < StallSlots; i++) { w.WriteInt(0); w.WriteUShort(0); w.WriteInt(0); }
        });

        var status = SlaveMerchantWire.ReadStatus(p)!;

        Assert.True(status.Open);
        Assert.Equal(23, status.Hours);
        Assert.Equal(4000, status.Coins);
        Assert.Equal(StallSlots, status.Lines.Length);
        Assert.Equal(new SlaveMerchantLine(Scroll, 3, 2000), status.Lines[0]);
        Assert.True(status.Lines[1].IsEmpty);
        Assert.Equal(0, p.RemainingBytes);
    }

    [Fact]
    public void ACollectionSaysWhatCameBackAndWhatStayed()
    {
        var p = Reply(w => { w.WriteByte(1); w.WriteUInt(1000); w.WriteByte(2); w.WriteByte(1); w.WriteUInt(3000); });

        Assert.Equal(new SlaveMerchantCollected(true, 1000, 2, 1, 3000), SlaveMerchantWire.ReadCollected(p));
    }

    [Fact]
    public void ACollectionThatFailedSaysSo()
    {
        var p = Reply(w => { w.WriteByte(0); w.WriteUInt(0); w.WriteByte(0); w.WriteByte(0); w.WriteUInt(0); });

        Assert.Equal(new SlaveMerchantCollected(false, 0, 0, 0, 0), SlaveMerchantWire.ReadCollected(p));
    }

    [Fact]
    public void TheSmallRepliesReadTheirFields()
    {
        Assert.Equal(new SlaveMerchantRedeem(true, 744), SlaveMerchantWire.ReadRedeem(Reply(w => { w.WriteByte(1); w.WriteUShort(744); })));
        Assert.Equal(5, SlaveMerchantWire.ReadTime(Reply(w => w.WriteUShort(5))));
        Assert.Equal(SlaveMerchantDeployResult.NoTime, SlaveMerchantWire.ReadDeploy(Reply(w => w.WriteByte(2))));
        Assert.Equal(SlaveMerchantDeployResult.Failed, SlaveMerchantWire.ReadDeploy(Reply(w => w.WriteByte(5))));
        Assert.Equal(new SlaveMerchantSale(Scroll, 2, 4000), SlaveMerchantWire.ReadSale(Reply(w => { w.WriteInt(Scroll); w.WriteUShort(2); w.WriteUInt(4000); })));
        Assert.Equal(SlaveMerchantClosedReason.OutOfTime, SlaveMerchantWire.ReadClosed(Reply(w => w.WriteByte(2))));
    }

    [Fact]
    public void AShortReplyIsIgnored()
    {
        Assert.Null(SlaveMerchantWire.ReadStatus(Reply(w => w.WriteByte(1))));
        Assert.Null(SlaveMerchantWire.ReadCollected(Reply(w => w.WriteUInt(1))));
        Assert.Null(SlaveMerchantWire.ReadTime(Reply(w => w.WriteByte(1))));
    }

    [Fact]
    public void TheSessionKeepsTheHoursFromEveryReplyThatCarriesThem()
    {
        var session = new SlaveMerchantSession();
        Assert.Equal(SlaveMerchantSession.UnknownHours, session.Hours);

        session.ApplyStatus(new SlaveMerchantStatus(false, 10, 0, System.Array.Empty<SlaveMerchantLine>()));
        Assert.Equal(10, session.Hours);

        session.ApplyRedeem(new SlaveMerchantRedeem(true, 34));
        Assert.Equal(34, session.Hours);

        session.ApplyHours(33);
        Assert.Equal(33, session.Hours);

        session.Clear();
        Assert.Equal(SlaveMerchantSession.UnknownHours, session.Hours);
        Assert.Null(session.Status);
    }

    [Theory]
    [InlineData(SlaveMerchant.OneDayCertificate, 24)]
    [InlineData(SlaveMerchant.ThirtyDayCertificate, 720)]
    [InlineData(Scroll, 0)]
    public void ACertificateAddsItsHours(int itemId, int hours)
    {
        Assert.Equal(hours, SlaveMerchant.HoursOf(itemId));
        Assert.Equal(hours > 0, SlaveMerchant.IsCertificate(itemId));
    }

    [Fact]
    public void OnlyAKnownEmptyBalanceRefusesTheStall()
    {
        Assert.True(SlaveMerchant.HasNoTime(0));
        Assert.False(SlaveMerchant.HasNoTime(SlaveMerchantSession.UnknownHours));
        Assert.False(SlaveMerchant.HasNoTime(1));
    }
}
