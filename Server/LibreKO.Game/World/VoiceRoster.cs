using System.Net;
using System.Security.Cryptography;

namespace LibreKO.Game.World;

public static class VoiceFlags
{
    public const byte MicMuted = 0x01;
    public const byte Deafened = 0x02;
    public const byte CannotSpeak = 0x04;
    public const byte Silenced = 0x08;
    public const byte ClientSettable = MicMuted | Deafened;
}

public sealed class VoiceMember(int characterId, string roomKey, uint sessionId, byte[] key, bool canSpeak) : IDisposable
{
    public int CharacterId { get; } = characterId;
    public string RoomKey { get; } = roomKey;
    public byte[] Key { get; } = key;
    public VoiceSeal Seal { get; } = new(sessionId, key);
    public bool CanSpeak { get; } = canSpeak;
    public IPEndPoint? Endpoint { get; set; }
    public bool Tcp { get; set; }
    public byte Flags { get; set; }
    public bool Silenced { get; set; }

    internal long WindowStart;
    internal int WindowFrames;
    internal int WindowBytes;

    public uint SessionId => Seal.SessionId;
    public byte WireFlags => (byte)(Flags | (CanSpeak ? 0 : VoiceFlags.CannotSpeak) | (Silenced ? VoiceFlags.Silenced : 0));
    public bool Listening => (Flags & VoiceFlags.Deafened) == 0;

    public void Dispose() => Seal.Dispose();
}

public sealed class VoiceRoster
{
    public const int MaxFramesPerSecond = 60;
    public const int MaxBytesPerSecond = 16 * 1024;
    public const int MaxFrameBytes = 400;
    private const long WindowMs = 1000;

    private readonly object gate = new();
    private readonly Dictionary<int, VoiceMember> byCharacter = [];
    private readonly Dictionary<uint, VoiceMember> bySession = [];
    private readonly Dictionary<string, HashSet<int>> rooms = [];

    public VoiceMember? Join(int characterId, string roomKey, bool canSpeak, int maxPerRoom, out List<VoiceMember> others)
    {
        lock (gate)
        {
            RemoveLocked(characterId);
            var room = rooms.TryGetValue(roomKey, out var set) ? set : rooms[roomKey] = [];
            others = room.Select(id => byCharacter[id]).ToList();
            if (maxPerRoom > 0 && room.Count >= maxPerRoom)
                return null;
            uint session;
            do session = (uint)RandomNumberGenerator.GetInt32(1, int.MaxValue);
            while (bySession.ContainsKey(session));
            var member = new VoiceMember(characterId, roomKey, session, RandomNumberGenerator.GetBytes(VoiceSeal.KeyLength), canSpeak);
            room.Add(characterId);
            byCharacter[characterId] = member;
            bySession[session] = member;
            return member;
        }
    }

    public VoiceMember? Leave(int characterId, out List<int> others)
    {
        lock (gate)
        {
            var member = RemoveLocked(characterId);
            others = member != null && rooms.TryGetValue(member.RoomKey, out var room) ? [.. room] : [];
            return member;
        }
    }

    public VoiceMember? Get(int characterId)
    {
        lock (gate) return byCharacter.GetValueOrDefault(characterId);
    }

    public VoiceMember? BySession(uint sessionId)
    {
        lock (gate) return bySession.GetValueOrDefault(sessionId);
    }

    public List<VoiceMember> MembersOf(string roomKey)
    {
        lock (gate)
            return rooms.TryGetValue(roomKey, out var room) ? room.Select(id => byCharacter[id]).ToList() : [];
    }

    public List<VoiceMember> ListenersOf(VoiceMember speaker)
    {
        lock (gate)
        {
            if (!rooms.TryGetValue(speaker.RoomKey, out var room)) return [];
            var listeners = new List<VoiceMember>(room.Count);
            foreach (int id in room)
            {
                var member = byCharacter[id];
                if (id != speaker.CharacterId && member.Listening) listeners.Add(member);
            }
            return listeners;
        }
    }

    public bool AllowFrame(VoiceMember speaker, int bytes, long nowMs)
    {
        if (!speaker.CanSpeak || speaker.Silenced || (speaker.Flags & VoiceFlags.MicMuted) != 0 || bytes is 0 or > MaxFrameBytes)
            return false;
        lock (gate)
        {
            if (nowMs - speaker.WindowStart >= WindowMs)
            {
                speaker.WindowStart = nowMs;
                speaker.WindowFrames = 0;
                speaker.WindowBytes = 0;
            }
            if (speaker.WindowFrames >= MaxFramesPerSecond || speaker.WindowBytes + bytes > MaxBytesPerSecond)
                return false;
            speaker.WindowFrames++;
            speaker.WindowBytes += bytes;
            return true;
        }
    }

    public int CountIn(string roomKey)
    {
        lock (gate) return rooms.TryGetValue(roomKey, out var room) ? room.Count : 0;
    }

    private VoiceMember? RemoveLocked(int characterId)
    {
        if (!byCharacter.Remove(characterId, out var member))
            return null;
        bySession.Remove(member.SessionId);
        if (rooms.TryGetValue(member.RoomKey, out var room))
        {
            room.Remove(characterId);
            if (room.Count == 0) rooms.Remove(member.RoomKey);
        }
        member.Dispose();
        return member;
    }
}
