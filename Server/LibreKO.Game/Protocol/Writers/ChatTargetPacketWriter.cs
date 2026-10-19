using LibreKO.Common.Infrastructure.Network;

namespace LibreKO.Game.Protocol.Writers;

public enum ChatTargetResult : short
{
    TargetNotFound = 0,
    Connected = 1,
    TargetBlocked = -1,
    CrossNationZone = -2,
    SenderBlocked = -3,
}

public sealed class ChatTargetPacketWriter
{
    public const byte TypeWhisper = 1;
    public const byte TypeBlockToggle = 2;
    public const byte TypeClanAdmission = 4;

    public const byte AdmissionRequestSent = 0;
    public const byte AdmissionRequestReceived = 1;

    public const int MaxNameLength = 20;

    private byte type;
    private ChatTargetResult result;
    private string targetName = string.Empty;
    private byte trailingFlag;

    private static ChatTargetPacketWriter Whisper(ChatTargetResult result, string targetName) => new()
    {
        type = TypeWhisper,
        result = result,
        targetName = targetName,
    };

    public static Packet WhisperConnected(string targetName) => Whisper(ChatTargetResult.Connected, targetName).Build();
    public static Packet WhisperTargetNotFound() => Whisper(ChatTargetResult.TargetNotFound, string.Empty).Build();
    public static Packet WhisperTargetBlocked(string targetName) => Whisper(ChatTargetResult.TargetBlocked, targetName).Build();
    public static Packet WhisperCrossNationZone(string targetName) => Whisper(ChatTargetResult.CrossNationZone, targetName).Build();
    public static Packet WhisperSenderBlocked(string targetName) => Whisper(ChatTargetResult.SenderBlocked, targetName).Build();
    public static Packet ClanAdmissionFailed()
    {
        var packet = new Packet(GameOpcodes.GS_CHAT_TARGET);
        packet.WriteByte(TypeClanAdmission);
        packet.WriteShort((short)ChatTargetResult.TargetBlocked);
        return packet;
    }

    public static Packet ClanAdmissionRequestSent(string targetName) => ClanAdmission(targetName, AdmissionRequestSent).Build();
    public static Packet ClanAdmissionRequestReceived(string targetName) => ClanAdmission(targetName, AdmissionRequestReceived).Build();
    private static ChatTargetPacketWriter ClanAdmission(string targetName, byte direction) => new()
    {
        type = TypeClanAdmission,
        result = ChatTargetResult.Connected,
        targetName = targetName,
        trailingFlag = direction,
    };

    private Packet Build()
    {
        var packet = new Packet(GameOpcodes.GS_CHAT_TARGET);
        packet.WriteByte(type);
        packet.WriteShort((short)result);
        packet.WriteString(targetName);
        packet.WriteByte(trailingFlag);
        return packet;
    }
}
