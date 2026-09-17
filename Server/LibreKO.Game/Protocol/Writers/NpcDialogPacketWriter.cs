using LibreKO.Common.Gameplay;
using LibreKO.Common.Infrastructure.Network;

namespace LibreKO.Game.Protocol.Writers;

public sealed class NpcDialogPacketWriter
{
    public const int NoText = -1;
    public const int NpcSayLines = 8;
    public const byte ObjectEventEffect = 3;

    public static Packet NpcSay(IReadOnlyList<int> textIds) => NpcSay(textIds, null);

    public static Packet NpcSay(IReadOnlyList<int> textIds, IReadOnlyList<string>? lines)
    {
        var packet = new Packet(GameOpcodes.GS_NPC_SAY);
        packet.WriteInt(NoText);
        packet.WriteInt(NoText);

        for (var index = 0; index < NpcSayLines; index++)
            packet.WriteInt(index < textIds.Count ? textIds[index] : NoText);

        if (lines is null || lines.Count == 0)
            return packet;

        packet.WriteUInt(GameplayProtocol.DialogTextMagic);
        packet.WriteByte(GameplayProtocol.ExtensionVersion);
        packet.WriteByte((byte)Math.Min(lines.Count, NpcSayLines));
        for (var index = 0; index < lines.Count && index < NpcSayLines; index++)
            packet.WriteUtf8String(lines[index]);

        return packet;
    }

    public static Packet SelectMessage(
        int npcId, byte flag, int questId, int headerTextId,
        IReadOnlyList<int> buttonTextIds, int buttonSlots, string scriptFile) =>
        SelectMessage(npcId, flag, questId, headerTextId, buttonTextIds, buttonSlots, scriptFile, null, null);

    public static Packet SelectMessage(
        int npcId, byte flag, int questId, int headerTextId,
        IReadOnlyList<int> buttonTextIds, int buttonSlots, string scriptFile,
        string? headerText, IReadOnlyList<string>? buttonTexts)
    {
        var packet = new Packet(GameOpcodes.GS_SELECT_MSG);
        packet.WriteInt(npcId);
        packet.WriteByte(flag);
        packet.WriteInt(questId);
        packet.WriteInt(headerTextId);

        for (var index = 0; index < buttonSlots; index++)
            packet.WriteInt(index < buttonTextIds.Count ? buttonTextIds[index] : NoText);

        packet.WriteSByteString(scriptFile);

        var choiceCount = Math.Max(buttonTextIds.Count, buttonTexts?.Count ?? 0);
        if (choiceCount > buttonSlots)
        {
            if (choiceCount > 256)
                throw new ArgumentOutOfRangeException(nameof(buttonTextIds));
            packet.WriteUInt(GameplayProtocol.DialogChoicesMagic);
            packet.WriteByte(GameplayProtocol.ExtensionVersion);
            packet.WriteUtf8String(headerText ?? string.Empty);
            packet.WriteUShort((ushort)choiceCount);
            for (var index = 0; index < choiceCount; index++)
            {
                packet.WriteInt(index < buttonTextIds.Count ? buttonTextIds[index] : NoText);
                packet.WriteUtf8String(index < (buttonTexts?.Count ?? 0) ? buttonTexts![index] : string.Empty);
            }
            return packet;
        }

        if (headerText is null && buttonTexts is null)
            return packet;

        packet.WriteUInt(GameplayProtocol.DialogTextMagic);
        packet.WriteByte(GameplayProtocol.ExtensionVersion);
        packet.WriteUtf8String(headerText ?? string.Empty);
        var count = Math.Min(buttonTexts?.Count ?? 0, buttonSlots);
        packet.WriteByte((byte)count);
        for (var index = 0; index < count; index++)
            packet.WriteUtf8String(buttonTexts![index]);

        return packet;
    }

    public static Packet Effect(short entityId, int effectId)
    {
        var packet = new Packet(GameOpcodes.GS_OBJECT_EVENT);
        packet.WriteByte(ObjectEventEffect);
        packet.WriteShort(entityId);
        packet.WriteInt(effectId);
        return packet;
    }
}
