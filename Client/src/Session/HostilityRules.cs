namespace LibreKO.Session;

public sealed class HostilityRules(SelfSession self, SiegeSession siege, ClanSession clan, ClanBattleWarSession clanBattleWar,
    ZoneAbilitySession zoneAbility, EntitiesSession entities)
{
    public bool IsHostilePlayer(EntitySnapshot e) =>
        !e.IsNpc && e.Id != self.MyCharId
        && (IsHostilePlayer(e.Nation, e.KnightsId, e.AllianceId) || AtClanWarWith(e));

    public bool IsHostilePlayer(int nation, int clanId, int allianceId) =>
        Domain.SiegeWar.At(self.LastEnter.Zone, siege.SiegeWar)
            ? Domain.SiegeWar.Hostile(MyClanId, MyAllianceId, clanId, allianceId, siege.SiegeWar)
            : zoneAbility.CurrentZoneAbility.IsHostilePlayer(self.Nation, nation);

    public bool IsHostileNpc(EntitySnapshot e) =>
        NpcHostility.IsHostile(e, self.Nation, zoneAbility.CurrentZoneAbility.NpcsAreTargets);

    public bool AtClanWarWith(EntitySnapshot e) => AtClanWarWith(e.KnightsId);

    public bool AtClanWarWith(int clanId) => clanBattleWar.EnemyClanId > 0 && clanId == clanBattleWar.EnemyClanId;

    public int SiegeChatMask(int senderId, bool senderIsGm)
    {
        if (senderIsGm || senderId == self.MyCharId || !Domain.SiegeWar.At(self.LastEnter.Zone, siege.SiegeWar)) return 0;
        entities.TryGet(senderId, out var sender);
        return Domain.SiegeWar.TargetMaskText(self.IsGm, MyClanId, MyAllianceId, sender?.KnightsId ?? 0, sender?.AllianceId ?? 0, siege.SiegeWar);
    }

    public void RefreshKnownHostility()
    {
        foreach (var known in entities.All)
            known.Attackable = known.IsNpc ? IsHostileNpc(known) : IsHostilePlayer(known);
    }

    private int MyClanId => clan.MyClan.InClan ? clan.MyClan.ClanId : 0;

    private int MyAllianceId => clan.MyClan.InClan ? clan.MyClan.AllianceId : 0;
}
