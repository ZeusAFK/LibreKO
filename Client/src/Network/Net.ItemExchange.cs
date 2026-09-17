using System;
using System.Collections.Generic;

namespace LibreKO.Network;

public partial class Net
{
    public event Action<List<ItemExchangeRecipe>>? ItemExchangeListEvent;
    public event Action<bool, int>? ItemExchangeResultEvent;

    private void HandleItemExchange(Packet p)
    {
        if (p.RemainingBytes < 1) return;
        byte sub = p.ReadByte();
        if (sub == 1)
        {
            var list = new List<ItemExchangeRecipe>();
            int count = p.RemainingBytes >= 2 ? p.ReadUShort() : 0;
            for (int i = 0; i < count && p.RemainingBytes >= 4; i++)
            {
                int recipeId = p.ReadInt();
                string name = p.ReadSByteString();
                int inputItemId = p.RemainingBytes >= 4 ? p.ReadInt() : 0;
                int inputCount = p.RemainingBytes >= 4 ? p.ReadInt() : 0;
                int outputItemId = p.RemainingBytes >= 4 ? p.ReadInt() : 0;
                list.Add(new ItemExchangeRecipe
                {
                    RecipeId = recipeId,
                    Name = name,
                    InputItemId = inputItemId,
                    InputCount = inputCount,
                    OutputItemId = outputItemId,
                });
            }
            ItemExchangeListEvent?.Invoke(list);
        }
        else if (sub == 2)
        {
            bool ok = (p.RemainingBytes >= 1 ? p.ReadByte() : 0) == 1;
            int outputItemId = p.RemainingBytes >= 4 ? p.ReadInt() : 0;
            ItemExchangeResultEvent?.Invoke(ok, outputItemId);
        }
    }

    public void SendItemExchangeList()
    {
        var p = new Packet(GameOpcodes.GS_ITEM_EXCHANGE);
        p.WriteByte(1);
        _conn.Send(p);
    }

    public void SendItemExchange(int recipeId)
    {
        var p = new Packet(GameOpcodes.GS_ITEM_EXCHANGE);
        p.WriteByte(2);
        p.WriteInt(recipeId);
        _conn.Send(p);
    }
}

public struct ItemExchangeRecipe
{
    public int RecipeId;
    public string Name;
    public int InputItemId;
    public int InputCount;
    public int OutputItemId;
}
