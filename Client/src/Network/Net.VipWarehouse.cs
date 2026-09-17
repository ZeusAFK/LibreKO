using System;
using LibreKO.Domain;

namespace LibreKO.Network;

public partial class Net
{
    public const int VipWarehouseSlots = 48;
    public const int VipWarehousePageSize = 12;

    private const byte VipWhOpen = 0x01;
    private const byte VipWhInput = 0x02;
    private const byte VipWhOutput = 0x03;
    private const byte VipWhMove = 0x04;
    public const byte VipWhSetPinSub = 0x08;
    public const byte VipWhCancelPinSub = 0x09;
    public const byte VipWhChangePinSub = 0x0A;
    public const byte VipWhEnterPinSub = 0x0B;

    public const byte VipFail = 0;
    public const byte VipSuccess = 1;
    public const byte VipDuplicate = 2;
    public const byte VipExpired = 3;

    public event Action<int, ItemSlot[]>? VipWarehouseContentsEvent;

    public event Action<byte, bool>? VipWarehouseResultEvent;

    public event Action? VipWarehouseExpiredEvent;

    public event Action? VipWarehousePinPromptEvent;

    public event Action<byte, bool>? VipWarehousePinResultEvent;

    private void HandleVipWarehouse(Packet p)
    {
        if (p.RemainingBytes < 1) return;
        byte sub = p.ReadByte();
        switch (sub)
        {
            case VipWhOpen:
            {
                byte result = p.RemainingBytes >= 1 ? p.ReadByte() : VipFail;
                if (result == VipExpired) { VipWarehouseExpiredEvent?.Invoke(); break; }
                if (result != VipSuccess) break;
                int remaining = p.RemainingBytes >= 4 ? p.ReadInt() : 0;
                var slots = new ItemSlot[VipWarehouseSlots];
                for (int i = 0; i < VipWarehouseSlots && p.RemainingBytes >= 17; i++)
                {
                    int itemId = p.ReadInt();
                    short dur = UShortToShort(p.ReadUShort());
                    short count = UShortToShort(p.ReadUShort());
                    p.ReadByte();
                    SkipBytes(p, 8);
                    slots[i] = itemId == 0 ? default : new ItemSlot { ItemId = itemId, Durability = dur, Count = count };
                }
                VipWarehouseContentsEvent?.Invoke(remaining, slots);
                break;
            }
            case VipWhInput: case VipWhOutput: case VipWhMove:
            {
                bool ok = p.RemainingBytes >= 1 && p.ReadByte() == VipSuccess;
                VipWarehouseResultEvent?.Invoke(sub, ok);
                break;
            }
            case VipWhSetPinSub: case VipWhCancelPinSub: case VipWhChangePinSub:
            {
                bool ok = p.RemainingBytes >= 1 && p.ReadByte() == VipSuccess;
                VipWarehousePinResultEvent?.Invoke(sub, ok);
                break;
            }
            case VipWhEnterPinSub:
            {
                byte result = p.RemainingBytes >= 1 ? p.ReadByte() : VipFail;
                if (result == VipSuccess)
                {
                    if (p.RemainingBytes >= 1) { p.ReadByte(); VipWarehousePinResultEvent?.Invoke(VipWhEnterPinSub, true); }
                    else VipWarehousePinPromptEvent?.Invoke();
                }
                else VipWarehousePinResultEvent?.Invoke(VipWhEnterPinSub, false);
                break;
            }
        }
    }

    public void SendVipWarehouseOpen()
    {
        var p = new Packet(GameOpcodes.GS_VIP_WAREHOUSE);
        p.WriteByte(VipWhOpen);
        _conn.Send(p);
    }

    public void SendVipWarehouseInput(int itemId, byte page, byte srcInvPos, byte dstVipPos, int count)
        => SendVipWarehouseMove(VipWhInput, itemId, page, srcInvPos, dstVipPos, count);

    public void SendVipWarehouseOutput(int itemId, byte page, byte srcVipPos, byte dstInvPos, int count)
        => SendVipWarehouseMove(VipWhOutput, itemId, page, srcVipPos, dstInvPos, count);

    private void SendVipWarehouseMove(byte sub, int itemId, byte page, byte srcPos, byte dstPos, int count)
    {
        var p = new Packet(GameOpcodes.GS_VIP_WAREHOUSE);
        p.WriteByte(sub);
        p.WriteInt(0);
        p.WriteInt(itemId);
        p.WriteByte(page);
        p.WriteByte(srcPos);
        p.WriteByte(dstPos);
        p.WriteInt(count);
        _conn.Send(p);
    }

    public void SendVipWarehouseSetPin(string pin) => SendVipWarehousePin(VipWhSetPinSub, pin);

    public void SendVipWarehouseChangePin(string pin) => SendVipWarehousePin(VipWhChangePinSub, pin);

    public void SendVipWarehouseEnterPin(string pin) => SendVipWarehousePin(VipWhEnterPinSub, pin);

    private void SendVipWarehousePin(byte sub, string pin)
    {
        var p = new Packet(GameOpcodes.GS_VIP_WAREHOUSE);
        p.WriteByte(sub);
        p.WriteSByteString(pin);
        _conn.Send(p);
    }

    public void SendVipWarehouseCancelPin()
    {
        var p = new Packet(GameOpcodes.GS_VIP_WAREHOUSE);
        p.WriteByte(VipWhCancelPinSub);
        _conn.Send(p);
    }
}
