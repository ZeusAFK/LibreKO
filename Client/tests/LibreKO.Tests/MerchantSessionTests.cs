using System.Linq;
using LibreKO.Network;
using LibreKO.Session;
using Xunit;

namespace LibreKO.Tests;

public class MerchantSessionTests
{
    private const int Seller = 4021;
    private const int Buyer = 4022;

    [Fact]
    public void KnownStallsFollowRememberAndForget()
    {
        var merchant = new MerchantSession();
        merchant.RememberStall(new StallOwner { CharacterId = Seller, IsBuying = false });
        merchant.RememberStall(new StallOwner { CharacterId = Buyer, IsBuying = true });
        merchant.RememberStall(new StallOwner { CharacterId = Seller, IsBuying = true });

        Assert.Equal(2, merchant.KnownStalls.Count());
        Assert.True(merchant.KnownStalls.Single(s => s.CharacterId == Seller).IsBuying);

        merchant.ForgetStall(Buyer);
        Assert.Equal(new[] { Seller }, merchant.KnownStalls.Select(s => s.CharacterId));

        merchant.ForgetStalls();
        Assert.Empty(merchant.KnownStalls);
    }
}
