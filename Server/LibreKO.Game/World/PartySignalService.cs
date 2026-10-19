using System.Collections.Concurrent;
using LibreKO.Game.Protocol.Writers;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace LibreKO.Game.World;

public interface IPartySignalService
{
    void Echo(UserSession session, uint token);
    ushort? ShownMs(int characterId);
    Task SendTableAsync(UserSession recipient, PartyGroup party);
    Task TickAsync();
}

public sealed class PartySignalService(
    SessionManager sessionManager,
    ILogger<PartySignalService> logger,
    Func<long>? clock = null) : BackgroundService, IPartySignalService
{
    public const int ProbeIntervalMs = 2000;
    public const double SmoothingWeight = 0.3;
    public const int MissedProbesForNoSignal = 3;
    public const int ShownStepMs = 10;
    public const int StrongMaxMs = 100;
    public const int GoodMaxMs = 200;
    public const int WeakMaxMs = 350;
    private const int LargestShownMs = PartyPacketWriter.NoSignal / ShownStepMs * ShownStepMs;

    public enum SignalLevel : byte
    {
        Strong,
        Good,
        Weak,
        Poor,
        NoSignal,
    }

    public static SignalLevel LevelOf(ushort shownMs) => shownMs switch
    {
        PartyPacketWriter.NoSignal => SignalLevel.NoSignal,
        <= StrongMaxMs => SignalLevel.Strong,
        <= GoodMaxMs => SignalLevel.Good,
        <= WeakMaxMs => SignalLevel.Weak,
        _ => SignalLevel.Poor,
    };

    private readonly record struct Reading(ushort Ms, SignalLevel Level)
    {
        public static Reading Of(ushort ms) => new(ms, LevelOf(ms));
    }

    private sealed class Signal
    {
        public readonly List<(uint Token, long SentAt)> Pending = [];
        public double? Smoothed;
        public bool Lost;
    }

    private readonly Func<long> currentTicks = clock ?? (() => Environment.TickCount64);
    private readonly ConcurrentDictionary<int, Signal> signals = new();
    private readonly ConcurrentDictionary<int, Dictionary<int, Reading>> sentTables = new();

    public void Echo(UserSession session, uint token)
    {
        if (!signals.TryGetValue(session.CharacterId, out var signal))
            return;

        var now = currentTicks();
        lock (signal)
        {
            var index = signal.Pending.FindIndex(probe => probe.Token == token);
            if (index < 0)
                return;

            double sample = Math.Max(0, now - signal.Pending[index].SentAt);
            signal.Pending.RemoveRange(0, index + 1);
            signal.Smoothed = signal.Smoothed is { } smoothed ? smoothed + SmoothingWeight * (sample - smoothed) : sample;
            signal.Lost = false;
        }
    }

    public ushort? ShownMs(int characterId) => ReadingOf(characterId)?.Ms;

    public async Task SendTableAsync(UserSession recipient, PartyGroup party)
    {
        var entries = EntriesOf(ReadingsOf(MembersOf(party)));
        if (entries.Count > 0)
            await recipient.Client.SendPacket(PartyPacketWriter.SignalTable(entries));
    }

    public async Task TickAsync()
    {
        var now = currentTicks();
        var parties = MembersByParty();
        Forget(parties);

        foreach (var (partyIndex, members) in parties)
        {
            foreach (var member in members)
                MarkMissed(signals.GetOrAdd(member.CharacterId, _ => new Signal()));

            await PublishIfChangedAsync(partyIndex, members);

            foreach (var member in members)
                await ProbeAsync(member, now);
        }
    }

    private void Forget(Dictionary<int, List<UserSession>> parties)
    {
        var tracked = parties.Values.SelectMany(members => members).Select(member => member.CharacterId).ToHashSet();
        foreach (var characterId in signals.Keys)
        {
            if (!tracked.Contains(characterId))
                signals.TryRemove(characterId, out _);
        }

        foreach (var partyIndex in sentTables.Keys)
        {
            if (!parties.ContainsKey(partyIndex))
                sentTables.TryRemove(partyIndex, out _);
        }
    }

    private static void MarkMissed(Signal signal)
    {
        lock (signal)
        {
            if (signal.Pending.Count >= MissedProbesForNoSignal)
                signal.Lost = true;
        }
    }

    private async Task PublishIfChangedAsync(int partyIndex, List<UserSession> members)
    {
        var readings = ReadingsOf(members);
        var sent = sentTables.GetValueOrDefault(partyIndex);
        var changed = readings.Any(reading =>
            sent == null || !sent.TryGetValue(reading.Key, out var before) || before != reading.Value);
        if (!changed)
            return;

        sentTables[partyIndex] = readings;
        var table = PartyPacketWriter.SignalTable(EntriesOf(readings));
        foreach (var member in members)
            await member.Client.SendPacket(table);
    }

    private async Task ProbeAsync(UserSession member, long now)
    {
        var token = (uint)Random.Shared.Next();
        var signal = signals.GetOrAdd(member.CharacterId, _ => new Signal());
        lock (signal)
        {
            signal.Pending.Add((token, now));
            if (signal.Pending.Count > MissedProbesForNoSignal)
                signal.Pending.RemoveAt(0);
        }

        await member.Client.SendPacket(PartyPacketWriter.SignalProbe(token));
    }

    private Reading? ReadingOf(int characterId)
    {
        if (!signals.TryGetValue(characterId, out var signal))
            return null;

        lock (signal)
        {
            if (signal.Lost)
                return Reading.Of(PartyPacketWriter.NoSignal);
            if (signal.Smoothed is not { } smoothed)
                return null;

            var rounded = Math.Round(smoothed / ShownStepMs, MidpointRounding.AwayFromZero) * ShownStepMs;
            return Reading.Of((ushort)Math.Clamp(rounded, 0, LargestShownMs));
        }
    }

    private Dictionary<int, Reading> ReadingsOf(List<UserSession> members)
    {
        var readings = new Dictionary<int, Reading>(members.Count);
        foreach (var member in members)
        {
            if (ReadingOf(member.CharacterId) is { } reading)
                readings[member.CharacterId] = reading;
        }
        return readings;
    }

    private static List<PartyPacketWriter.SignalEntry> EntriesOf(Dictionary<int, Reading> readings) =>
        readings.Select(reading => new PartyPacketWriter.SignalEntry(reading.Key, reading.Value.Ms)).ToList();

    private Dictionary<int, List<UserSession>> MembersByParty()
    {
        var parties = new Dictionary<int, List<UserSession>>();
        foreach (var session in sessionManager.GetAll())
        {
            if (!session.IsInParty || parties.ContainsKey(session.PartyIndex)
                || sessionManager.Parties.GetParty(session.PartyIndex) is not { } party)
                continue;

            parties[party.Index] = MembersOf(party);
        }
        return parties;
    }

    private List<UserSession> MembersOf(PartyGroup party)
    {
        var members = new List<UserSession>(PartyGroup.MaxMembers);
        foreach (var memberId in party.MemberIds)
        {
            if (memberId >= 0 && sessionManager.GetByCharacterId(memberId) is { } member && member.PartyIndex == party.Index)
                members.Add(member);
        }
        return members;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(ProbeIntervalMs));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await TickAsync();
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Party signal tick failed");
            }
        }
    }
}
