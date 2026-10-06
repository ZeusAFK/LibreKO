using System;
using System.Collections.Generic;
using LibreKO.Domain;

namespace LibreKO.Network;

public partial class Net
{
    public const byte ItemCombineSub = 11;
    private const byte CombineEffectSub = 1;

    public event Action<CombineReply>? ItemCombineReplyEvent;
    public event Action<CombineEffect>? CombineEffectEvent;

    public void SendItemCombine(int npcRuntimeId, int shadowItem, int shadowBagSlot, IReadOnlyList<CombineEntry> ordered)
    {
        var p = new Packet(GameOpcodes.GS_ITEM_UPGRADE);
        p.WriteByte(ItemCombineSub);
        p.WriteInt(npcRuntimeId);
        p.WriteInt(shadowItem);
        p.WriteByte((byte)shadowBagSlot);
        p.WriteByte((byte)ordered.Count);
        foreach (var entry in ordered)
            p.WriteByte((byte)entry.BagSlot);
        p.WriteSByteString(ItemCombine.MaterialText(ordered));
        _conn.Send(p);
    }

    private void HandleItemCombineReply(Packet p)
    {
        byte result = p.ReadByte();
        short row = 0;
        byte slot = 0;
        if (ItemCombine.IsDone(result))
        {
            row = p.ReadShort();
            if (result == ItemCombine.Succeeded)
            {
                p.ReadInt();
                slot = p.ReadByte();
            }
        }
        ItemCombineReplyEvent?.Invoke(new CombineReply(result, row, slot));
    }

    private void HandleNpcEvent(Packet p)
    {
        if (p.RemainingBytes < 1 || p.ReadByte() != CombineEffectSub) return;
        bool success = p.ReadByte() == ItemCombine.Succeeded;
        int npc = p.ReadShort();
        int row = p.ReadUShort();
        CombineEffectEvent?.Invoke(new CombineEffect(success, npc, row));
    }
}
