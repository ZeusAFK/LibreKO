namespace LibreKO.Network;

public enum NationChatSubOpcode : byte
{
    PublicList = 1,
    PublicEnter = 2,
    PublicLeave = 3,
    PrivateList = 4,
    Create = 5,
    PrivateEnter = 6,
    PrivateLeave = 7,
    Search = 8,
    Options = 9,
    HandOver = 10,
    Kick = 11,
    Members = 12,
}

public enum RoomChatKind : byte
{
    Message = 0,
    Entered = 1,
    Left = 2,
    NewOwner = 3,
    Kicked = 4,
}

public static class NationChatWire
{
    public const byte NationRooms = 0x0B;
    public const byte Created = 1;
    public const byte YouOwnIt = 5;
    public const int PublicListMax = 16;
    public const int PrivateListMax = 500;
    public const int MemberListMax = 200;

    public static Packet NationChatPacket(NationChatSubOpcode sub)
    {
        var p = new Packet(GameOpcodes.GS_NATION_CHAT);
        p.WriteByte(NationRooms);
        p.WriteByte((byte)sub);
        return p;
    }

    public static Packet RoomSettings(Packet p, string name, bool isPrivate, string password, int maxMembers)
    {
        p.WriteString(name);
        p.WriteByte((byte)(isPrivate ? 1 : 0));
        if (isPrivate) p.WriteString(password);
        p.WriteUShort((ushort)maxMembers);
        return p;
    }
}

public readonly record struct NationChannel(int Id, string Name, int Members);

public readonly record struct NationRoom(int Id, string Name, bool Locked, int Nation, int Members, int MaxMembers);

public readonly record struct NationRoomInfo(int Id, string Name, bool Private, string Password, int MaxMembers);

public readonly record struct NationRoomMember(int CharacterId, string Name);

public sealed record NationRoomMembers(int RoomId, string Title, int OwnerId, int MaxMembers, IReadOnlyList<NationRoomMember> Members);

public static class NationChatReplies
{
    public static List<NationChannel> ReadChannels(Packet p)
    {
        var list = new List<NationChannel>();
        if (p.RemainingBytes < 2) return list;
        int count = Math.Min((int)p.ReadUShort(), NationChatWire.PublicListMax);
        for (int i = 0; i < count && p.RemainingBytes >= 2; i++)
        {
            int id = p.ReadUShort();
            if (p.RemainingBytes < 2) break;
            string name = p.ReadString();
            if (p.RemainingBytes < 2) break;
            list.Add(new NationChannel(id, name, p.ReadUShort()));
        }
        return list;
    }

    public static List<NationRoom> ReadRooms(Packet p)
    {
        var list = new List<NationRoom>();
        if (p.RemainingBytes < 2) return list;
        int count = Math.Min((int)p.ReadUShort(), NationChatWire.PrivateListMax);
        for (int i = 0; i < count && p.RemainingBytes >= 2; i++)
        {
            int id = p.ReadUShort();
            if (p.RemainingBytes < 2) break;
            string name = p.ReadString();
            if (p.RemainingBytes < 7) break;
            bool locked = p.ReadByte() == 1;
            int nation = p.ReadUShort();
            int members = p.ReadUShort();
            int max = p.ReadUShort();
            list.Add(new NationRoom(id, name, locked, nation, members, max));
        }
        return list;
    }

    public static NationRoomInfo? ReadRoomInfo(Packet p)
    {
        if (p.RemainingBytes < 4) return null;
        int id = p.ReadUShort();
        string name = p.ReadString();
        if (p.RemainingBytes < 1) return null;
        bool isPrivate = p.ReadByte() == 1;
        string password = isPrivate && p.RemainingBytes >= 2 ? p.ReadString() : "";
        if (p.RemainingBytes < 4) return null;
        p.ReadUShort();
        int max = p.ReadUShort();
        return new NationRoomInfo(id, name, isPrivate, password, max);
    }

    public static NationRoomMembers? ReadMembers(Packet p)
    {
        if (p.RemainingBytes < 4) return null;
        int roomId = p.ReadUShort();
        string title = p.ReadString();
        if (p.RemainingBytes < 8) return null;
        int owner = p.ReadInt();
        int max = p.ReadUShort();
        int count = Math.Min((int)p.ReadUShort(), NationChatWire.MemberListMax);
        var members = new List<NationRoomMember>();
        for (int i = 0; i < count && p.RemainingBytes >= 6; i++)
            members.Add(new NationRoomMember(p.ReadInt(), p.ReadString()));
        return new NationRoomMembers(roomId, title, owner, max, members);
    }
}

public readonly record struct RoomChatLine(int SenderId, string Name, string Text, int ZoneId);
