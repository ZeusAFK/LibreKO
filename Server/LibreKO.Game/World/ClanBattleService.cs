using System.Collections.Concurrent;
using LibreKO.Common.Domain.Entities;
using LibreKO.Common.Enums;
using LibreKO.Game.Protocol.Writers;
using Microsoft.Extensions.Hosting;

namespace LibreKO.Game.World;

public interface IClanBattleService
{
    Task ChallengeAsync(UserSession challenger, int targetId);
    Task AnswerAsync(UserSession leader, bool accept, int challengerId);
    Task SurrenderAsync(UserSession leader);
    Task RestoreAsync(UserSession session);
    short EnemyOf(short clanId);
}

public sealed class ClanBattleService(SessionManager sessionManager, TimeProvider? clock = null) : BackgroundService, IClanBattleService
{
    private readonly TimeProvider time = clock ?? TimeProvider.System;
    public const int BattleMinutes = 30;
    public const int ChallengeSeconds = 60;
    private const int CheckSeconds = 30;
    private static readonly byte[] TownZones = [21, 22, 23, 24, 25];

    private readonly record struct Battle(short ClanA, short ClanB, DateTimeOffset Ends);
    private readonly record struct Challenge(int ChallengerId, short ChallengerClan, short TargetClan, DateTimeOffset Expires);

    private readonly ConcurrentDictionary<short, Battle> battles = new();
    private readonly ConcurrentDictionary<int, Challenge> challenges = new();

    public short EnemyOf(short clanId) =>
        battles.TryGetValue(clanId, out var battle) ? (battle.ClanA == clanId ? battle.ClanB : battle.ClanA) : (short)0;

    public async Task ChallengeAsync(UserSession challenger, int targetId)
    {
        var refusal = Refusal(challenger, targetId, out var targetLeader, out var targetClan);
        if (refusal is { } result)
        {
            await challenger.Client.SendPacket(ClanBattlePacketWriter.Result(result));
            return;
        }

        challenges[targetLeader!.CharacterId] = new Challenge(
            challenger.CharacterId, challenger.KnightsId, targetClan!.Id, time.GetUtcNow().AddSeconds(ChallengeSeconds));
        await targetLeader.Client.SendPacket(ClanBattlePacketWriter.Challenge(challenger.CharacterId, challenger.Name));
    }

    private ClanBattleResult? Refusal(UserSession challenger, int targetId, out UserSession? targetLeader, out KnightsEntity? targetClan)
    {
        targetLeader = null;
        targetClan = null;
        var ownClan = sessionManager.Knights.GetClan(challenger.KnightsId);
        if (ownClan == null) return ClanBattleResult.NotClanMember;
        if (!IsLeader(challenger, ownClan)) return ClanBattleResult.NotLeader;
        var target = sessionManager.GetByCharacterId(targetId);
        if (target == null) return ClanBattleResult.Offline;
        if (target.Hp <= 0) return ClanBattleResult.Dead;
        if (target.Nation != challenger.Nation) return ClanBattleResult.OtherNation;
        targetClan = sessionManager.Knights.GetClan(target.KnightsId);
        if (targetClan == null || targetClan.Id == ownClan.Id) return ClanBattleResult.NotClanMember;
        if (target.ZoneId != challenger.ZoneId || Array.IndexOf(TownZones, challenger.ZoneId) >= 0)
            return ClanBattleResult.WrongZone;
        if (battles.ContainsKey(ownClan.Id) || battles.ContainsKey(targetClan.Id)) return ClanBattleResult.AlreadyAtWar;
        targetLeader = IsLeader(target, targetClan) ? target : sessionManager.GetByName(targetClan.Chief);
        return targetLeader == null ? ClanBattleResult.Offline : null;
    }

    public async Task AnswerAsync(UserSession leader, bool accept, int challengerId)
    {
        if (!challenges.TryRemove(leader.CharacterId, out var challenge)
            || challenge.ChallengerId != challengerId
            || challenge.Expires < time.GetUtcNow())
            return;

        var challenger = sessionManager.GetByCharacterId(challengerId);
        if (!accept)
        {
            if (challenger != null)
                await challenger.Client.SendPacket(ClanBattlePacketWriter.Result(ClanBattleResult.Declined));
            return;
        }

        if (battles.ContainsKey(challenge.ChallengerClan) || battles.ContainsKey(challenge.TargetClan))
        {
            await leader.Client.SendPacket(ClanBattlePacketWriter.Result(ClanBattleResult.AlreadyAtWar));
            return;
        }

        var battle = new Battle(challenge.ChallengerClan, challenge.TargetClan, time.GetUtcNow().AddMinutes(BattleMinutes));
        battles[battle.ClanA] = battle;
        battles[battle.ClanB] = battle;
        await TellClanAsync(battle.ClanA, ClanBattlePacketWriter.Begun(battle.ClanB), battle.ClanB);
        await TellClanAsync(battle.ClanB, ClanBattlePacketWriter.Begun(battle.ClanA), battle.ClanA);
    }

    public async Task SurrenderAsync(UserSession leader)
    {
        var clan = sessionManager.Knights.GetClan(leader.KnightsId);
        if (clan == null || !IsLeader(leader, clan) || !battles.TryGetValue(clan.Id, out var battle))
        {
            await leader.Client.SendPacket(ClanBattlePacketWriter.Result(ClanBattleResult.NotAtWar));
            return;
        }

        var winnerId = battle.ClanA == clan.Id ? battle.ClanB : battle.ClanA;
        var winner = sessionManager.Knights.GetClan(winnerId);
        await TellClanAsync(clan.Id, ClanBattlePacketWriter.End(ClanBattleEnd.YouSurrendered, winnerId, winner?.Name ?? ""), 0);
        await TellClanAsync(winnerId, ClanBattlePacketWriter.End(ClanBattleEnd.TheySurrendered, clan.Id, clan.Name), 0);
        await EndAsync(battle);
    }

    public async Task RestoreAsync(UserSession session)
    {
        var enemy = session.WithLock(s =>
        {
            s.ClanBattleEnemy = EnemyOf(s.KnightsId);
            return s.ClanBattleEnemy;
        });
        if (enemy != 0)
            await session.Client.SendPacket(ClanBattlePacketWriter.Begun(enemy));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(CheckSeconds));
        while (await timer.WaitForNextTickAsync(stoppingToken))
            await ExpireAsync();
    }

    public async Task ExpireAsync()
    {
        var now = time.GetUtcNow();
        foreach (var battle in battles.Values.Distinct().Where(b => b.Ends <= now).ToList())
            await EndAsync(battle);
        foreach (var (leaderId, challenge) in challenges)
            if (challenge.Expires <= now)
                challenges.TryRemove(leaderId, out _);
    }

    private async Task EndAsync(Battle battle)
    {
        battles.TryRemove(battle.ClanA, out _);
        battles.TryRemove(battle.ClanB, out _);
        await TellClanAsync(battle.ClanA, ClanBattlePacketWriter.End(ClanBattleEnd.Cleared), 0);
        await TellClanAsync(battle.ClanB, ClanBattlePacketWriter.End(ClanBattleEnd.Cleared), 0);
    }

    private async Task TellClanAsync(short clanId, Common.Infrastructure.Network.Packet packet, short enemy)
    {
        foreach (var member in sessionManager.GetAll().Where(s => s.KnightsId == clanId))
        {
            var stillMember = member.WithLock(m =>
            {
                if (m.KnightsId != clanId)
                    return false;
                m.ClanBattleEnemy = enemy;
                return true;
            });
            if (stillMember)
                await member.Client.SendPacket(packet);
        }
    }

    private static bool IsLeader(UserSession session, KnightsEntity clan) =>
        string.Equals(clan.Chief, session.Name, StringComparison.OrdinalIgnoreCase);
}
