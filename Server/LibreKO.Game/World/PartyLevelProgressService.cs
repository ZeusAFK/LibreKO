using System.Collections.Concurrent;
using LibreKO.Common.Domain.Services;
using LibreKO.Game.Protocol;
using LibreKO.Game.Protocol.Writers;

namespace LibreKO.Game.World;

public interface IPartyLevelProgressService
{
    ushort PermilleOf(UserSession member);
    Task ProgressChangedAsync(UserSession member);
    Task AnnounceAsync(UserSession member);
    Task SendPartyToJoinerAsync(UserSession joiner, PartyGroup party);
}

public sealed class PartyLevelProgressService(
    SessionManager sessionManager,
    IGameDataService gameDataService,
    ICombatNotificationService combatNotificationService) : IPartyLevelProgressService
{
    public const int PermilleScale = 1000;
    public const int PermillePerPercent = 10;

    private readonly record struct Shown(byte Level, int Percent);

    private readonly ConcurrentDictionary<int, Shown> lastShown = new();

    public static ushort Permille(long experience, long required) =>
        required <= 0 ? (ushort)0 : (ushort)Math.Clamp(experience * PermilleScale / required, 0, PermilleScale);

    public ushort PermilleOf(UserSession member) => Permille(
        member.Experience,
        RebirthBonus.RequiredExperience(gameDataService.GetMaxExpForLevel(member.Level), member.RebirthLevel));

    public async Task ProgressChangedAsync(UserSession member)
    {
        if (PartyOf(member) is not { } party)
        {
            lastShown.TryRemove(member.CharacterId, out _);
            return;
        }

        var permille = PermilleOf(member);
        var shown = new Shown(member.Level, permille / PermillePerPercent);
        if (lastShown.TryGetValue(member.CharacterId, out var before) && before == shown)
            return;

        lastShown[member.CharacterId] = shown;
        await combatNotificationService.SendToPartyAsync(party, PartyPacketWriter.LevelProgress(member.CharacterId, permille));
    }

    public async Task AnnounceAsync(UserSession member)
    {
        if (PartyOf(member) is not { } party)
            return;

        var permille = PermilleOf(member);
        lastShown[member.CharacterId] = new Shown(member.Level, permille / PermillePerPercent);
        await combatNotificationService.SendToPartyAsync(party, PartyPacketWriter.LevelProgress(member.CharacterId, permille));
    }

    public async Task SendPartyToJoinerAsync(UserSession joiner, PartyGroup party)
    {
        foreach (var memberId in party.MemberIds)
        {
            if (memberId < 0 || memberId == joiner.CharacterId)
                continue;

            if (sessionManager.GetByCharacterId(memberId) is { } member)
                await joiner.Client.SendPacket(PartyPacketWriter.LevelProgress(member.CharacterId, PermilleOf(member)));
        }
    }

    private PartyGroup? PartyOf(UserSession member) =>
        member.IsInParty
        && sessionManager.Parties.GetParty(member.PartyIndex) is { } party
        && party.FindMember((short)member.CharacterId) >= 0
            ? party
            : null;
}
