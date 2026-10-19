using LibreKO.Common.Domain.Services;
using LibreKO.Common.Enums;
using LibreKO.Game.Configuration;
using LibreKO.Game.Protocol;
using LibreKO.Game.Protocol.Writers;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LibreKO.Game.World;

public interface IWarCaptainService
{
    bool IsCaptain(UserSession session);
    Task OnPlayerDiedAsync(UserSession victim);
}

public sealed class WarCaptainService : BackgroundService, IWarCaptainService
{
    public const byte CaptainFame = 100;
    public const int SelectionDivisor = 24;
    public const int ClanChiefsPerNation = 5;
    private const byte FameSub = 1;
    private const int TickMilliseconds = 1000;
    private const int KarusCaptainsText = 140;
    private const int ElMoradCaptainsText = 141;
    private const int KarusCaptainKilledText = 142;
    private const int ElMoradCaptainKilledText = 143;
    private const string NamePlaceholder = "%s";

    private readonly SessionManager sessions;
    private readonly IOptions<GameServerSettings> settings;
    private readonly IKingSystemRuntimeService kings;
    private readonly IGameDataService gameData;
    private readonly ILogger<WarCaptainService> logger;
    private readonly Dictionary<int, byte> formerFame = [];
    private readonly SemaphoreSlim gate = new(1, 1);
    private bool selected;

    public WarCaptainService(
        SessionManager sessions,
        IOptions<GameServerSettings> settings,
        IKingSystemRuntimeService kings,
        IGameDataService gameData,
        IZoneTransitionService zones,
        ILogger<WarCaptainService> logger)
    {
        this.sessions = sessions;
        this.settings = settings;
        this.kings = kings;
        this.gameData = gameData;
        this.logger = logger;
        zones.ZoneAbilitySent += OnZoneAbilitySentAsync;
    }

    public bool IsCaptain(UserSession session) => selected && formerFame.ContainsKey(session.CharacterId);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(TickMilliseconds));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await TickAsync();
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "War captain tick failed");
            }
        }
    }

    internal async Task TickAsync()
    {
        var battle = sessions.Battle;
        var selectAfter = TimeSpan.FromMinutes(settings.Value.Events.BattleDurationMinutes) / SelectionDivisor;
        if (battle.IsBattleActive && !selected && battle.GetElapsedTime() >= selectAfter)
            await SelectAsync();
        else if (!battle.IsBattleActive && selected)
            await ResetAsync();
    }

    internal async Task SelectAsync()
    {
        await gate.WaitAsync();
        try
        {
            selected = true;
            foreach (var nation in new[] { AccountNation.Karus, AccountNation.ElMorad })
            {
                var captains = CandidatesOf(nation).DistinctBy(s => s.CharacterId).ToList();
                foreach (var captain in captains)
                {
                    formerFame[captain.CharacterId] = captain.KnightsFame;
                    await SetFameAsync(captain, CaptainFame);
                }
                if (captains.Count > 0)
                    await AnnounceAsync(nation, nation == AccountNation.Karus ? KarusCaptainsText : ElMoradCaptainsText,
                        captains.Select(TaggedName).ToList());
            }
            logger.LogInformation("War captains chosen: {Count}", formerFame.Count);
        }
        finally
        {
            gate.Release();
        }
    }

    internal async Task ResetAsync()
    {
        await gate.WaitAsync();
        try
        {
            foreach (var (characterId, fame) in formerFame)
                if (sessions.GetByCharacterId(characterId) is { } captain && captain.KnightsFame == CaptainFame)
                    await SetFameAsync(captain, fame);
            formerFame.Clear();
            selected = false;
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task OnPlayerDiedAsync(UserSession victim)
    {
        if (!IsCaptain(victim))
            return;
        var clan = ClanName(victim);
        var text = gameData.GetServerResource(victim.Nation == AccountNation.Karus ? KarusCaptainKilledText : ElMoradCaptainKilledText)
                   ?? "[%s][%s], the captain has been killed";
        await BroadcastAsync(null, Fill(text, [clan, victim.Name]));
    }

    private IEnumerable<UserSession> CandidatesOf(AccountNation nation)
    {
        var inWar = sessions.GetAll().Where(s => s.Nation == nation && BattleZoneManager.IsBattleZone(s.ZoneId)).ToList();
        if (kings.GetKingData(nation) is { } king && inWar.FirstOrDefault(s => kings.IsKing(s, king)) is { } kingSession)
            yield return kingSession;

        var topClans = sessions.Knights.GetAll()
            .Where(c => c.Nation == (byte)nation)
            .OrderByDescending(c => c.Points)
            .Take(ClanChiefsPerNation)
            .Select(c => c.Id)
            .ToHashSet();
        foreach (var chief in inWar.Where(s => s.KnightsFame == ClanRules.FameChief && topClans.Contains(s.KnightsId)))
            yield return chief;
    }

    private async Task OnZoneAbilitySentAsync(UserSession session)
    {
        if (IsCaptain(session) && session.KnightsFame != CaptainFame)
            await SetFameAsync(session, CaptainFame);
        else if (!selected && session.KnightsFame == CaptainFame)
            await SetFameAsync(session, ClanRules.FameChief);
    }

    private async Task SetFameAsync(UserSession session, byte fame)
    {
        session.KnightsFame = fame;
        await sessions.Regions.SendToRegion(session, MiscPacketWriter.AuthorityChange(FameSub, session.CharacterId, fame),
            excludeSender: false);
    }

    private string TaggedName(UserSession session) => $"[{ClanName(session)}][{session.Name}]";

    private string ClanName(UserSession session) =>
        session.KnightsId > 0 ? sessions.Knights.GetClan(session.KnightsId)?.Name ?? string.Empty : string.Empty;

    private async Task AnnounceAsync(AccountNation nation, int textId, IReadOnlyList<string> names)
    {
        var text = gameData.GetServerResource(textId) ?? "The captain(s) are %s, %s, %s, %s, %s";
        await BroadcastAsync(nation, Fill(text, names));
    }

    private async Task BroadcastAsync(AccountNation? nation, string text)
    {
        var packet = NoticePacketWriter.Broadcast(text);
        foreach (var session in sessions.GetAll().Where(s => nation == null || s.Nation == nation))
            await session.Client.SendPacket(packet);
    }

    internal static string Fill(string template, IReadOnlyList<string> values)
    {
        var parts = template.Split(NamePlaceholder);
        var filled = new System.Text.StringBuilder(parts[0]);
        for (int i = 1; i < parts.Length; i++)
            filled.Append(i - 1 < values.Count ? values[i - 1] : string.Empty).Append(parts[i]);
        return filled.ToString().TrimEnd(' ', ',');
    }
}
