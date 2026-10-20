namespace LibreKO.Session;

public sealed class SlaveMerchantSession
{
    public const int UnknownHours = -1;

    public int Hours { get; private set; } = UnknownHours;

    public SlaveMerchantStatus? Status { get; private set; }

    public event Action<SlaveMerchantStatus>? StatusEvent;
    public event Action<int>? HoursEvent;
    public event Action<SlaveMerchantRedeem>? RedeemEvent;
    public event Action<SlaveMerchantDeployResult>? DeployEvent;
    public event Action<SlaveMerchantCollected>? CollectEvent;
    public event Action<SlaveMerchantSale>? SaleEvent;
    public event Action<SlaveMerchantClosedReason>? ClosedEvent;

    internal void ApplyStatus(SlaveMerchantStatus status)
    {
        Status = status;
        Hours = status.Hours;
        StatusEvent?.Invoke(status);
        HoursEvent?.Invoke(Hours);
    }

    internal void ApplyHours(int hours)
    {
        Hours = hours;
        HoursEvent?.Invoke(hours);
    }

    internal void ApplyRedeem(SlaveMerchantRedeem redeem)
    {
        Hours = redeem.Hours;
        RedeemEvent?.Invoke(redeem);
        HoursEvent?.Invoke(Hours);
    }

    internal void RaiseDeploy(SlaveMerchantDeployResult result) => DeployEvent?.Invoke(result);

    internal void RaiseCollect(SlaveMerchantCollected collected) => CollectEvent?.Invoke(collected);

    internal void RaiseSale(SlaveMerchantSale sale) => SaleEvent?.Invoke(sale);

    internal void RaiseClosed(SlaveMerchantClosedReason reason) => ClosedEvent?.Invoke(reason);

    internal void Clear()
    {
        Hours = UnknownHours;
        Status = null;
    }
}
