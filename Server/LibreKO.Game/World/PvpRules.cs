using LibreKO.Common.Enums;

namespace LibreKO.Game.World;

public static class PvpRules
{
    public static bool CanAttackPlayer(UserSession attacker, UserSession target)
        => target.Hp > 0 && !target.IsBot && !target.IsInfiltrating && !target.IsBlinking
            && !ZoneSafetyAreas.IsInEnemySafetyArea(attacker) && !ZoneSafetyAreas.IsInOwnSafetyArea(target)
            && IsHostile(attacker, target);

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

    public static bool IsHostile(UserSession attacker, UserSession target)
    {
        if (attacker.CharacterId == target.CharacterId || attacker.ZoneId != target.ZoneId)
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

    // One rule for who an aimed skill may touch, both before the cast is paid and when it lands.
    public static bool MayTouch(UserSession caster, UserSession target, SkillMoral moral, bool healStrikes) => moral switch
    {
        SkillMoral.Enemy or SkillMoral.AreaEnemy or SkillMoral.Npc => CanAttackPlayer(caster, target),
        SkillMoral.Party or SkillMoral.PartyAll => caster.IsInParty && target.PartyIndex == caster.PartyIndex,
        SkillMoral.FriendWithMe or SkillMoral.FriendExceptMe or SkillMoral.AreaFriend or SkillMoral.Clan
            or SkillMoral.ClanAll or SkillMoral.CorpseFriend
            => !IsHostile(caster, target) || (healStrikes && CanAttackPlayer(caster, target)),
        _ => true,
    };

    public static SkillMoral CureMoral(SkillMoral moral, bool liftsBlessings) => moral switch
    {
        SkillMoral.None or SkillMoral.All or SkillMoral.AreaAll or SkillMoral.SelfArea
            => liftsBlessings ? SkillMoral.Enemy : SkillMoral.FriendWithMe,
        _ => moral,
    };

    private static bool AtClanWar(UserSession attacker, UserSession target)
        => attacker.ClanBattleEnemy > 0 && attacker.ClanBattleEnemy == target.KnightsId;

    private static bool SharesPartyWith(UserSession attacker, UserSession target)
        => attacker.IsInParty && attacker.PartyIndex == target.PartyIndex;
}
