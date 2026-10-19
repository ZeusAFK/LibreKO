using System.Collections.Concurrent;
using LibreKO.Common.Domain.Services;
using LibreKO.Game.Configuration;
using LibreKO.Game.Protocol;
using LibreKO.Game.Protocol.Writers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace LibreKO.Game.World;

public interface IPartyUserVoteService
{
    Task StartAsync(UserSession session, int targetId, byte reason, string text);
    Task AnswerAsync(UserSession session, byte answer);
    Task<(short Warnings, short Limit)?> JuraidBarAsync(int characterId);
    Task ExpireDueAsync();
}

public sealed class PartyUserVoteService(
    SessionManager sessionManager,
    IServiceScopeFactory scopeFactory,
    IUserNotificationService notifications,
    IOptions<GameServerSettings> settings,
    TimeProvider? clock = null) : BackgroundService, IPartyUserVoteService
{
    private readonly TimeProvider time = clock ?? TimeProvider.System;

    private sealed class Vote
    {
        public int TargetId;
        public HashSet<int> Voters = [];
        public Dictionary<int, byte> Answers = [];
        public DateTimeOffset Expires;
    }

    private readonly ConcurrentDictionary<int, Vote> votes = new();
    private readonly ConcurrentDictionary<int, DateTimeOffset> cooldowns = new();

    private PartyVoteSettings Rules => settings.Value.PartyVote;
    private TimeSpan WarningsLast => TimeSpan.FromDays(Rules.WarningDays);

    public async Task StartAsync(UserSession session, int targetId, byte reason, string text)
    {
        short result = Check(session, targetId, reason, text, out var party);
        if (result != PartyUserVoteRules.Started || party == null)
        {
            await session.Client.SendPacket(PartyPacketWriter.UserVoteRefused(result));
            return;
        }

        session.Money -= PartyUserVoteRules.Cost;
        await notifications.SendGoldLossAsync(session, PartyUserVoteRules.Cost);
        var now = time.GetUtcNow();
        cooldowns[party.Index] = now.AddSeconds(PartyUserVoteRules.CooldownSeconds);
        var vote = new Vote { TargetId = targetId, Expires = now.AddSeconds(PartyUserVoteRules.AnswerSeconds) };
        foreach (var member in OnlineMembers(party))
            if (member.CharacterId != targetId) vote.Voters.Add(member.CharacterId);
        votes[party.Index] = vote;

        string targetName = sessionManager.GetByCharacterId(targetId)?.Name ?? "";
        var ask = PartyPacketWriter.UserVoteAsk(targetName, reason, reason == PartyUserVoteRules.ReasonOther ? text : "");
        foreach (int voterId in vote.Voters)
            if (sessionManager.GetByCharacterId(voterId) is { } voter)
                await voter.Client.SendPacket(ask);
    }

    public async Task AnswerAsync(UserSession session, byte answer)
    {
        if (!session.IsInParty || !votes.TryGetValue(session.PartyIndex, out var vote)) return;
        if (!vote.Voters.Contains(session.CharacterId) || vote.Answers.ContainsKey(session.CharacterId))
        {
            await session.Client.SendPacket(PartyPacketWriter.UserVoteRefused(PartyUserVoteRules.AlreadyAnswered));
            return;
        }
        vote.Answers[session.CharacterId] = answer is PartyUserVoteRules.AnswerYes or PartyUserVoteRules.AnswerNo
            ? answer : PartyUserVoteRules.AnswerDraw;
        if (vote.Answers.Count >= vote.Voters.Count) await FinishAsync(session.PartyIndex);
    }

    public async Task<(short Warnings, short Limit)?> JuraidBarAsync(int characterId)
    {
        using var scope = scopeFactory.CreateScope();
        var (warnings, warnedAt) = await scope.ServiceProvider.GetRequiredService<ICharacterRepository>()
            .GetJuraidWarningsAsync(characterId);
        short standing = PartyUserVoteRules.Standing(warnings, warnedAt, time.GetUtcNow().UtcDateTime, WarningsLast);
        return PartyUserVoteRules.Barred(standing, Rules.WarningLimit) ? (standing, (short)Rules.WarningLimit) : null;
    }

    public async Task ExpireDueAsync()
    {
        var now = time.GetUtcNow();
        foreach (var (partyIndex, vote) in votes)
            if (now >= vote.Expires) await FinishAsync(partyIndex);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        while (await timer.WaitForNextTickAsync(stoppingToken))
            await ExpireDueAsync();
    }

    private short Check(UserSession session, int targetId, byte reason, string text, out PartyGroup? party)
    {
        party = null;
        if (session.ZoneId != PartyUserVoteRules.JuraidZone) return PartyUserVoteRules.NotHere;
        party = session.IsInParty ? sessionManager.Parties.GetParty(session.PartyIndex) : null;
        if (party == null || targetId == session.CharacterId || party.FindMember((short)targetId) < 0
            || sessionManager.GetByCharacterId(targetId) == null)
            return PartyUserVoteRules.NoSuchUser;
        if (votes.ContainsKey(party.Index)) return PartyUserVoteRules.AlreadyRunning;
        if (OnlineMembers(party).Count < PartyUserVoteRules.MinimumVoters) return PartyUserVoteRules.TooFewVoters;
        if (cooldowns.TryGetValue(party.Index, out var until) && time.GetUtcNow() < until) return PartyUserVoteRules.CoolingDown;
        if (reason is 0 or > PartyUserVoteRules.LastReason) return PartyUserVoteRules.NoSuchUser;
        if (reason == PartyUserVoteRules.ReasonOther && !PartyUserVoteRules.TextFits(text)) return PartyUserVoteRules.TextTooLong;
        if (session.Money < PartyUserVoteRules.Cost) return PartyUserVoteRules.NotEnoughCoins;
        return PartyUserVoteRules.Started;
    }

    private List<UserSession> OnlineMembers(PartyGroup party)
    {
        var members = new List<UserSession>();
        foreach (short id in party.MemberIds)
            if (id >= 0 && sessionManager.GetByCharacterId(id) is { } member) members.Add(member);
        return members;
    }

    private async Task FinishAsync(int partyIndex)
    {
        if (!votes.TryRemove(partyIndex, out var vote)) return;
        bool warned = PartyUserVoteRules.Warns(vote.Answers.Values);
        var result = PartyPacketWriter.UserVoteResult(warned);
        foreach (int voterId in vote.Voters)
            if (sessionManager.GetByCharacterId(voterId) is { } voter)
                await voter.Client.SendPacket(result);
        if (!warned) return;

        short warnings;
        using (var scope = scopeFactory.CreateScope())
            warnings = await scope.ServiceProvider.GetRequiredService<ICharacterRepository>()
                .AddJuraidWarningAsync(vote.TargetId, time.GetUtcNow().UtcDateTime, WarningsLast);
        if (sessionManager.GetByCharacterId(vote.TargetId) is { } target)
            await target.Client.SendPacket(PartyPacketWriter.UserVoteWarnings(warnings, (short)Rules.WarningLimit));
    }
}
