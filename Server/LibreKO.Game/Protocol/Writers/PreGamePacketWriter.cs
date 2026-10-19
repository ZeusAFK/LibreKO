using LibreKO.Common.Enums;
using LibreKO.Common.Infrastructure.Network;

namespace LibreKO.Game.Protocol.Writers;

public sealed class PreGamePacketWriter
{
    public const int CharSelectEquipmentSlots = 17;
    public const byte CharacterListReady = 1;
    public const short NoOldVictory = -1;
    public const byte DeleteFailedSlot = 0xFF;
    public const byte NationRejected = 0;
    public const byte LoginQueueReady = 1;
    public const byte NoStory = 0;
    public const byte NameChangeFaceOffset = 100;

    public readonly record struct CharSelectItem(int ItemId, short Durability);

    public readonly record struct CharacterSlot(
        string Name,
        byte Race,
        short Class,
        byte Level,
        byte Rebirth,
        byte Face,
        int Hair,
        short ZoneId,
        IReadOnlyList<CharSelectItem> Equipment,
        bool NameChangeRequired = false);

    public static Packet NationSelect(byte nation)
    {
        var packet = new Packet(GameOpcodes.GS_NATION_SELECT);
        packet.WriteByte(nation);
        return packet;
    }

    public static Packet NameChangeRefused(SelectingCharacterNameChangeResult result = SelectingCharacterNameChangeResult.Failed) =>
        NameChangeReply(result);

    public static Packet NameChanged(short slot, string name)
    {
        var packet = NameChangeReply(SelectingCharacterNameChangeResult.Success);
        packet.WriteShort(slot);
        packet.WriteString(name);
        return packet;
    }

    public static Packet LoginQueue(byte result, int waiting)
    {
        var packet = new Packet(GameOpcodes.GS_LOADING_LOGIN);
        packet.WriteByte(result);
        packet.WriteInt(waiting);
        return packet;
    }

    public static Packet DeleteCharacter(DeleteCharacterResult result, byte slot)
    {
        var packet = new Packet(GameOpcodes.GS_DELETE_CHARACTER);
        packet.WriteShort((short)result);
        packet.WriteByte(slot);
        return packet;
    }

    public static Packet SelectCharacterFailure(byte result)
    {
        var packet = new Packet(GameOpcodes.GS_SELECT_CHARACTER);
        packet.WriteByte(result);
        return packet;
    }

    public const byte ChangeHairSucceeded = 0;
    public const byte ChangeHairFailed = 1;
    public const byte ChangeHairOpenShop = 2;
    private const uint NoMakeOverExpiry = 0;

    public static Packet ChangeHairShop() => ChangeHairResult(ChangeHairOpenShop);

    public static Packet ChangeHairApplied()
    {
        var packet = ChangeHairResult(ChangeHairSucceeded);
        packet.WriteUInt(NoMakeOverExpiry);
        return packet;
    }

    public static Packet ChangeHairResult(byte result)
    {
        var packet = new Packet(GameOpcodes.GS_CHANGE_HAIR);
        packet.WriteByte(result);
        return packet;
    }

    public static Packet GameStart() => new(GameOpcodes.GS_GAMESTART);

    public static Packet CreateCharacterResult(byte result)
    {
        var packet = new Packet(GameOpcodes.GS_CREATE_CHARACTER);
        packet.WriteByte(result);
        return packet;
    }

    public static Packet Story(byte storyId, int storyData)
    {
        var packet = new Packet(GameOpcodes.GS_STORY);
        packet.WriteByte((byte)PreGameSubOpcode.StoryShow);
        packet.WriteByte(storyId);
        packet.WriteInt(storyData);
        return packet;
    }

    public static Packet AllCharacterInfo(IReadOnlyList<CharacterSlot?> slots)
    {
        var packet = new Packet(GameOpcodes.GS_ALLCHAR_INFO_REQ);
        packet.WriteByte((byte)AllCharacterInfoOpcode.CharacterList);
        packet.WriteByte(CharacterListReady);

        foreach (var slot in slots)
            WriteSlot(packet, slot);

        return packet;
    }

    public static Packet SelectCharacterSuccess(
        byte result, short zoneId, short posX, short posZ, short posY, byte nation)
    {
        var packet = new Packet(GameOpcodes.GS_SELECT_CHARACTER);
        packet.WriteByte(result);
        packet.WriteShort(zoneId);
        packet.WriteShort(posX);
        packet.WriteShort(posZ);
        packet.WriteShort(posY);
        packet.WriteByte(nation);
        packet.WriteShort(NoOldVictory);
        return packet;
    }

    private static Packet NameChangeReply(SelectingCharacterNameChangeResult result)
    {
        var packet = new Packet(GameOpcodes.GS_ALLCHAR_INFO_REQ);
        packet.WriteByte((byte)AllCharacterInfoOpcode.NameChange);
        packet.WriteByte((byte)result);
        return packet;
    }

    internal static void WriteSlot(Packet packet, CharacterSlot? slot)
    {
        if (slot is not { } character)
        {
            packet.WriteString(string.Empty);
            packet.WriteByte(0);
            packet.WriteShort(0);
            packet.WriteByte(0);
            packet.WriteByte(0);
            packet.WriteByte(0);
            packet.WriteInt(0);
            packet.WriteShort(0);

            for (var index = 0; index < CharSelectEquipmentSlots; index++)
            {
                packet.WriteInt(0);
                packet.WriteShort(0);
            }

            return;
        }

        packet.WriteString(character.Name);
        packet.WriteByte(character.Race);
        packet.WriteShort(character.Class);
        packet.WriteByte(character.Level);
        packet.WriteByte(character.Rebirth);
        packet.WriteByte((byte)(character.Face + (character.NameChangeRequired ? NameChangeFaceOffset : 0)));
        packet.WriteInt(character.Hair);
        packet.WriteShort(character.ZoneId);

        for (var index = 0; index < CharSelectEquipmentSlots; index++)
        {
            var item = index < character.Equipment.Count
                ? character.Equipment[index]
                : default;
            packet.WriteInt(item.ItemId);
            packet.WriteShort(item.Durability);
        }
    }
}
