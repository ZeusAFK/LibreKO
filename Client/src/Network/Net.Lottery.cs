using System;
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

public partial class Net
{
    public event Action<LotteryState>? LotteryStateEvent;
    public event Action<LotteryJoinResult>? LotteryJoinEvent;
    public event Action<int>? LotteryProgressEvent;
    public event Action<string, List<LotteryWinner>>? LotteryEndedEvent;
    public event Action? LotteryCloseEvent;

    public const byte SubLotteryState = 1;
    public const byte SubLotteryJoinAck = 2;
    public const byte SubLotteryProgress = 3;
    public const byte SubLotteryEnded = 4;
    public const byte SubLotteryClose = 5;

    public void SendLotteryStateRequest()
    {
        var packet = new Packet(GameOpcodes.GS_LOTTERY);
        packet.WriteByte(SubLotteryState);
        _conn.Send(packet);
    }

    public void SendLotteryBuyTicket()
    {
        var packet = new Packet(GameOpcodes.GS_LOTTERY);
        packet.WriteByte(SubLotteryJoinAck);
        _conn.Send(packet);
    }

    private void HandleLottery(Packet p)
    {
        if (p.RemainingBytes < 1) return;
        var sub = p.ReadByte();
        switch (sub)
        {
            case SubLotteryState:
            {
                if (p.RemainingBytes < 1) return;
                bool active = p.ReadByte() != 0;
                if (!active)
                {
                    LotteryStateEvent?.Invoke(new LotteryState { Active = false });
                    return;
                }

                if (p.RemainingBytes < 20) return;
                var state = new LotteryState
                {
                    Active = true,
                    LotteryId = p.ReadInt(),
                    Name = p.ReadSByteString(),
                    RemainingSeconds = p.ReadInt(),
                    UserLimit = p.ReadInt(),
                    TotalTickets = p.ReadInt(),
                    MyTickets = p.ReadInt(),
                    ReqItemId = p.ReadInt(),
                    ReqItemCount = p.ReadInt(),
                    ReqItemName = p.ReadSByteString()
                };

                if (p.RemainingBytes >= 1)
                {
                    int rewardCount = p.ReadByte();
                    for (int i = 0; i < rewardCount && p.RemainingBytes >= 8; i++)
                    {
                        state.Rewards.Add(new LotteryReward
                        {
                            ItemId = p.ReadInt(),
                            ItemCount = p.ReadInt(),
                            Name = p.ReadSByteString()
                        });
                    }
                }

                LotteryStateEvent?.Invoke(state);
                break;
            }

            case SubLotteryJoinAck:
            {
                if (p.RemainingBytes < 1) return;
                bool success = p.ReadByte() != 0;
                string message = p.ReadSByteString();
                int myTickets = p.RemainingBytes >= 4 ? p.ReadInt() : 0;
                int totalTickets = p.RemainingBytes >= 4 ? p.ReadInt() : 0;

                LotteryJoinEvent?.Invoke(new LotteryJoinResult
                {
                    Success = success,
                    Message = message,
                    MyTickets = myTickets,
                    TotalTickets = totalTickets
                });
                break;
            }

            case SubLotteryProgress:
            {
                if (p.RemainingBytes < 4) return;
                int totalTickets = p.ReadInt();
                LotteryProgressEvent?.Invoke(totalTickets);
                break;
            }

            case SubLotteryEnded:
            {
                string message = p.ReadSByteString();
                var winners = new List<LotteryWinner>();
                if (p.RemainingBytes >= 1)
                {
                    int count = p.ReadByte();
                    for (int i = 0; i < count && p.RemainingBytes >= 9; i++)
                    {
                        winners.Add(new LotteryWinner
                        {
                            Place = p.ReadByte(),
                            CharacterName = p.ReadSByteString(),
                            ItemId = p.ReadInt(),
                            ItemCount = p.ReadInt(),
                            ItemName = p.ReadSByteString()
                        });
                    }
                }
                LotteryEndedEvent?.Invoke(message, winners);
                break;
            }

            case SubLotteryClose:
            {
                LotteryCloseEvent?.Invoke();
                break;
            }
        }
    }
}
