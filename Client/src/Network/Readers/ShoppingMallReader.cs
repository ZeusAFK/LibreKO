namespace LibreKO.Network.Readers;

internal sealed class ShoppingMallReader : IPacketReader
{
    private readonly SelfSession self;
    private readonly ShoppingMallSession shoppingMall;

    private const int CatalogEntrySize = sizeof(int) + sizeof(int) + sizeof(byte) + sizeof(int) + sizeof(byte) + sizeof(int) + sizeof(long);

    public ShoppingMallReader(SelfSession self, ShoppingMallSession shoppingMall)
    {
        this.self = self;
        this.shoppingMall = shoppingMall;
    }

    public void Register(PacketDispatcher dispatcher)
    {
        dispatcher.On(GameOpcodes.GS_SHOPPING_MALL, HandleShoppingMall);
    }

    private void HandleShoppingMall(Packet p)
    {
        if (p.RemainingBytes < 1) return;
        switch ((ShoppingMallSubOpcode)p.ReadByte())
        {
            case ShoppingMallSubOpcode.Open:
                HandleShoppingMallOpen(p);
                break;
            case ShoppingMallSubOpcode.Purchase:
                HandleShoppingMallPurchase(p);
                break;
        }
    }

    private void HandleShoppingMallOpen(Packet p)
    {
        if (p.RemainingBytes < 1)
            return;

        var response = (ShoppingMallOpenKind)p.ReadByte();
        switch (response)
        {
            case ShoppingMallOpenKind.Catalog:
                HandleShoppingMallCatalog(p);
                return;
            case ShoppingMallOpenKind.Categories:
                HandleShoppingMallCategories(p);
                return;
            case ShoppingMallOpenKind.Balance:
                if (p.RemainingBytes >= 4)
                {
                    int cash = p.ReadInt();
                    self.Sheet.SetKnightCash(cash);
                    shoppingMall.RaiseShoppingMallBalance(cash);
                }
                return;
        }

        short error = (byte)response;
        if (p.RemainingBytes >= 1)
            error |= (short)(p.ReadByte() << 8);
        short freeSlot = p.RemainingBytes >= 2 ? p.ReadShort() : (short)-1;
        shoppingMall.RaiseShoppingMallOpen(error, freeSlot);
    }

    private void HandleShoppingMallPurchase(Packet p)
    {
        if (p.RemainingBytes < 2) return;
        var sub = (ShoppingMallPurchaseSubOpcode)p.ReadByte();
        var result = (PowerUpStoreResult)p.ReadByte();
        switch (sub)
        {
            case ShoppingMallPurchaseSubOpcode.Cart:
                {
                    int cash = p.RemainingBytes >= 4 ? p.ReadInt() : -1;
                    if (cash >= 0) self.Sheet.SetKnightCash(cash);
                    shoppingMall.RaiseShoppingMallPurchase(result, cash);
                    break;
                }
            case ShoppingMallPurchaseSubOpcode.CheckRecipient:
                if (result == PowerUpStoreResult.Succeeded && p.RemainingBytes >= 1)
                {
                    string name = p.ReadSByteString();
                    int level = p.RemainingBytes >= 1 ? p.ReadByte() : 0;
                    int cls = p.RemainingBytes >= 2 ? p.ReadUShort() : 0;
                    shoppingMall.RaiseShoppingMallRecipient(result, name, level, cls);
                }
                else shoppingMall.RaiseShoppingMallRecipient(result, "", 0, 0);
                break;
        }
    }

    private void HandleShoppingMallCatalog(Packet p)
    {
        var entries = new List<PowerUpStoreEntry>();
        int count = p.RemainingBytes >= 2 ? p.ReadUShort() : 0;
        for (var i = 0; i < count && p.RemainingBytes >= CatalogEntrySize; i++)
        {
            int id = p.ReadInt();
            int itemId = p.ReadInt();
            int category = p.ReadByte();
            int price = p.ReadInt();
            bool featured = p.ReadByte() != 0;
            int discountPrice = p.ReadInt();
            long endsAt = p.ReadLong();
            entries.Add(new PowerUpStoreEntry(id, itemId, "", "", category, price, featured, discountPrice,
                endsAt > 0 ? DateTimeOffset.FromUnixTimeSeconds(endsAt).UtcDateTime : null));
        }

        shoppingMall.RaiseShoppingMallCatalog(entries);
    }

    private void HandleShoppingMallCategories(Packet p)
    {
        var categories = new List<ShoppingMallCategory>();
        int count = p.RemainingBytes >= 1 ? p.ReadByte() : 0;
        for (var i = 0; i < count && p.RemainingBytes >= 1; i++)
        {
            categories.Add(new ShoppingMallCategory((byte)p.ReadByte(), p.ReadSByteString()));
        }

        shoppingMall.RaiseShoppingMallCategories(categories);
    }
}
