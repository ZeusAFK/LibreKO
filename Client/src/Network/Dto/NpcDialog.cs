using System.Collections.Generic;

namespace LibreKO.Network;

public sealed class NpcDialog
{
    public int NpcId;
    public int Flag;
    public int QuestId;
    public int HeaderTextId;
    public string ScriptFile = "";

    public string? HeaderText;

    public readonly List<(int Index, int TextId, string? Text)> Buttons = new();

    public static NpcDialog Read(Packet packet)
    {
        var dialog = new NpcDialog
        {
            NpcId = packet.ReadInt(),
            Flag = packet.ReadByte(),
            QuestId = packet.ReadInt(),
            HeaderTextId = packet.ReadInt(),
        };
        var ids = new List<int>(12);
        for (var index = 0; index < 12; index++)
            ids.Add(packet.ReadInt());
        dialog.ScriptFile = packet.RemainingBytes > 0 ? packet.ReadSByteString().Trim() : "";
        var texts = ReadText(packet, true, out var header, ids);
        dialog.HeaderText = header;
        for (var index = 0; index < ids.Count; index++)
        {
            var text = index < texts.Count ? texts[index] : null;
            if (ids[index] >= 0 || !string.IsNullOrEmpty(text))
                dialog.Buttons.Add((index, ids[index], text));
        }
        return dialog;
    }

    public static List<string> ReadText(Packet packet, bool hasHeader, out string? header) =>
        ReadText(packet, hasHeader, out header, null);

    private static List<string> ReadText(Packet packet, bool hasHeader, out string? header, List<int>? ids)
    {
        header = null;
        var texts = new List<string>();
        if (packet.RemainingBytes == 0)
            return texts;
        var magic = packet.ReadUInt();
        if (magic != LibreKOProtocol.DialogTextMagic && magic != LibreKOProtocol.DialogChoicesMagic)
            throw new InvalidDataException("Unknown NPC dialog extension.");
        if (packet.ReadByte() != LibreKOProtocol.ExtensionVersion)
            throw new InvalidDataException("Unknown NPC dialog version.");
        if (magic == LibreKOProtocol.DialogChoicesMagic)
        {
            if (!hasHeader || ids is null)
                throw new InvalidDataException("NPC choices require a dialog.");
            var body = packet.ReadUtf8String();
            header = body.Length > 0 ? body : null;
            int choices = (ushort)packet.ReadShort();
            if (choices is < 13 or > 256)
                throw new InvalidDataException("Invalid NPC choice count.");
            ids.Clear();
            for (var index = 0; index < choices; index++)
            {
                ids.Add(packet.ReadInt());
                texts.Add(packet.ReadUtf8String());
            }
            return texts;
        }
        if (hasHeader)
        {
            var text = packet.ReadUtf8String();
            header = text.Length > 0 ? text : null;
        }
        var count = packet.ReadByte();
        if (count > (hasHeader ? 12 : 8))
            throw new InvalidDataException("Too many NPC dialog lines.");
        for (var index = 0; index < count; index++)
            texts.Add(packet.ReadUtf8String());
        return texts;
    }
}
