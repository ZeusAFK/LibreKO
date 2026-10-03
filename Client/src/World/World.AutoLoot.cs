using System.Collections.Generic;
using LibreKO.Network;

namespace LibreKO;

public partial class World
{
    private const int ItemIdsPerBase = 1000;
    private const float AutoLootRange = 10f;
    private const float AutoLootRangeSq = AutoLootRange * AutoLootRange;
    private static readonly HashSet<int> AutoLootFairyBases = new() { 700039, 1700039, 1700040 };

    private readonly HashSet<int> _autoLootTried = new();
    private bool _autoLooting;

    private bool WearsAutoLootFairy()
    {
        var fairy = Inv[InventoryConstants.CosFairy];
        return !fairy.IsEmpty && AutoLootFairyBases.Contains(fairy.ItemId / ItemIdsPerBase);
    }

    private void AutoLootTick()
    {
        if (_self == null || _selfDead || _openBundleId >= 0 || _boxes.Count == 0 || !(WearsAutoLootFairy() || FamiliarLoots())) return;

        var me = _self.Position;
        foreach (var (id, box) in _boxes)
        {
            if (_autoLootTried.Contains(id) || box.BasePos.DistanceSquaredTo(me) > AutoLootRangeSq) continue;
            _autoLootTried.Add(id);
            _autoLooting = true;
            _openBundleId = id;
            Net.I.SendBundleOpen(id);
            return;
        }
    }

    private bool AutoLootNext()
    {
        if (!_autoLooting) return false;
        if (_lootEntries.Count == 0) return true;
        TakeLoot(0, _lootEntries[0].ItemId);
        return true;
    }

    private void StopAutoLoot()
    {
        if (!_autoLooting) return;
        _autoLooting = false;
        CloseLoot();
    }
}
