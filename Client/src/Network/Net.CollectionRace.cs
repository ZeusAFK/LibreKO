using System;
using System.Collections.Generic;

namespace LibreKO.Network;

public partial class Net
{
    public event Action<CollectionRaceState>? CollectionRaceStateEvent;
    public event Action<int, int, int, int>? CollectionRaceProgressEvent;
    public event Action<string>? CollectionRaceCompletedEvent;
    public event Action? CollectionRaceCloseEvent;

    private void HandleCollectionRace(Packet p)
    {
        if (p.RemainingBytes < 1) return;
        var sub = p.ReadByte();
        switch (sub)
        {
            case 1: // State
            {
                if (p.RemainingBytes < 9) return;
                var state = new CollectionRaceState
                {
                    EventIndex = p.ReadInt(),
                    EventName = p.ReadSByteString(),
                    ZoneId = p.ReadByte(),
                    RemainingSeconds = p.ReadInt(),
                    Target1 = new CollectionRaceTarget
                    {
                        ProtoId = p.ReadInt(),
                        TargetCount = p.ReadInt(),
                        CurrentCount = p.ReadInt(),
                        Name = p.ReadSByteString()
                    },
                    Target2 = new CollectionRaceTarget
                    {
                        ProtoId = p.ReadInt(),
                        TargetCount = p.ReadInt(),
                        CurrentCount = p.ReadInt(),
                        Name = p.ReadSByteString()
                    },
                    Target3 = new CollectionRaceTarget
                    {
                        ProtoId = p.ReadInt(),
                        TargetCount = p.ReadInt(),
                        CurrentCount = p.ReadInt(),
                        Name = p.ReadSByteString()
                    },
                    EnemyTarget = p.ReadInt(),
                    EnemyCurrent = p.ReadInt(),
                    IsCompleted = p.ReadByte() != 0
                };

                if (p.RemainingBytes >= 1)
                {
                    int rewardCount = p.ReadByte();
                    for (int i = 0; i < rewardCount && p.RemainingBytes >= 8; i++)
                    {
                        var itemId = p.ReadInt();
                        var count = p.ReadInt();
                        var name = p.ReadSByteString();
                        byte rate = p.RemainingBytes >= 1 ? p.ReadByte() : (byte)100;
                        state.Rewards.Add(new CollectionRaceReward
                        {
                            ItemId = itemId,
                            ItemCount = count,
                            Name = name,
                            Rate = rate
                        });
                    }
                }

                CollectionRaceStateEvent?.Invoke(state);
                break;
            }

            case 2: // Progress
            {
                if (p.RemainingBytes < 16) return;
                int t1 = p.ReadInt();
                int t2 = p.ReadInt();
                int t3 = p.ReadInt();
                int enemy = p.ReadInt();
                CollectionRaceProgressEvent?.Invoke(t1, t2, t3, enemy);
                break;
            }

            case 3: // Completed
            {
                var msg = p.ReadSByteString();
                CollectionRaceCompletedEvent?.Invoke(msg);
                break;
            }

            case 4: // Close
            {
                CollectionRaceCloseEvent?.Invoke();
                break;
            }
        }
    }

    public void SendCollectionRaceRequest()
    {
        var p = new Packet(GameOpcodes.GS_COLLECTION_RACE);
        _conn.Send(p);
    }
}

public struct CollectionRaceTarget
{
    public int ProtoId;
    public int TargetCount;
    public int CurrentCount;
    public string Name;
}

public struct CollectionRaceReward
{
    public int ItemId;
    public int ItemCount;
    public string Name;
    public byte Rate;
}

public class CollectionRaceState
{
    public int EventIndex;
    public string EventName = string.Empty;
    public byte ZoneId;
    public int RemainingSeconds;
    public CollectionRaceTarget Target1;
    public CollectionRaceTarget Target2;
    public CollectionRaceTarget Target3;
    public int EnemyTarget;
    public int EnemyCurrent;
    public bool IsCompleted;
    public List<CollectionRaceReward> Rewards = [];
}
