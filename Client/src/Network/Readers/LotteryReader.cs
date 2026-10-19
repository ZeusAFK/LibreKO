namespace LibreKO.Network.Readers;

internal sealed class LotteryReader : IPacketReader
{
    private readonly LotterySession lottery;

    public LotteryReader(LotterySession lottery)
    {
        this.lottery = lottery;
    }

    public void Register(PacketDispatcher dispatcher)
    {
        dispatcher.On(GameOpcodes.GS_LOTTERY, HandleLottery);
    }

    private void HandleLottery(Packet p)
    {
        if (p.RemainingBytes < 1) return;
        var sub = (LotterySubOpcode)p.ReadByte();
        switch (sub)
        {
            case LotterySubOpcode.State:
                {
                    if (p.RemainingBytes < 1) return;
                    bool active = p.ReadByte() != 0;
                    if (!active)
                    {
                        lottery.RaiseLotteryState(new LotteryState { Active = false });
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

                    lottery.RaiseLotteryState(state);
                    break;
                }

            case LotterySubOpcode.Join:
                {
                    if (p.RemainingBytes < 1) return;
                    bool success = p.ReadByte() != 0;
                    string message = p.ReadSByteString();
                    int myTickets = p.RemainingBytes >= 4 ? p.ReadInt() : 0;
                    int totalTickets = p.RemainingBytes >= 4 ? p.ReadInt() : 0;

                    lottery.RaiseLotteryJoin(new LotteryJoinResult
                    {
                        Success = success,
                        Message = message,
                        MyTickets = myTickets,
                        TotalTickets = totalTickets
                    });
                    break;
                }

            case LotterySubOpcode.Progress:
                {
                    if (p.RemainingBytes < 4) return;
                    int totalTickets = p.ReadInt();
                    lottery.RaiseLotteryProgress(totalTickets);
                    break;
                }

            case LotterySubOpcode.Ended:
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
                    lottery.RaiseLotteryEnded(message, winners);
                    break;
                }

            case LotterySubOpcode.Close:
                {
                    lottery.RaiseLotteryClose();
                    break;
                }
        }
    }
}
