using System;
using LibreKO.Domain;

namespace LibreKO.Network;

public static class CharacterRecordWire
{
    public const int SlotCount = 4;
    public const int NameChangeFaceOffset = 100;
    private const int HeaderBytes = 1 + 2 + 1 + 1 + 1 + 4 + 2;
    private const int GearBytes = 6;

    public static CharacterSummary Read(Packet p)
    {
        var ci = new CharacterSummary { Name = p.ReadString() };
        if (ci.Name.Length == 0)
        {
            Skip(p, HeaderBytes + InventoryConstants.VisualSlotCount * GearBytes);
            return ci;
        }
        ci.Race = p.ReadByte();
        ci.Class = p.ReadShort();
        ci.Level = p.ReadByte();
        ci.Rebirth = p.ReadByte();
        int face = p.ReadByte();
        ci.NameChangeRequired = face >= NameChangeFaceOffset;
        ci.Face = ci.NameChangeRequired ? face - NameChangeFaceOffset : face;
        ci.Hair = p.ReadUInt();
        ci.Zone = p.ReadUShort();
        ci.Gear = new int[InventoryConstants.VisualSlots.Length];
        for (int i = 0; i < InventoryConstants.VisualSlotCount; i++)
        {
            int item = p.ReadInt();
            p.ReadShort();
            if (i >= InventoryConstants.CharacterListVisualSlots.Length) continue;
            int equippedSlot = InventoryConstants.CharacterListVisualSlots[i];
            int visualIndex = Array.IndexOf(InventoryConstants.VisualSlots, equippedSlot);
            if (visualIndex >= 0)
                ci.Gear[visualIndex] = item;
        }
        return ci;
    }

    private static void Skip(Packet p, int n)
    {
        int take = Math.Min(n, p.RemainingBytes);
        if (take > 0) p.ReadBytes(take);
    }
}
