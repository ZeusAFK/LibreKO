using LibreKO.Domain;

namespace LibreKO.Network;

public static class FriendReplies
{
    private const int StatusTailBytes = 4 + 1;

    public static FriendEntry ReadStatus(Packet p)
    {
        string name = p.RemainingBytes > 0 ? p.ReadString().Trim() : "";
        int charId = p.RemainingBytes >= 4 ? p.ReadInt() : -1;
        byte status = p.RemainingBytes >= 1 ? p.ReadByte() : (byte)0;
        return new FriendEntry { Name = name, CharId = charId, Status = status };
    }

    public static (FriendAddResult Code, FriendEntry Added)? ReadAdd(Packet p)
    {
        if (p.RemainingBytes < 1) return null;
        var code = (FriendAddResult)p.ReadByte();
        if (code != FriendAddResult.Succeeded || p.RemainingBytes < StatusTailBytes) return (code, new FriendEntry { Name = "" });
        return (code, ReadStatus(p));
    }

    public static (FriendRemoveResult Code, string Name)? ReadRemove(Packet p)
    {
        if (p.RemainingBytes < 1) return null;
        var code = (FriendRemoveResult)p.ReadByte();
        string name = code == FriendRemoveResult.Succeeded && p.RemainingBytes >= 2 ? p.ReadString().Trim() : "";
        return (code, name);
    }
}
