namespace LibreKO.Game.World;

public static class PvpRules
{
    public static bool CanAttackPlayer(UserSession attacker, UserSession target)
        => target.Hp > 0 && !target.IsBot && !target.IsInfiltrating && !target.IsBlinking && IsHostileTarget(attacker, target);

    public static bool IsEnemy(UserSession caster, UserSession target)
    {
        if (caster.MatchTeam != ClanMatchService.NoTeam)
            return target.MatchTeam != ClanMatchService.NoTeam && target.MatchTeam != caster.MatchTeam;
        if (caster.SiegeSide != SiegeSide.None)
            return CastleSiegeRules.Hostile(caster.SiegeSide, target.SiegeSide);
        return caster.IsInArena || target.IsInArena
            ? caster.ArenaId == target.ArenaId && caster.CharacterId != target.CharacterId
            : caster.Nation != target.Nation || AtClanWar(caster, target);
    }

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

        if (attacker.SiegeSide != SiegeSide.None)
            return CastleSiegeRules.Hostile(attacker.SiegeSide, target.SiegeSide);

        if (attacker.MatchTeam != ClanMatchService.NoTeam)
            return target.MatchTeam != ClanMatchService.NoTeam && target.MatchTeam != attacker.MatchTeam;

        if (BattleZoneManager.IsFreeForAllZone(attacker.ZoneId) || AtClanWar(attacker, target))
            return true;

        return attacker.Nation != target.Nation
            && BattleZoneManager.AllowsNationCombat(attacker.ZoneId);
    }

    private static bool AtClanWar(UserSession attacker, UserSession target)
        => attacker.ClanBattleEnemy > 0 && attacker.ClanBattleEnemy == target.KnightsId;

    private static bool SharesPartyWith(UserSession attacker, UserSession target)
        => attacker.IsInParty && attacker.PartyIndex == target.PartyIndex;
}
