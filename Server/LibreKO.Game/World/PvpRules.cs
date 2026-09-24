namespace LibreKO.Game.World;

public static class PvpRules
{
    public static bool CanAttackPlayer(UserSession attacker, UserSession target)
        => target.Hp > 0 && !target.IsInfiltrating && IsHostileTarget(attacker, target);

    public static bool IsEnemy(UserSession caster, UserSession target)
        => caster.IsInArena || target.IsInArena
            ? caster.ArenaId == target.ArenaId && caster.CharacterId != target.CharacterId
            : caster.Nation != target.Nation;

    public static bool IsPenaltyFreeDeath(UserSession victim, UserSession killer)
        => victim.IsInArena || killer.IsInArena;

    private static bool IsHostileTarget(UserSession attacker, UserSession target)
    {
        if (attacker.CharacterId == target.CharacterId || attacker.ZoneId != target.ZoneId)
            return false;

        if (ZoneSafetyAreas.IsInEnemySafetyArea(attacker)
            || ZoneSafetyAreas.IsInOwnSafetyArea(target))
            return false;

        if (attacker.IsInArena || target.IsInArena)
        {
            return attacker.ArenaId == target.ArenaId
                && (attacker.ArenaId != ArenaZones.MoradonPartyArena || !SharesPartyWith(attacker, target));
        }

        if (BattleZoneManager.IsFreeForAllZone(attacker.ZoneId))
            return true;

        return attacker.Nation != target.Nation
            && BattleZoneManager.AllowsNationCombat(attacker.ZoneId);
    }

    private static bool SharesPartyWith(UserSession attacker, UserSession target)
        => attacker.IsInParty && attacker.PartyIndex == target.PartyIndex;
}
