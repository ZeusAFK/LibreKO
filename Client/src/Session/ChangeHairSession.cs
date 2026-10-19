namespace LibreKO.Session;

public sealed class ChangeHairSession
{
    public HairChangeRequest Request { get; } = new();

    public event Action<bool, int, int>? ChangeHairResultEvent;

    internal void RaiseChangeHairResult(bool arg1, int arg2, int arg3) => ChangeHairResultEvent?.Invoke(arg1, arg2, arg3);

    public event Action? BeautyShopEvent;

    internal void RaiseBeautyShop() => BeautyShopEvent?.Invoke();

    internal void CancelPending()
    {
        if (Request.TryFinish()) RaiseChangeHairResult(false, Request.Face, Request.Hair);
    }
}
