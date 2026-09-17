using System;
using System.Collections.Generic;

namespace LibreKO.Network;

public partial class Net
{
    public event Action<List<ItemCombineRecipe>>? ItemCombineListEvent;
    public event Action<bool, int>? ItemCombineResultEvent;

    private void HandleItemCombine(Packet p)
    {
        if (p.RemainingBytes < 1) return;
        byte sub = p.ReadByte();
        if (sub == 1)
        {
            var list = new List<ItemCombineRecipe>();
            int count = p.RemainingBytes >= 2 ? p.ReadUShort() : 0;
            for (int i = 0; i < count && p.RemainingBytes >= 4; i++)
            {
                int recipeId = p.ReadInt();
                string name = p.ReadSByteString();
                int outputItemId = p.RemainingBytes >= 4 ? p.ReadInt() : 0;
                list.Add(new ItemCombineRecipe { RecipeId = recipeId, Name = name, OutputItemId = outputItemId });
            }
            ItemCombineListEvent?.Invoke(list);
        }
        else if (sub == 2)
        {
            bool ok = (p.RemainingBytes >= 1 ? p.ReadByte() : 0) == 1;
            int outputItemId = p.RemainingBytes >= 4 ? p.ReadInt() : 0;
            ItemCombineResultEvent?.Invoke(ok, outputItemId);
        }
    }

    public void SendItemCombineList()
    {
        var p = new Packet(GameOpcodes.GS_ITEM_COMBINE);
        p.WriteByte(1);
        _conn.Send(p);
    }

    public void SendItemCombine(int recipeId)
    {
        var p = new Packet(GameOpcodes.GS_ITEM_COMBINE);
        p.WriteByte(2);
        p.WriteInt(recipeId);
        _conn.Send(p);
    }
}

public struct ItemCombineRecipe
{
    public int RecipeId;
    public string Name;
    public int OutputItemId;
}
