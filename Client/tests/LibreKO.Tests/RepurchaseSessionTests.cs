using LibreKO.Domain;
using LibreKO.Network;
using Xunit;

namespace LibreKO.Tests;

public class RepurchaseSessionTests
{
    private static RepurchaseSession Opened()
    {
        var session = new RepurchaseSession();
        session.Request();
        session.Accept(RepurchaseReply.Open, ok: true);
        return session;
    }

    [Fact]
    public void AListOpensTheWindowOnlyAfterARequest()
    {
        var session = new RepurchaseSession();
        Assert.Equal(RepurchaseListing.Ignored, session.Accept(RepurchaseReply.Open, ok: true));

        session.Request();
        Assert.Equal(RepurchaseListing.Opened, session.Accept(RepurchaseReply.Open, ok: true));
        Assert.True(session.IsOpen);
    }

    [Fact]
    public void ARefusedListLeavesTheWindowClosed()
    {
        var session = new RepurchaseSession();
        session.Request();

        Assert.Equal(RepurchaseListing.Ignored, session.Accept(RepurchaseReply.Open, ok: false));
        Assert.False(session.IsOpen);
    }

    [Fact]
    public void ARefreshOnlyUpdatesAnOpenWindow()
    {
        Assert.Equal(RepurchaseListing.Ignored, new RepurchaseSession().Accept(RepurchaseReply.Refresh, ok: true));
        Assert.Equal(RepurchaseListing.Refreshed, Opened().Accept(RepurchaseReply.Refresh, ok: true));
    }

    [Fact]
    public void ClosingAnOpenWindowSendsTheCloseRequestOnce()
    {
        var session = Opened();

        Assert.True(session.Close());
        Assert.False(session.IsOpen);
        Assert.False(session.Close());
    }

    [Fact]
    public void ClosingBeforeTheListArrivesDropsTheRequest()
    {
        var session = new RepurchaseSession();
        session.Request();

        Assert.False(session.Close());
        Assert.Equal(RepurchaseListing.Ignored, session.Accept(RepurchaseReply.Open, ok: true));
    }
}
