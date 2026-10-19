namespace LibreKO.Network.Readers;

internal sealed class CollectionRaceReader : IPacketReader
{
    private readonly CollectionRaceSession collectionRace;

    private const int MinStateBytes = 12;
    private const int MinObjectiveEntryBytes = 14;
    private const int MinRewardEntryBytes = 10;
    private const int WinnersBytes = 8;
    private const int WinnersUpdateBytes = 12;

    public CollectionRaceReader(CollectionRaceSession collectionRace)
    {
        this.collectionRace = collectionRace;
    }

    public void Register(PacketDispatcher dispatcher)
    {
        dispatcher.On(GameOpcodes.GS_COLLECTION_RACE, HandleCollectionRace);
    }

    private void HandleCollectionRace(Packet p)
    {
        if (p.RemainingBytes < 1) return;
        var sub = (CollectionRaceSubOpcode)p.ReadByte();
        switch (sub)
        {
            case CollectionRaceSubOpcode.State:
                {
                    if (p.RemainingBytes < MinStateBytes) return;
                    var state = new CollectionRaceState
                    {
                        RaceId = p.ReadInt(),
                        Name = p.ReadSByteString(),
                        ZoneId = p.ReadByte(),
                        RemainingSeconds = p.ReadInt(),
                        IsCompleted = p.ReadByte() != 0
                    };

                    int objectiveCount = p.ReadByte();
                    for (int i = 0; i < objectiveCount && p.RemainingBytes >= MinObjectiveEntryBytes; i++)
                    {
                        state.Objectives.Add(new CollectionRaceObjective
                        {
                            Kind = (CollectionRaceObjectiveKind)p.ReadByte(),
                            TargetId = p.ReadInt(),
                            Count = p.ReadInt(),
                            Current = p.ReadInt(),
                            Name = p.ReadSByteString()
                        });
                    }

                    if (p.RemainingBytes >= 1)
                    {
                        int rewardCount = p.ReadByte();
                        for (int i = 0; i < rewardCount && p.RemainingBytes >= MinRewardEntryBytes; i++)
                        {
                            state.Rewards.Add(new CollectionRaceReward
                            {
                                ItemId = p.ReadInt(),
                                ItemCount = p.ReadInt(),
                                Name = p.ReadSByteString(),
                                Rate = p.ReadByte()
                            });
                        }
                    }

                    if (p.RemainingBytes >= WinnersBytes)
                    {
                        state.Winners = p.ReadInt();
                        state.MaxWinners = p.ReadInt();
                    }

                    collectionRace.RaiseCollectionRaceState(state);
                    break;
                }

            case CollectionRaceSubOpcode.Winners:
                {
                    if (p.RemainingBytes < WinnersUpdateBytes) return;
                    int raceId = p.ReadInt();
                    int winners = p.ReadInt();
                    int maxWinners = p.ReadInt();
                    collectionRace.RaiseCollectionRaceWinners(raceId, winners, maxWinners);
                    break;
                }

            case CollectionRaceSubOpcode.Progress:
                {
                    if (p.RemainingBytes < 5) return;
                    var raceId = p.ReadInt();
                    int count = p.ReadByte();
                    if (p.RemainingBytes < count * 4) return;
                    var currents = new int[count];
                    for (int i = 0; i < count; i++)
                        currents[i] = p.ReadInt();
                    collectionRace.RaiseCollectionRaceProgress(raceId, currents);
                    break;
                }

            case CollectionRaceSubOpcode.Completed:
                {
                    var msg = p.ReadSByteString();
                    collectionRace.RaiseCollectionRaceCompleted(msg);
                    break;
                }

            case CollectionRaceSubOpcode.Close:
                {
                    collectionRace.RaiseCollectionRaceClose();
                    break;
                }
        }
    }
}
