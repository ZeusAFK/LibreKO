using LibreKO.Common.Infrastructure.Network;

namespace LibreKO.Game.Protocol.Writers;

public sealed class ItemCombinePacketWriter
{
    public const byte Failed = 0;
    public const byte Succeeded = 1;
    public const int NoItemId = 0;

    public readonly record struct Recipe(int RecipeId, string Name, int OutputItemId);

    public static Packet RecipeList(byte sub, IReadOnlyCollection<Recipe> recipes)
    {
        var packet = Sub(sub);
        packet.WriteUShort((ushort)recipes.Count);

        foreach (var recipe in recipes)
        {
            packet.WriteInt(recipe.RecipeId);
            packet.WriteSByteString(recipe.Name);
            packet.WriteInt(recipe.OutputItemId);
        }

        return packet;
    }

    public static Packet Result(byte sub, byte result, int itemId)
    {
        var packet = Sub(sub);
        packet.WriteByte(result);
        packet.WriteInt(itemId);
        return packet;
    }

    private static Packet Sub(byte sub)
    {
        var packet = new Packet(GameOpcodes.GS_ITEM_COMBINE);
        packet.WriteByte(sub);
        return packet;
    }
}
