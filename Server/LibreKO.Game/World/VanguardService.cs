using LibreKO.Common.Domain.Services;
using LibreKO.Common.Enums;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.Protocol;
using LibreKO.Game.Protocol.Writers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace LibreKO.Game.World;

public interface IVanguardService
{
    bool Enabled { get; }
    Task StartAsync();
    Task StopAsync();
    bool IsWanted(UserSession session);
    Task HandleAsync(IClient client, Packet packet);
    Task OnPlayerDiedAsync(UserSession victim, UserSession? killer);
    Task ReleaseAsync(UserSession session);
}

public sealed class VanguardService : BackgroundService, IVanguardService
{
    private const int TickMilliseconds = 1000;

    private sealed class Round
    {
        public required byte Zone { get; init; }
        public VanguardPhase Phase { get; set; }
        public int PhaseLeft { get; set; }
        public int ActiveLeft { get; set; }
        public int ListLeft { get; set; }
        public int PositionLeft { get; set; }
        public int BaseCheckLeft { get; set; }
        public HashSet<int> Invited { get; } = [];
        public List<int> Karus { get; } = [];
        public List<int> ElMorad { get; } = [];

        public List<int> Of(AccountNation nation) => nation == AccountNation.Karus ? Karus : ElMorad;

        public bool Holds(int characterId) => Karus.Contains(characterId) || ElMorad.Contains(characterId);
    }

    private readonly SessionManager sessions;
    private readonly IGameDataService gameData;
    private readonly IServiceProvider services;
    private readonly ILogger<VanguardService> logger;
    private readonly Dictionary<byte, Round> rounds;
    private readonly SemaphoreSlim gate = new(1, 1);

    public VanguardService(
        SessionManager sessions,
        IGameDataService gameData,
        IZoneTransitionService zones,
        IServiceProvider services,
        ILogger<VanguardService> logger)
    {
        this.sessions = sessions;
        this.gameData = gameData;
        this.services = services;
        this.logger = logger;
        rounds = VanguardRules.Zones.ToDictionary(zone => zone, zone => new Round { Zone = zone });
        zones.PlayerLeavingZone += (session, _) => ReleaseAsync(session);
    }

    public bool Enabled { get; private set; }

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
                logger.LogError(ex, "Wanted event tick failed");
            }
        }
    }

    public async Task StartAsync()
    {
        await gate.WaitAsync();
        try
        {
            Enabled = true;
            foreach (var round in rounds.Values.Where(r => r.Phase == VanguardPhase.Off))
                Wait(round, 1);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task StopAsync()
    {
        List<Round> running;
        await gate.WaitAsync();
        try
        {
            Enabled = false;
            running = rounds.Values.Where(r => r.Phase is VanguardPhase.Accepting or VanguardPhase.Active).ToList();
            foreach (var round in rounds.Values.Except(running))
                round.Phase = VanguardPhase.Off;
        }
        finally
        {
            gate.Release();
        }

        foreach (var round in running)
            await EndRoundAsync(round, rewardSurvivors: false);
    }

    public bool IsWanted(UserSession session) =>
        rounds.TryGetValue(session.ZoneId, out var round) && round.Holds(session.CharacterId);

    internal async Task TickAsync()
    {
        foreach (var round in rounds.Values)
        {
            switch (round.Phase)
            {
                case VanguardPhase.Waiting:
                    if (--round.PhaseLeft <= 0)
                        await SelectAsync(round);
                    break;
                case VanguardPhase.Accepting:
                    await CountActiveAsync(round);
                    if (round.Phase == VanguardPhase.Accepting && --round.PhaseLeft <= 0)
                    {
                        round.Invited.Clear();
                        if (round.Karus.Count == 0 && round.ElMorad.Count == 0)
                            Wait(round, VanguardRules.WaitSeconds);
                        else
                            round.Phase = VanguardPhase.Active;
                    }
                    break;
                case VanguardPhase.Active:
                    await CountActiveAsync(round);
                    break;
            }
        }
    }

    private async Task CountActiveAsync(Round round)
    {
        if (--round.ListLeft == 0)
            await SendListsAsync(round);
        if (round.ListLeft <= 0 && --round.PositionLeft <= 0)
        {
            round.PositionLeft = VanguardRules.PositionSeconds;
            await SendPositionsAsync(round);
        }
        if (--round.BaseCheckLeft <= 0)
        {
            round.BaseCheckLeft = VanguardRules.BaseCheckSeconds;
            await SendHomeFromBasesAsync(round);
        }
        if (--round.ActiveLeft <= 0)
            await EndRoundAsync(round, rewardSurvivors: true);
    }

    private void Wait(Round round, int seconds)
    {
        round.Phase = Enabled ? VanguardPhase.Waiting : VanguardPhase.Off;
        round.PhaseLeft = seconds;
        round.Invited.Clear();
        round.Karus.Clear();
        round.ElMorad.Clear();
    }

    private async Task SelectAsync(Round round)
    {
        var candidates = InZone(round.Zone).Where(s => !s.IsGM && !s.IsBot && s.Hp > 0).ToList();
        var chosen = new List<UserSession>();
        foreach (var nation in new[] { AccountNation.Karus, AccountNation.ElMorad })
            chosen.AddRange(candidates.Where(s => s.Nation == nation)
                .OrderBy(_ => Random.Shared.Next())
                .Take(VanguardRules.MaxPerNation));

        if (chosen.Count == 0)
        {
            Wait(round, VanguardRules.WaitSeconds);
            return;
        }

        round.Phase = VanguardPhase.Accepting;
        round.PhaseLeft = VanguardRules.AcceptSeconds;
        round.ActiveLeft = VanguardRules.ActiveSeconds;
        round.ListLeft = VanguardRules.ListDelaySeconds;
        round.PositionLeft = 0;
        round.BaseCheckLeft = VanguardRules.BaseCheckSeconds;
        foreach (var session in chosen)
        {
            round.Invited.Add(session.CharacterId);
            await session.Client.SendPacket(VanguardPacketWriter.Invite());
        }
        logger.LogInformation("Wanted event: {Count} players invited in zone {Zone}", chosen.Count, round.Zone);
    }

    public async Task HandleAsync(IClient client, Packet packet)
    {
        var session = sessions.GetByClientId(client.Id);
        if (session == null || packet.RemainingBytes < 2 || packet.ReadByte() != VanguardPacketWriter.SubAppoint
            || packet.ReadByte() != VanguardPacketWriter.AcceptRequest)
            return;

        if (!rounds.TryGetValue(session.ZoneId, out var round) || round.Phase != VanguardPhase.Accepting
            || !round.Invited.Remove(session.CharacterId) || session.Hp <= 0)
        {
            await client.SendPacket(VanguardPacketWriter.Result(VanguardPacketWriter.AppointFailed));
            return;
        }

        var side = round.Of(session.Nation);
        if (side.Count >= VanguardRules.MaxPerNation)
        {
            await client.SendPacket(VanguardPacketWriter.Result(VanguardPacketWriter.AppointFailed));
            return;
        }

        side.Add(session.CharacterId);
        await client.SendPacket(VanguardPacketWriter.Result(VanguardPacketWriter.Appointed));
        if (gameData.GetMagic(VanguardRules.RewardMarkSkill) is { } magic)
            await services.GetRequiredService<IMagicStatusEffectService>().ExecuteAsync(
                session, magic, MagicSkillType.Buff, VanguardRules.RewardMarkSkill, session.CharacterId, new int[7]);
    }

    public async Task OnPlayerDiedAsync(UserSession victim, UserSession? killer)
    {
        if (!rounds.TryGetValue(victim.ZoneId, out var round) || !round.Holds(victim.CharacterId))
            return;

        await RemoveAsync(round, victim);
        if (killer != null && killer.Nation != victim.Nation)
            await RewardKillerAsync(killer);
        await CloseIfEmptyAsync(round);
    }

    public async Task ReleaseAsync(UserSession session)
    {
        if (!rounds.TryGetValue(session.ZoneId, out var round))
            return;
        round.Invited.Remove(session.CharacterId);
        if (!round.Holds(session.CharacterId))
            return;
        await RemoveAsync(round, session);
        await CloseIfEmptyAsync(round);
    }

    private async Task RemoveAsync(Round round, UserSession session)
    {
        round.Of(session.Nation).Remove(session.CharacterId);
        if (round.ListLeft <= 0)
            await SendToNationAsync(round.Zone, Enemy(session.Nation), VanguardPacketWriter.Remove(session.Name));
    }

    private async Task CloseIfEmptyAsync(Round round)
    {
        if (round.Phase == VanguardPhase.Active && round.Karus.Count == 0 && round.ElMorad.Count == 0)
            await EndRoundAsync(round, rewardSurvivors: false);
    }

    private async Task EndRoundAsync(Round round, bool rewardSurvivors)
    {
        var survivors = rewardSurvivors
            ? round.Karus.Concat(round.ElMorad).Select(sessions.GetByCharacterId)
                .Where(s => s != null && s.ZoneId == round.Zone).Cast<UserSession>().ToList()
            : [];
        Wait(round, VanguardRules.WaitSeconds);

        foreach (var survivor in survivors)
        {
            await GrantAsync(survivor, VanguardRules.OffensiveEmblem);
            await services.GetRequiredService<ILoyaltyService>().ChangeAsync(survivor, VanguardRules.SurvivorLoyalty);
        }
        foreach (var session in InZone(round.Zone))
            await session.Client.SendPacket(VanguardPacketWriter.Closed());
    }

    private async Task RewardKillerAsync(UserSession killer)
    {
        await GrantAsync(killer, Random.Shared.Next(2) == 0 ? VanguardRules.OffensiveEmblem : VanguardRules.DefensiveEmblem);
        var party = killer.IsInParty ? sessions.Parties.GetParty(killer.PartyIndex) : null;
        var members = party == null
            ? [killer]
            : party.MemberIds.Where(id => id > 0).Select(id => sessions.GetByCharacterId(id))
                .Where(s => s != null && s.Hp > 0 && s.ZoneId == killer.ZoneId).Cast<UserSession>().ToList();
        var loyalty = services.GetRequiredService<ILoyaltyService>();
        foreach (var member in members)
            await loyalty.ChangeAsync(member, VanguardRules.KillerPartyLoyalty);
    }

    private async Task GrantAsync(UserSession session, int itemId)
    {
        if (gameData.GetItem(itemId) is { } item)
            await services.GetRequiredService<IItemGrantService>().GrantAsync(session, item, 1);
    }

    private async Task SendListsAsync(Round round)
    {
        foreach (var nation in new[] { AccountNation.Karus, AccountNation.ElMorad })
        {
            var names = Live(round, nation).Select(s => s.Name).ToList();
            if (names.Count > 0)
                await SendToNationAsync(round.Zone, Enemy(nation), VanguardPacketWriter.List(names));
        }
    }

    private async Task SendPositionsAsync(Round round)
    {
        foreach (var nation in new[] { AccountNation.Karus, AccountNation.ElMorad })
        {
            var wanted = Live(round, nation).Select(s => ((short)s.X, (short)s.Z, s.Name)).ToList();
            if (wanted.Count > 0)
                await SendToNationAsync(round.Zone, Enemy(nation), VanguardPacketWriter.Positions(wanted));
        }
    }

    private async Task SendHomeFromBasesAsync(Round round)
    {
        var world = services.GetRequiredService<IWorldPacketCoordinator>();
        foreach (var nation in new[] { AccountNation.Karus, AccountNation.ElMorad })
            foreach (var session in Live(round, nation).Where(s => VanguardRules.InOwnBase(round.Zone, s.Nation, s.X, s.Z)).ToList())
                await world.WarpAsync(session, VanguardRules.CentreX, VanguardRules.CentreZ);
    }

    private IEnumerable<UserSession> Live(Round round, AccountNation nation) =>
        round.Of(nation).Select(sessions.GetByCharacterId)
            .Where(s => s != null && s.ZoneId == round.Zone).Cast<UserSession>();

    private IEnumerable<UserSession> InZone(byte zone) => sessions.GetAll().Where(s => s.ZoneId == zone).ToList();

    private async Task SendToNationAsync(byte zone, AccountNation nation, Packet packet)
    {
        foreach (var session in InZone(zone).Where(s => s.Nation == nation))
            await session.Client.SendPacket(packet);
    }

    private static AccountNation Enemy(AccountNation nation) =>
        nation == AccountNation.Karus ? AccountNation.ElMorad : AccountNation.Karus;
}
