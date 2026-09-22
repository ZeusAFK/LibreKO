using System;
using System.Collections.Generic;

namespace LibreKO.Network;

public partial class Net
{
    public event Action<CollectionRaceState>? CollectionRaceStateEvent;
    public event Action<int, int[]>? CollectionRaceProgressEvent;
    public event Action<string>? CollectionRaceCompletedEvent;
    public event Action? CollectionRaceCloseEvent;

    public const byte SubState = 1;
    public const byte SubProgress = 2;
    public const byte SubCompleted = 3;
    public const byte SubClose = 4;

    private const int MinStateBytes = 12;
    private const int MinObjectiveEntryBytes = 14;
    private const int MinRewardEntryBytes = 10;

    private void HandleCollectionRace(Packet p)
    {
        if (p.RemainingBytes < 1) return;
        var sub = p.ReadByte();
        switch (sub)
        {
            case SubState:
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

                CollectionRaceStateEvent?.Invoke(state);
                break;
            }

            case SubProgress:
            {
                if (p.RemainingBytes < 5) return;
                var raceId = p.ReadInt();
                int count = p.ReadByte();
                if (p.RemainingBytes < count * 4) return;
                var currents = new int[count];
                for (int i = 0; i < count; i++)
                    currents[i] = p.ReadInt();
                CollectionRaceProgressEvent?.Invoke(raceId, currents);
                break;
            }

            case SubCompleted:
            {
                var msg = p.ReadSByteString();
                CollectionRaceCompletedEvent?.Invoke(msg);
                break;
            }

            case SubClose:
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

public enum CollectionRaceObjectiveKind : byte
{
    Monster = 0,
    EnemyPlayer = 1,
    Item = 2,
}

public class CollectionRaceObjective
{
    public CollectionRaceObjectiveKind Kind;
    public int TargetId;
    public int Count;
    public int Current;
    public string Name = string.Empty;
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
    public int RaceId;
    public string Name = string.Empty;
    public byte ZoneId;
    public int RemainingSeconds;
    public bool IsCompleted;
    public List<CollectionRaceObjective> Objectives = [];
    public List<CollectionRaceReward> Rewards = [];
}
