namespace LibreKO.Network.Readers;

internal sealed class VipWarehouseReader : IPacketReader
{
    private readonly VipWarehouseSession vipWarehouse;

    public VipWarehouseReader(VipWarehouseSession vipWarehouse)
    {
        this.vipWarehouse = vipWarehouse;
    }

    public void Register(PacketDispatcher dispatcher)
    {
        dispatcher.On(GameOpcodes.GS_VIP_WAREHOUSE, HandleVipWarehouse);
    }

    private void HandleVipWarehouse(Packet p)
    {
        if (p.RemainingBytes < 1) return;
        var sub = (VipWarehouseSubOpcode)p.ReadByte();
        switch (sub)
        {
            case VipWarehouseSubOpcode.Open:
                {
                    var result = p.RemainingBytes >= 1 ? (VipWarehouseResult)p.ReadByte() : VipWarehouseResult.Failed;
                    if (result == VipWarehouseResult.Expired) { vipWarehouse.RaiseVipWarehouseExpired(); break; }
                    if (result != VipWarehouseResult.Succeeded) break;
                    if (VipVaultContents.Read(p, VipWarehouseWire.Slots) is { } contents)
                        vipWarehouse.RaiseVipWarehouseContents(contents);
                    break;
                }
            case VipWarehouseSubOpcode.Input:
            case VipWarehouseSubOpcode.Output:
            case VipWarehouseSubOpcode.Store:
                vipWarehouse.RaiseVipWarehouseResult(sub, p.RemainingBytes >= 1 ? (VipWarehouseResult)p.ReadByte() : VipWarehouseResult.Failed);
                break;
            case VipWarehouseSubOpcode.UseVault:
                if (VipVaultKeyReply.Read(p) is { } reply) vipWarehouse.RaiseVipVaultRenewed(reply);
                break;
            case VipWarehouseSubOpcode.KeyExpired:
                vipWarehouse.RaiseVipVaultKeyExpired();
                break;
            case VipWarehouseSubOpcode.SetPassword:
            case VipWarehouseSubOpcode.CancelPassword:
            case VipWarehouseSubOpcode.ChangePassword:
                {
                    bool ok = p.RemainingBytes >= 1 && (VipWarehouseResult)p.ReadByte() == VipWarehouseResult.Succeeded;
                    vipWarehouse.RaiseVipWarehousePinResult(sub, ok);
                    break;
                }
            case VipWarehouseSubOpcode.EnterPassword:
                {
                    var result = p.RemainingBytes >= 1 ? (VipWarehouseResult)p.ReadByte() : VipWarehouseResult.Failed;
                    if (result == VipWarehouseResult.Succeeded)
                    {
                        if (p.RemainingBytes >= 1) { p.ReadByte(); vipWarehouse.RaiseVipWarehousePinResult(VipWarehouseSubOpcode.EnterPassword, true); }
                        else vipWarehouse.RaiseVipWarehousePinPrompt();
                    }
                    else if (result == VipWarehouseResult.Rejected && p.RemainingBytes >= 1) vipWarehouse.RaiseVipWarehousePinRejected(p.ReadByte());
                    else vipWarehouse.RaiseVipWarehousePinResult(VipWarehouseSubOpcode.EnterPassword, false);
                    break;
                }
        }
    }
}
