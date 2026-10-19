using System.Collections.Generic;

namespace LibreKO.Network;

public sealed class LotteryState
{
    public bool Active { get; set; }
    public int LotteryId { get; set; }
    public string Name { get; set; } = string.Empty;
    public int RemainingSeconds { get; set; }
    public int UserLimit { get; set; }
    public int TotalTickets { get; set; }
    public int MyTickets { get; set; }
    public int ReqItemId { get; set; }
    public int ReqItemCount { get; set; }
    public string ReqItemName { get; set; } = string.Empty;
    public List<LotteryReward> Rewards { get; } = [];
}

public sealed class LotteryReward
{
    public int ItemId { get; set; }
    public int ItemCount { get; set; }
    public string Name { get; set; } = string.Empty;
}

public sealed class LotteryJoinResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public int MyTickets { get; set; }
    public int TotalTickets { get; set; }
}

public sealed class LotteryWinner
{
    public byte Place { get; set; }
    public string CharacterName { get; set; } = string.Empty;
    public int ItemId { get; set; }
    public int ItemCount { get; set; }
    public string ItemName { get; set; } = string.Empty;
}
