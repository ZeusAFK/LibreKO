namespace LibreKO.Domain;

public static class BeautyShop
{
    public const int Coupon = 810340000;
    public const int NoCouponText = 18901;
    public const int SucceededText = 18902;
    public const int FailedText = 18903;

    public static int ResultText(bool succeeded) => succeeded ? SucceededText : FailedText;

    public static bool HasCoupon(Inventory inventory)
    {
        for (int abs = Inventory.GridStart; abs < inventory.Length; abs++)
            if (inventory.Holds(abs, Coupon)) return true;
        return false;
    }
}
