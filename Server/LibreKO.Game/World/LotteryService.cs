using LibreKO.Common.Domain.Entities.GameData;
using LibreKO.Common.Domain.Services;
using LibreKO.Common.Enums;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.Protocol;
using LibreKO.Game.Protocol.Writers;
using Microsoft.Extensions.Logging;

namespace LibreKO.Game.World;

public class LotteryService : ILotteryService
{
    private readonly SessionManager _sessionManager;
    private readonly IGameDataService _gameDataService;
    private readonly IUserNotificationService _userNotificationService;
    private readonly IMailService _mailService;
    private readonly ILogger<LotteryService> _logger;

    private ActiveLottery? _active;
    private readonly object _stateLock = new();
    private int _lastAnnouncedMinute = -1;
    private int _lastAutoCheckMinute = -1;

    public LotteryService(
        SessionManager sessionManager,
        IGameDataService gameDataService,
        IUserNotificationService userNotificationService,
        IMailService mailService,
        ILogger<LotteryService> logger)
    {
        _sessionManager = sessionManager;
        _gameDataService = gameDataService;
        _userNotificationService = userNotificationService;
        _mailService = mailService;
        _logger = logger;
    }

    public ActiveLottery? ActiveEvent => _active;

    public IReadOnlyCollection<LotteryEventData> AvailableEvents =>
        _gameDataService.LotteryEventTable.Values.ToList();

    public async Task StartAsync(int lotteryId, int? durationMinutes = null)
    {
        if (!_gameDataService.LotteryEventTable.TryGetValue(lotteryId, out var eventData))
        {
            _logger.LogWarning("Lottery event {Id} not found in database", lotteryId);
            return;
        }

        lock (_stateLock)
        {
            if (_active != null && _active.RemainingSeconds > 0)
            {
                _logger.LogWarning("Lottery event '{Name}' is already active", _active.EventData.Name);
                return;
            }

            var duration = durationMinutes ?? (eventData.DurationMinutes > 0 ? eventData.DurationMinutes : LotteryEventData.DefaultDurationMinutes);
            var endTime = DateTime.UtcNow.AddMinutes(duration);
            _active = new ActiveLottery(eventData, endTime);
            _lastAnnouncedMinute = duration + 1;
        }

        _logger.LogInformation("Lottery event '{Name}' (ID {Id}) started for {Duration} minutes",
            eventData.Name, eventData.Id, durationMinutes ?? eventData.DurationMinutes);

        await BroadcastNoticeAsync($"[Lottery Event] '{eventData.Name}' has begun! Buy tickets via the Lottery window.");

        var firstReward = _gameDataService.LotteryRewardsByEvent[eventData.Id].OrderBy(r => r.Place).FirstOrDefault();
        if (firstReward != null)
            await BroadcastChatNoticeAsync($"[Lottery Event] {eventData.Name} started! 1st Prize: {GetRewardName(firstReward.ItemId)} x{firstReward.Count}.");
        else
            await BroadcastChatNoticeAsync($"[Lottery Event] {eventData.Name} started!");

        foreach (var player in _sessionManager.GetAll())
        {
            await SyncPlayerAsync(player);
        }
    }

    public async Task CloseAsync(bool cancelWithoutWinners = false)
    {
        ActiveLottery? current;
        lock (_stateLock)
        {
            current = _active;
            if (current == null) return;
            if (cancelWithoutWinners)
            {
                _active = null;
            }
        }

        if (cancelWithoutWinners)
        {
            _logger.LogInformation("Lottery event '{Name}' was cancelled without drawing winners", current.EventData.Name);
            await BroadcastNoticeAsync($"[Lottery Event] '{current.EventData.Name}' was cancelled by GM.");
            await BroadcastPacketAsync(LotteryPacketWriter.Close());

            var reqItemId = current.EventData.ReqItemId;
            var reqCost = current.EventData.ReqItemCount;

            foreach (var (charId, ticketCount) in current.PlayerTicketCounts)
            {
                if (ticketCount <= 0) continue;
                long totalRefund = (long)ticketCount * reqCost;
                if (totalRefund <= 0) continue;

                MailAttachmentDraft attachment;
                if (reqItemId == InventoryConstants.ItemGold)
                {
                    attachment = new MailAttachmentDraft(MailAttachmentKind.Gold, reqItemId, (int)Math.Min(totalRefund, int.MaxValue));
                }
                else
                {
                    var itemData = _gameDataService.GetItem(reqItemId);
                    short duration = itemData?.Duration ?? 0;
                    attachment = new MailAttachmentDraft(MailAttachmentKind.Item, reqItemId, (int)Math.Min(totalRefund, int.MaxValue), duration);
                }

                await _mailService.SendSystemMailAsync(
                    charId,
                    "Lottery Event Cancelled - Refund",
                    $"The '{current.EventData.Name}' event was cancelled. Your purchase of {ticketCount} ticket(s) has been refunded in full.",
                    [attachment]);
            }
            return;
        }

        await FinishAsync(current);
    }

    public async Task<(bool Success, string Message, int MyTickets, int TotalTickets)> BuyTicketAsync(UserSession player)
    {
        ActiveLottery? active = _active;
        if (active == null || active.RemainingSeconds <= 0)
            return (false, "No lottery event is currently active.", 0, 0);

        var reqItemId = active.EventData.ReqItemId;
        var reqItemCount = active.EventData.ReqItemCount;

        int myTickets;
        int totalTickets;
        List<(byte SlotIndex, int ItemId, ushort Count, short Durability)>? stackChanges = null;

        lock (active.Lock)
        {
            if (active.TicketsFor(player.CharacterId) >= active.EventData.UserLimit)
                return (false, "You have reached your maximum ticket limit for this lottery.", active.TicketsFor(player.CharacterId), active.TotalTickets);

            if (reqItemId == InventoryConstants.ItemGold)
            {
                if (player.Money < reqItemCount)
                    return (false, $"Not enough Noah. ({reqItemCount:N0} Noah required)", active.TicketsFor(player.CharacterId), active.TotalTickets);

                player.Money -= reqItemCount;
            }
            else
            {
                if (CountItem(player, reqItemId) < reqItemCount)
                {
                    var reqName = _gameDataService.GetItem(reqItemId)?.Name ?? "required item";
                    return (false, $"You don't have the required {reqName} x{reqItemCount}.", active.TicketsFor(player.CharacterId), active.TotalTickets);
                }

                stackChanges = RemoveItemsSynchronous(player, reqItemId, reqItemCount);
            }

            active.Tickets.Add((player.CharacterId, player.Name));
            active.PlayerTicketCounts[player.CharacterId] = active.TicketsFor(player.CharacterId) + 1;
            myTickets = active.TicketsFor(player.CharacterId);
            totalTickets = active.TotalTickets;
        }

        if (reqItemId == InventoryConstants.ItemGold)
        {
            await _userNotificationService.SendGoldLossAsync(player, reqItemCount);
        }
        else if (stackChanges != null)
        {
            foreach (var sc in stackChanges)
            {
                await _userNotificationService.SendStackChangeAsync(player, sc.SlotIndex, sc.ItemId, sc.Count, sc.Durability);
            }
            player.RecalculateStatsWithBuffs(_gameDataService);
            await _userNotificationService.SendWeightChangeAsync(player);
        }

        await BroadcastPacketAsync(LotteryPacketWriter.Progress(totalTickets));

        return (true, "Ticket purchased successfully! Good luck!", myTickets, totalTickets);
    }

    public async Task SyncPlayerAsync(UserSession player)
    {
        var active = _active;
        if (active == null || active.RemainingSeconds <= 0)
        {
            await player.Client.SendPacket(LotteryPacketWriter.InactiveState());
            return;
        }

        var eventData = active.EventData;
        var reqName = eventData.ReqItemId == InventoryConstants.ItemGold
            ? "Noah"
            : (_gameDataService.GetItem(eventData.ReqItemId)?.Name ?? "Item");

        var rewards = _gameDataService.LotteryRewardsByEvent[eventData.Id]
            .OrderBy(r => r.Place)
            .Select(r => new LotteryPacketWriter.RewardInfo(r.ItemId, r.Count, GetRewardName(r.ItemId)))
            .ToList();

        var pkt = LotteryPacketWriter.State(
            eventData.Id,
            eventData.Name,
            active.RemainingSeconds,
            eventData.UserLimit,
            active.TotalTickets,
            active.TicketsFor(player.CharacterId),
            eventData.ReqItemId,
            eventData.ReqItemCount,
            reqName,
            rewards);

        await player.Client.SendPacket(pkt);
    }

    public async Task TickAsync()
    {
        var active = _active;
        if (active != null)
        {
            var rem = active.RemainingSeconds;
            if (rem <= 0)
            {
                await FinishAsync(active);
                return;
            }

            // Periodic countdown announcements
            int[] intervals = [15, 10, 5, 3, 1];
            foreach (var minutes in intervals)
            {
                if (rem <= minutes * 60 && _lastAnnouncedMinute > minutes)
                {
                    _lastAnnouncedMinute = minutes;
                    await BroadcastNoticeAsync($"[Lottery Event] {minutes} minute(s) remaining! ({active.TotalTickets} tickets bought)");
                    break;
                }
            }
        }

        // Automatic schedule checking via database (matching Collection Race)
        var utcNow = DateTime.UtcNow;
        if (utcNow.Minute != _lastAutoCheckMinute)
        {
            _lastAutoCheckMinute = utcNow.Minute;
            if (_active == null)
            {
                foreach (var eventData in _gameDataService.LotteryEventTable.Values)
                {
                    if (!eventData.AutoStart)
                        continue;

                    var schedules = _gameDataService.LotterySchedulesByEvent[eventData.Id];
                    if (schedules.Any(s => s.Matches(utcNow)))
                    {
                        _logger.LogInformation("Auto-starting lottery event '{Name}' (ID {Id}) from database schedule", eventData.Name, eventData.Id);
                        await StartAsync(eventData.Id);
                        break;
                    }
                }
            }
        }
    }

    private async Task FinishAsync(ActiveLottery active)
    {
        lock (_stateLock)
        {
            if (_active == active) _active = null;
        }

        _logger.LogInformation("Lottery event '{Name}' finished with {Count} total tickets",
            active.EventData.Name, active.TotalTickets);

        if (active.Tickets.Count == 0)
        {
            await BroadcastNoticeAsync($"[Lottery Event] '{active.EventData.Name}' ended with no participants.");
            await BroadcastPacketAsync(LotteryPacketWriter.Ended("Lottery ended with no participants.", []));
            return;
        }

        var rewards = _gameDataService.LotteryRewardsByEvent[active.EventData.Id]
            .OrderBy(r => r.Place)
            .ToList();

        var targetWinnersCount = rewards.Count;
        var shuffled = active.Tickets.OrderBy(_ => Random.Shared.Next()).ToList();
        var winners = new List<(int CharacterId, string CharacterName)>();
        foreach (var ticket in shuffled)
        {
            if (!winners.Any(w => w.CharacterId == ticket.CharacterId))
            {
                winners.Add(ticket);
                if (winners.Count == targetWinnersCount) break;
            }
        }

        var winnerInfos = new List<LotteryPacketWriter.WinnerInfo>();

        for (int i = 0; i < winners.Count; i++)
        {
            byte place = (byte)(i + 1);
            var (charId, charName) = winners[i];
            var reward = rewards[i];
            var itemName = GetRewardName(reward.ItemId);

            winnerInfos.Add(new LotteryPacketWriter.WinnerInfo(place, charName, reward.ItemId, reward.Count, itemName));

            string placeStr = place switch { 1 => "1st", 2 => "2nd", 3 => "3rd", _ => $"{place}th" };
            var itemData = _gameDataService.GetItem(reward.ItemId);
            short duration = itemData?.Duration ?? 0;

            var attachment = reward.ItemId switch
            {
                InventoryConstants.ItemGold => new MailAttachmentDraft(MailAttachmentKind.Gold, reward.ItemId, reward.Count),
                InventoryConstants.ItemExperience => new MailAttachmentDraft(MailAttachmentKind.Experience, reward.ItemId, reward.Count),
                InventoryConstants.ItemLadderPoint => new MailAttachmentDraft(MailAttachmentKind.NationalPoints, reward.ItemId, reward.Count),
                _ => new MailAttachmentDraft(MailAttachmentKind.Item, reward.ItemId, reward.Count, duration),
            };

            await _mailService.SendSystemMailAsync(
                charId,
                $"Lottery Reward - {placeStr} Prize",
                $"Congratulations {charName}! You won {placeStr} Prize in the '{active.EventData.Name}' event! Your reward ({itemName} x{reward.Count}) is attached to this mail.",
                [attachment]);

            _logger.LogInformation("Mailed {Place} prize ({Item} x{Count}) to winner {Name} ({CharId})",
                placeStr, itemName, reward.Count, charName, charId);
        }

        await BroadcastChatNoticeAsync("------ Lottery Event Winners ------");
        foreach (var w in winnerInfos)
        {
            string placeStr = w.Place switch { 1 => "1st", 2 => "2nd", 3 => "3rd", _ => $"{w.Place}th" };
            await BroadcastChatNoticeAsync($"{placeStr} Winner: {w.CharacterName} - Prize: {w.ItemName} x{w.ItemCount}");
        }
        await BroadcastChatNoticeAsync("-----------------------------------");
        await BroadcastNoticeAsync($"[Lottery Event] Ended! {winnerInfos.Count} winners have been rewarded via Mailbox!");

        await BroadcastPacketAsync(LotteryPacketWriter.Ended("Lottery completed! Check your mailbox for rewards.", winnerInfos));
    }

    private string GetRewardName(int itemId) =>
        itemId switch
        {
            InventoryConstants.ItemGold => "Noah",
            InventoryConstants.ItemExperience => "Experience",
            InventoryConstants.ItemLadderPoint => "National Points",
            _ => _gameDataService.GetItem(itemId)?.Name ?? $"Item {itemId}",
        };

    private async Task BroadcastNoticeAsync(string message)
    {
        var pkt = NoticePacketWriter.Broadcast(message);
        await BroadcastPacketAsync(pkt);
    }

    private async Task BroadcastChatNoticeAsync(string message)
    {
        var pkt = ChatPacketWriter.SystemNotice(0, message);
        await BroadcastPacketAsync(pkt);
    }

    private async Task BroadcastPacketAsync(Packet packet)
    {
        foreach (var session in _sessionManager.GetAll())
        {
            await session.Client.SendPacket(packet);
        }
    }

    private static int CountItem(UserSession player, int itemId)
    {
        var total = 0;
        for (var index = InventoryConstants.InventoryStart; index < player.Inventory.Length; index++)
        {
            if (player.Inventory[index].ItemId == itemId)
                total += player.Inventory[index].Count;
        }
        return total;
    }

    private static List<(byte SlotIndex, int ItemId, ushort Count, short Durability)> RemoveItemsSynchronous(UserSession player, int itemId, int count)
    {
        var list = new List<(byte SlotIndex, int ItemId, ushort Count, short Durability)>();
        var remaining = count;
        for (var index = InventoryConstants.InventoryStart; index < player.Inventory.Length && remaining > 0; index++)
        {
            var slot = player.Inventory[index];
            if (slot.ItemId != itemId)
                continue;

            var taken = Math.Min((int)slot.Count, remaining);
            slot.Count -= (ushort)taken;
            remaining -= taken;
            if (slot.Count == 0)
                slot.Clear();

            list.Add(((byte)index, slot.ItemId, slot.Count, slot.Durability));
        }
        return list;
    }
}
