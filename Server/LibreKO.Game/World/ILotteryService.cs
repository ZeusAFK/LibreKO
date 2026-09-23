using LibreKO.Common.Domain.Entities.GameData;
using LibreKO.Game.Protocol.Writers;

namespace LibreKO.Game.World;

public sealed class ActiveLottery
{
    public LotteryEventData EventData { get; }
    public DateTime EndTime { get; }
    public List<(int CharacterId, string CharacterName)> Tickets { get; } = [];
    public Dictionary<int, int> PlayerTicketCounts { get; } = [];
    public object Lock { get; } = new();

    public ActiveLottery(LotteryEventData data, DateTime endTime)
    {
        EventData = data;
        EndTime = endTime;
    }

    public int RemainingSeconds => Math.Max(0, (int)(EndTime - DateTime.UtcNow).TotalSeconds);
    public int TotalTickets => Tickets.Count;
    public int TicketsFor(int characterId) => PlayerTicketCounts.GetValueOrDefault(characterId, 0);
}

public interface ILotteryService
{
    ActiveLottery? ActiveEvent { get; }
    IReadOnlyCollection<LotteryEventData> AvailableEvents { get; }

    Task StartAsync(int lotteryId, int? durationMinutes = null);
    Task CloseAsync(bool cancelWithoutWinners = false);
    Task<(bool Success, string Message, int MyTickets, int TotalTickets)> BuyTicketAsync(UserSession player);
    Task SyncPlayerAsync(UserSession player);
    Task TickAsync();
}
