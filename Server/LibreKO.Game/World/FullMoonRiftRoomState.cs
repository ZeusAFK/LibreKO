using System;
using System.Collections.Concurrent;

namespace LibreKO.Game.World;

public sealed class FullMoonRiftRoomState
{
    public ushort RoomId { get; init; }
    public byte Difficulty { get; init; }
    public int CurrentStageId { get; set; }
    public int RemainingMonsters;
    public DateTime StartTime { get; init; }
    public DateTime NextSpawnTime { get; set; }
    public DateTime Deadline { get; set; }
    public bool WaveActive { get; set; }
    public bool Completed { get; set; }
    public bool Failed { get; set; }
    public DateTime? ClosesAt { get; set; }
    public ConcurrentDictionary<int, byte> Participants { get; } = new();
}