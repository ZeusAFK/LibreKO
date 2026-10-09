using LibreKO.Domain;
using LibreKO.Network;
using Xunit;

namespace LibreKO.Tests;

public class RedistributionRequestTests
{
    private const byte Stat = Net.ResetKindStat, Mastery = Net.ResetKindSkill;

    [Fact]
    public void ASecondRequestIsRefusedWhileOneIsPending()
    {
        var request = new RedistributionRequest();

        Assert.True(request.TryBegin(Stat));
        Assert.False(request.TryBegin(Mastery));
        Assert.Equal(Stat, request.Kind);
    }

    [Fact]
    public void NoKindIsNeverARequest()
    {
        var request = new RedistributionRequest();

        Assert.False(request.TryBegin(RedistributionRequest.NoKind));
        Assert.False(request.Pending);
    }

    [Fact]
    public void OnlyOneCostReplyIsTakenPerRequest()
    {
        var request = new RedistributionRequest();
        Assert.False(request.TakeCost());

        request.TryBegin(Mastery);
        Assert.False(request.CanConfirm);
        Assert.True(request.TakeCost());
        Assert.True(request.CanConfirm);
        Assert.False(request.TakeCost());
    }

    [Fact]
    public void ClearingAllowsTheNextRequest()
    {
        var request = new RedistributionRequest();
        request.TryBegin(Stat);
        request.TakeCost();

        request.Clear();

        Assert.False(request.Pending);
        Assert.False(request.CanConfirm);
        Assert.True(request.TryBegin(Mastery));
    }
}
