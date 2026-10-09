using LibreKO.Domain;
using Xunit;

namespace LibreKO.Tests;

public class BeautyShopTests
{
    private const int MakeoverCoupon = 810340000;
    private const int NoCouponText = 18901;
    private const int SucceededText = 18902;
    private const int FailedText = 18903;

    [Fact]
    public void TheMakeoverCouponPaysForTheChange()
    {
        Assert.Equal(MakeoverCoupon, BeautyShop.Coupon);
        Assert.Equal(NoCouponText, BeautyShop.NoCouponText);
    }

    [Theory]
    [InlineData(true, SucceededText)]
    [InlineData(false, FailedText)]
    public void TheResultUsesTheRetailText(bool succeeded, int text) =>
        Assert.Equal(text, BeautyShop.ResultText(succeeded));
}
