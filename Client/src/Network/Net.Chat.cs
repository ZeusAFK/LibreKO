using System;

namespace LibreKO.Network;

public partial class Net
{
    public const int WhisperLogMax = 120;

    public const byte SubChatTargetWhisper = 1, SubChatTargetBlock = 2;

    public const byte ChatAuthorityPlayer = 1, ChatAuthorityGameMaster = 2;

    public const int ChatTargetNotFound = 0, ChatTargetConnected = 1, ChatTargetBlocked = -1,
                     ChatTargetCrossNation = -2, ChatTargetSenderBlocked = -3;

    public readonly struct WhisperLine
    {
        public readonly bool Mine;
        public readonly bool Notice;
        public readonly string Text;

        public WhisperLine(bool mine, bool notice, string text)
        {
            Mine = mine; Notice = notice; Text = text;
        }
    }

    private readonly System.Collections.Generic.Dictionary<string, System.Collections.Generic.List<WhisperLine>> _whisperLog =
        new(StringComparer.OrdinalIgnoreCase);

    public System.Collections.Generic.IReadOnlyList<WhisperLine> WhisperHistory(string name) =>
        _whisperLog.TryGetValue(name, out var log)
            ? log
            : System.Array.Empty<WhisperLine>();

    public void RecordWhisper(string name, bool mine, bool notice, string text)
    {
        if (!_whisperLog.TryGetValue(name, out var log))
        {
            log = new System.Collections.Generic.List<WhisperLine>();
            _whisperLog[name] = log;
        }
        log.Add(new WhisperLine(mine, notice, text));
        if (log.Count > WhisperLogMax) log.RemoveRange(0, log.Count - WhisperLogMax);
    }

    public event Action<ChatLine>? ChatEvent;

    public readonly record struct DeathNotice(
        int VictimNation, int KillerNation, int Kind, int KillerId, string KillerName,
        int VictimId, string VictimName, int X, int Z);

    public event Action<DeathNotice>? DeathNoticeEvent;

    public const byte ChatTypeDeathNotice = 26;
    private const int DeathNoticeMinBytes = 4 + 4 + 1 + 4 + 1 + 4;

    public event Action<int, string>? ChatTargetEvent;

    private void HandleChat(Packet p)
    {
        if (p.RemainingBytes < 1) return;
        byte type = p.ReadByte();
        if (type == ChatTypeDeathNotice)
        {
            HandleDeathNotice(p);
            return;
        }
        int nation = p.RemainingBytes >= 1 ? p.ReadByte() : 0;
        int charId = p.RemainingBytes >= 4 ? p.ReadInt() : 0;
        string name = p.RemainingBytes >= 1 ? p.ReadSByteString() : "";
        string message = p.RemainingBytes >= 2 ? p.ReadString() : "";
        bool isGm = p.RemainingBytes >= 1 && p.ReadByte() == ChatAuthorityGameMaster;

        if (message.Length == 0) return;
        ChatEvent?.Invoke(new ChatLine(type, nation, charId, name, message, isGm));
    }

    private void HandleDeathNotice(Packet p)
    {
        if (p.RemainingBytes < DeathNoticeMinBytes) return;
        int victimNation = p.ReadByte();
        int killerNation = p.ReadByte();
        p.ReadByte();
        int kind = p.ReadByte();
        int killerId = p.ReadInt();
        string killerName = p.ReadSByteString();
        if (p.RemainingBytes < 5) return;
        int victimId = p.ReadInt();
        string victimName = p.ReadSByteString();
        int x = p.RemainingBytes >= 2 ? p.ReadShort() : 0;
        int z = p.RemainingBytes >= 2 ? p.ReadShort() : 0;
        DeathNoticeEvent?.Invoke(new DeathNotice(victimNation, killerNation, kind, killerId, killerName,
                                                 victimId, victimName, x, z));
    }

    private void HandleChatTarget(Packet p)
    {
        if (p.RemainingBytes < 3) return;
        byte type = p.ReadByte();
        if (type != SubChatTargetWhisper) return;
        short result = p.ReadShort();
        string name = p.RemainingBytes >= 2 ? p.ReadString() : "";
        ChatTargetEvent?.Invoke(result, name);
    }
}
