using LibreKO.Common.Gameplay;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.World;

namespace LibreKO.Game.Protocol.Writers;

public sealed class NpcDialogPacketWriter
{
    public const int NoText = -1;
    public const int NpcSayLines = 8;
    public const byte ObjectEventEffect = 11;
    public const byte ObjectEventShown = 1;
    public const byte RebirthPanelStyle = 48;
    public const byte FamiliarPanelStyle = 9;
    public const byte FamiliarShopStyle = 14;
    public const byte GenderChangePanelStyle = 53;
    public const byte FortunePanelStyle = 16;
    public const byte SpecialAuctionPanelStyle = 58;
    public const byte ItemCombinePanelStyle = 18;
    public const byte CombineRecipeBookStyle = 21;

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

    public static Packet RebirthPanel(int npcId, string scriptFile) =>
        SelectMessage(npcId, RebirthPanelStyle, NoText, NoText, [], UserSession.SelectMessageEventCount, scriptFile);

    public static Packet FamiliarPanel(int npcId, string scriptFile) =>
        SelectMessage(npcId, FamiliarPanelStyle, NoText, NoText, [], UserSession.SelectMessageEventCount, scriptFile);

    public static Packet GenderChangePanel(int npcId, string scriptFile) =>
        SelectMessage(npcId, GenderChangePanelStyle, NoText, NoText, [], UserSession.SelectMessageEventCount, scriptFile);

    public static Packet FortunePanel(int npcId, string scriptFile) =>
        SelectMessage(npcId, FortunePanelStyle, NoText, NoText, [], UserSession.SelectMessageEventCount, scriptFile);

    public static Packet SpecialAuctionPanel(int npcId, string scriptFile) =>
        SelectMessage(npcId, SpecialAuctionPanelStyle, NoText, NoText, [], UserSession.SelectMessageEventCount, scriptFile);

    public static Packet ItemCombinePanel(int npcId, string scriptFile) =>
        SelectMessage(npcId, ItemCombinePanelStyle, NoText, NoText, [], UserSession.SelectMessageEventCount, scriptFile);

    public static Packet CombineRecipeBook(int npcId, string scriptFile) =>
        SelectMessage(npcId, CombineRecipeBookStyle, NoText, NoText, [], UserSession.SelectMessageEventCount, scriptFile);

    public static Packet FamiliarShop(int npcId, string scriptFile) =>
        SelectMessage(npcId, FamiliarShopStyle, NoText, NoText, [], UserSession.SelectMessageEventCount, scriptFile);

    public static Packet Effect(int entityId, int effectId)
    {
        var packet = new Packet(GameOpcodes.GS_OBJECT_EVENT);
        packet.WriteByte(ObjectEventEffect);
        packet.WriteByte(ObjectEventShown);
        packet.WriteInt(entityId);
        packet.WriteInt(effectId);
        return packet;
    }
}
