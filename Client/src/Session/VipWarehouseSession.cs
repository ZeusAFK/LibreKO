namespace LibreKO.Session;

public sealed class VipWarehouseSession
{
    public event Action<VipVaultContents>? VipWarehouseContentsEvent;

    internal void RaiseVipWarehouseContents(VipVaultContents vipVaultContents) => VipWarehouseContentsEvent?.Invoke(vipVaultContents);

    public event Action<VipWarehouseSubOpcode, VipWarehouseResult>? VipWarehouseResultEvent;

    internal void RaiseVipWarehouseResult(VipWarehouseSubOpcode arg1, VipWarehouseResult arg2) => VipWarehouseResultEvent?.Invoke(arg1, arg2);

    public event Action? VipWarehouseExpiredEvent;

    internal void RaiseVipWarehouseExpired() => VipWarehouseExpiredEvent?.Invoke();

    public event Action<VipVaultKeyReply>? VipVaultRenewedEvent;

    internal void RaiseVipVaultRenewed(VipVaultKeyReply vipVaultKeyReply) => VipVaultRenewedEvent?.Invoke(vipVaultKeyReply);

    public event Action? VipVaultKeyExpiredEvent;

    internal void RaiseVipVaultKeyExpired() => VipVaultKeyExpiredEvent?.Invoke();

    public event Action? VipWarehousePinPromptEvent;

    internal void RaiseVipWarehousePinPrompt() => VipWarehousePinPromptEvent?.Invoke();

    public event Action<VipWarehouseSubOpcode, bool>? VipWarehousePinResultEvent;

    internal void RaiseVipWarehousePinResult(VipWarehouseSubOpcode arg1, bool arg2) => VipWarehousePinResultEvent?.Invoke(arg1, arg2);

    public event Action<int>? VipWarehousePinRejectedEvent;

    internal void RaiseVipWarehousePinRejected(int value) => VipWarehousePinRejectedEvent?.Invoke(value);
}
