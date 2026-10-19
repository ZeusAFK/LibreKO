using System.Collections.Concurrent;
using LibreKO.Game.Protocol.Writers;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace LibreKO.Game.World;

public interface IPartyWarBoardService
{
    void CountDamage(UserSession attacker, int amount);
    void CountHealing(UserSession healer, int amount);
    Task SetModeAsync(UserSession session, byte target, byte mode);
    void MemberJoined(int partyIndex, int memberId);
    void SeedModes(int partyIndex, int memberId, byte rowMode, byte boardMode);
    void MemberLeft(int partyIndex, int memberId);
    void PartyDeleted(int partyIndex);
    Task PushAsync();
}

public sealed class PartyWarBoardService(
    SessionManager sessionManager,
    ILogger<PartyWarBoardService> logger) : BackgroundService, IPartyWarBoardService
{
    public const byte TargetRows = 1;
    public const byte TargetBoard = 2;
    public const byte ModeOff = 0;
    public const byte ModeDamage = 1;
    public const byte ModeHealing = 2;
    public const int ShareScale = 1000;
    private const int PushIntervalMs = 500;

    private sealed class Member
    {
        public long Damage;
        public long Healing;
        public byte RowMode = ModeDamage;
        public byte BoardMode = ModeOff;

        public byte ModeFor(byte target) => target == TargetRows ? RowMode : BoardMode;
    }

    private sealed class Board
    {
        public readonly Dictionary<int, Member> Members = [];
        public bool Dirty;

        public Member Of(int memberId)
        {
            if (!Members.TryGetValue(memberId, out var member))
            {
                member = new Member();
                Members[memberId] = member;
            }
            return member;
        }
    }

    private static readonly Member Fresh = new();
    private readonly ConcurrentDictionary<int, Board> boards = new();

    public void CountDamage(UserSession attacker, int amount) => Count(attacker, amount, healing: false);

    public void CountHealing(UserSession healer, int amount) => Count(healer, amount, healing: true);

    private void Count(UserSession session, int amount, bool healing)
    {
        if (amount <= 0 || !session.IsInParty)
            return;

        var board = boards.GetOrAdd(session.PartyIndex, _ => new Board());
        lock (board)
        {
            var member = board.Of(session.CharacterId);
            if (healing)
                member.Healing += amount;
            else
                member.Damage += amount;
            board.Dirty = true;
        }
    }

    public async Task SetModeAsync(UserSession session, byte target, byte mode)
    {
        if (target is not (TargetRows or TargetBoard) || mode > ModeHealing || !session.IsInParty)
            return;

        var board = boards.GetOrAdd(session.PartyIndex, _ => new Board());
        lock (board)
        {
            var member = board.Of(session.CharacterId);
            if (target == TargetRows)
                member.RowMode = mode;
            else
                member.BoardMode = mode;
        }

        if (mode == ModeOff || sessionManager.Parties.GetParty(session.PartyIndex) is not { } party)
            return;

        await session.Client.SendPacket(PartyPacketWriter.WarBoard(target, mode, Shares(party, board, mode)));
    }

    public void SeedModes(int partyIndex, int memberId, byte rowMode, byte boardMode)
    {
        if (rowMode > ModeHealing || boardMode > ModeHealing)
            return;
        var board = boards.GetOrAdd(partyIndex, _ => new Board());
        lock (board)
        {
            var member = board.Of(memberId);
            member.RowMode = rowMode;
            member.BoardMode = boardMode;
        }
    }

    public void MemberJoined(int partyIndex, int memberId)
    {
        var board = boards.GetOrAdd(partyIndex, _ => new Board());
        lock (board)
        {
            board.Members[memberId] = new Member();
            board.Dirty = true;
        }
    }

    public void MemberLeft(int partyIndex, int memberId)
    {
        if (!boards.TryGetValue(partyIndex, out var board))
            return;
        lock (board)
        {
            board.Members.Remove(memberId);
            board.Dirty = true;
        }
    }

    public void PartyDeleted(int partyIndex) => boards.TryRemove(partyIndex, out _);

    public async Task PushAsync()
    {
        foreach (var (index, board) in boards)
        {
            lock (board)
            {
                if (!board.Dirty)
                    continue;
                board.Dirty = false;
            }

            if (sessionManager.Parties.GetParty(index) is not { } party)
            {
                boards.TryRemove(index, out _);
                continue;
            }

            foreach (var target in new[] { TargetRows, TargetBoard })
            {
                foreach (var mode in new[] { ModeDamage, ModeHealing })
                {
                    var recipients = Recipients(party, board, target, mode);
                    if (recipients.Count == 0)
                        continue;
                    var packet = PartyPacketWriter.WarBoard(target, mode, Shares(party, board, mode));
                    foreach (var recipient in recipients)
                        await recipient.Client.SendPacket(packet);
                }
            }
        }
    }

    private List<UserSession> Recipients(PartyGroup party, Board board, byte target, byte mode)
    {
        var recipients = new List<UserSession>();
        lock (board)
        {
            foreach (var memberId in party.MemberIds)
            {
                if (memberId < 0)
                    continue;
                var stored = (board.Members.TryGetValue(memberId, out var member) ? member : Fresh).ModeFor(target);
                if (stored == mode && sessionManager.GetByCharacterId(memberId) is { } session)
                    recipients.Add(session);
            }
        }
        return recipients;
    }

    private static List<PartyPacketWriter.WarBoardShare> Shares(PartyGroup party, Board board, byte mode)
    {
        var counters = new List<(int Id, long Value)>();
        lock (board)
        {
            foreach (var memberId in party.MemberIds)
            {
                if (memberId < 0)
                    continue;
                long value = board.Members.TryGetValue(memberId, out var member)
                    ? mode == ModeHealing ? member.Healing : member.Damage
                    : 0;
                counters.Add((memberId, value));
            }
        }
        return ShareOf(counters);
    }

    public static List<PartyPacketWriter.WarBoardShare> ShareOf(IReadOnlyList<(int Id, long Value)> counters)
    {
        long total = 0;
        foreach (var (_, value) in counters)
            total += value;
        var shares = new List<PartyPacketWriter.WarBoardShare>(counters.Count);
        foreach (var (id, value) in counters)
            shares.Add(new PartyPacketWriter.WarBoardShare(id, total > 0 ? (ushort)(value * ShareScale / total) : (ushort)0));
        return shares;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(PushIntervalMs));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await PushAsync();
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Error pushing the party war board");
            }
        }
    }
}
