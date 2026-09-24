using System;

namespace LibreKO.Network;

public partial class Net
{
    public ZoneAbilityInfo CurrentZoneAbility { get; private set; } = new() { ZoneType = 0, Tariff = 10 };

    public event Action<ZoneAbilityInfo>? ZoneAbilityEvent;

    private void HandleZoneAbility(Packet p)
    {
        if (p.RemainingBytes < 6) return;
        p.ReadByte();
        bool canTrade = p.ReadByte() != 0;
        byte zoneType = p.ReadByte();
        bool canTalk = p.ReadByte() != 0;
        int tariff = p.ReadUShort();

        CurrentZoneAbility = new ZoneAbilityInfo
        {
            ZoneType = zoneType,
            Tariff = tariff,
            CanTrade = canTrade,
            CanTalk = canTalk,
        };
        foreach (var known in _known.Values)
            known.Attackable = known.IsNpc ? IsHostileNpc(known) : IsHostilePlayer(known);
        ZoneAbilityEvent?.Invoke(CurrentZoneAbility);
    }

    public bool IsHostilePlayer(EntitySnapshot e) =>
        !e.IsNpc && e.Id != MyCharId
        && CurrentZoneAbility.IsHostilePlayer(Nation, e.Nation);

    public bool IsHostileNpc(EntitySnapshot e) =>
        NpcHostility.IsHostile(e, Nation, CurrentZoneAbility.NpcsAreTargets);
}
