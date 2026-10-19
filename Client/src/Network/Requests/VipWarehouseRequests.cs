namespace LibreKO.Network.Requests;

public interface IVipWarehouseRequests
{
    void VipVaultUseKey(int itemId, byte bagPos);
    void Open();
    void Input(int itemId, byte srcInvPos, byte dstVipPos, int count);
    void Output(int itemId, byte srcVipPos, byte dstInvPos, int count);
    void MoveInside(int itemId, byte from, byte to);
    void SetPin(string pin);
    void ChangePin(string pin);
    void EnterPin(string pin);
    void CancelPin();
}

public sealed class VipWarehouseRequests : IVipWarehouseRequests
{
    private readonly IPacketSink sink;

    public VipWarehouseRequests(IPacketSink sink) => this.sink = sink;

    public void VipVaultUseKey(int itemId, byte bagPos)
    {
        var p = new Packet(GameOpcodes.GS_VIP_WAREHOUSE);
        p.WriteByte((byte)VipWarehouseSubOpcode.UseVault);
        p.WriteInt(0);
        p.WriteInt(itemId);
        p.WriteByte(0);
        p.WriteByte(bagPos);
        p.WriteByte(VipVaultContents.OwnStorage);
        sink.Send(p);
    }

    public void Open()
    {
        var p = new Packet(GameOpcodes.GS_VIP_WAREHOUSE);
        p.WriteByte((byte)VipWarehouseSubOpcode.Open);
        sink.Send(p);
    }

    public void Input(int itemId, byte srcInvPos, byte dstVipPos, int count)
            => SendVipWarehouseMove(VipWarehouseSubOpcode.Input, itemId, VipVaultContents.OwnStorage, srcInvPos, dstVipPos, count);

    public void Output(int itemId, byte srcVipPos, byte dstInvPos, int count)
            => SendVipWarehouseMove(VipWarehouseSubOpcode.Output, itemId, VipVaultContents.OwnStorage, srcVipPos, dstInvPos, count);

    public void MoveInside(int itemId, byte from, byte to)
    {
        var p = new Packet(GameOpcodes.GS_VIP_WAREHOUSE);
        p.WriteByte((byte)VipWarehouseSubOpcode.Store);
        p.WriteInt(0);
        p.WriteInt(itemId);
        p.WriteByte(VipVaultContents.OwnStorage);
        p.WriteByte(from);
        p.WriteByte(to);
        sink.Send(p);
    }

    public void SetPin(string pin) => SendVipWarehousePin(VipWarehouseSubOpcode.SetPassword, pin);

    public void ChangePin(string pin) => SendVipWarehousePin(VipWarehouseSubOpcode.ChangePassword, pin);

    public void EnterPin(string pin) => SendVipWarehousePin(VipWarehouseSubOpcode.EnterPassword, pin);

    public void CancelPin()
    {
        var p = new Packet(GameOpcodes.GS_VIP_WAREHOUSE);
        p.WriteByte((byte)VipWarehouseSubOpcode.CancelPassword);
        sink.Send(p);
    }

    private void SendVipWarehouseMove(VipWarehouseSubOpcode sub, int itemId, byte page, byte srcPos, byte dstPos, int count)
    {
        var p = new Packet(GameOpcodes.GS_VIP_WAREHOUSE);
        p.WriteByte((byte)sub);
        p.WriteInt(0);
        p.WriteInt(itemId);
        p.WriteByte(page);
        p.WriteByte(srcPos);
        p.WriteByte(dstPos);
        p.WriteInt(count);
        sink.Send(p);
    }

    private void SendVipWarehousePin(VipWarehouseSubOpcode sub, string pin)
    {
        var p = new Packet(GameOpcodes.GS_VIP_WAREHOUSE);
        p.WriteByte((byte)sub);
        p.WriteSByteString(pin);
        sink.Send(p);
    }
}
