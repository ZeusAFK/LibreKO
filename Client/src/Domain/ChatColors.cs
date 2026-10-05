using System;
using System.Linq;
using Godot;

namespace LibreKO.Domain;

public enum ChatColorSlot { Normal, Shout, Party, Clan, SendWhisper, ReceiveWhisper, ReceiveWhisperFriend, NoahKnightKarus, NoahKnightElMorad, Union, ChatRoom }

public enum WhisperSide { None, Sent, Received, ReceivedFromFriend }

public sealed class ChatColors
{
    public const int SlotCount = 11;
    private const char Separator = ',';

    public static readonly string[] SlotLabels =
    {
        "Normal", "Shout", "Party", "Clan", "Send Whisper", "Receive Whisper", "Receive Whisper (Friend)",
        "Noah Knight (Karus)", "Noah Knight (El Morad)", "Union", "Chat Room",
    };

    public static readonly Color[] Defaults =
    {
        new("ffffff"), new("f86605"), new("00c0c0"), new("00ff00"), new("80ffff"), new("ffff00"),
        new("80ff80"), new("87cefa"), new("ffb6c1"), new("ff6b6b"), new("8080ff"),
    };

    public static readonly Color[] Palette = Defaults.Concat(new Color[]
    {
        new("c0c0c0"), new("ffd24a"), new("ff6b6b"), new("ffa860"),
        new("ff80ff"), new("b38cff"), new("6fa8ff"), new("c6c6fb"), new("80ffff"),
    }).Distinct().ToArray();

    private static readonly Color White = new("ffffff");
    private static readonly Color Force = new("00c0c0");
    private static readonly Color Announcement = new("ffff00");
    private static readonly Color Commander = new("00ff00");
    private static readonly Color Merchant = new("c6c6fb");
    private static readonly Color SeekingParty = new("32f640");
    private static readonly Color ClanOfficer = new("64ffff");

    private readonly Color[] _slots = Defaults.ToArray();

    public Color this[ChatColorSlot slot]
    {
        get => _slots[(int)slot];
        set => _slots[(int)slot] = value;
    }

    public void Reset() => Array.Copy(Defaults, _slots, SlotCount);

    public Color ForLine(byte type, WhisperSide side = WhisperSide.None, int nation = 0) => type switch
    {
        ChatType.General => this[ChatColorSlot.Normal],
        ChatType.Private => side switch
        {
            WhisperSide.Sent => this[ChatColorSlot.SendWhisper],
            WhisperSide.ReceivedFromFriend => this[ChatColorSlot.ReceiveWhisperFriend],
            _ => this[ChatColorSlot.ReceiveWhisper],
        },
        ChatType.Party => this[ChatColorSlot.Party],
        ChatType.Forced => Force,
        ChatType.Shout => this[ChatColorSlot.Shout],
        ChatType.Clan => this[ChatColorSlot.Clan],
        ChatType.Public or ChatType.WarSystem or ChatType.Permanent or ChatType.GameMasterInfo => Announcement,
        ChatType.Command => Commander,
        ChatType.Merchant => Merchant,
        ChatType.Alliance => this[ChatColorSlot.Union],
        ChatType.SeekingParty => SeekingParty,
        ChatType.ClanOfficer => ClanOfficer,
        ChatType.ChatRoom => this[ChatColorSlot.ChatRoom],
        ChatType.ClanRecruit => nation switch
        {
            Nations.Karus => this[ChatColorSlot.NoahKnightKarus],
            Nations.ElMorad => this[ChatColorSlot.NoahKnightElMorad],
            _ => White,
        },
        _ => White,
    };

    public Color ForTab(ChatTabSpec tab) => ForLine(tab.Tint, WhisperSide.Sent);

    public Color ForInput(byte channel, bool gameMasterMode) =>
        gameMasterMode && channel == ChatType.General ? Announcement : ForLine(channel, WhisperSide.Sent);

    public string Format() => string.Join(Separator, _slots.Select(c => c.ToHtml(false)));

    public static ChatColors Parse(string text)
    {
        var colors = new ChatColors();
        string[] parts = (text ?? "").Split(Separator);
        if (parts.Length != SlotCount) return colors;
        var parsed = new Color[SlotCount];
        for (int i = 0; i < SlotCount; i++)
        {
            if (!Color.HtmlIsValid(parts[i])) return colors;
            parsed[i] = new Color(parts[i]);
        }
        Array.Copy(parsed, colors._slots, SlotCount);
        return colors;
    }
}
