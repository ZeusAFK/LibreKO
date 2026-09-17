using System;
using System.Collections.Generic;

namespace LibreKO.Network;

public partial class Net
{
    public readonly record struct WarpListEntry(
        int WarpId, string Name, string Announce, int ZoneId, int MaxUsers, long Fee);

    public event Action<List<WarpListEntry>>? WarpListEvent;

    public event Action? WarpFailEvent;

    public const byte WarpListMenu = 1;
    public const byte WarpListResult = 2;

    public const byte WarpResultArrived = 1;
    public const byte WarpResultLevelTooLow = 2;
    public const byte WarpResultCastleSiege = 3;
    public const byte WarpResultNoNationalPoints = 5;
    public const byte WarpResultLevelRangeOnly = 6;
    public const byte WarpResultNotQualified = 7;
    public const byte WarpResultTradeCooldown = 8;
    public const byte WarpResultServerFull = 9;

    private void HandleWarpList(Packet p)
    {
        if (p.RemainingBytes < 1) return;
        int kind = p.ReadByte();
        if (kind == WarpListResult)
        {
            byte result = p.RemainingBytes >= 1 ? p.ReadByte() : WarpResultArrived;
            if (result != WarpResultArrived) WarpFailEvent?.Invoke();
            return;
        }
        if (kind != WarpListMenu)
        {
            WarpFailEvent?.Invoke();
            return;
        }

        if (p.RemainingBytes < 2) { WarpListEvent?.Invoke(new List<WarpListEntry>()); return; }
        int count = p.ReadShort();
        var list = new List<WarpListEntry>(count > 0 ? count : 0);
        for (int i = 0; i < count; i++)
        {
            if (p.RemainingBytes < 2) break;
            int warpId = p.ReadShort();
            string name = p.ReadString();
            string announce = p.ReadString();
            if (p.RemainingBytes < 8) break;
            int zoneId = p.ReadShort();
            int maxUsers = p.ReadShort();
            long fee = p.ReadUInt();
            list.Add(new WarpListEntry(warpId, name, announce, zoneId, maxUsers, fee));
        }
        WarpListEvent?.Invoke(list);
    }

    public void SendWarpListRequest(int npcId)
    {
        var p = new Packet(GameOpcodes.GS_WARP_LIST);
        p.WriteShort((short)npcId);
        _conn.Send(p);
    }

    public void SendWarpSelect(int npcId, int warpId)
    {
        var p = new Packet(GameOpcodes.GS_WARP_LIST);
        p.WriteShort((short)npcId);
        p.WriteShort((short)warpId);
        _conn.Send(p);
    }
}
