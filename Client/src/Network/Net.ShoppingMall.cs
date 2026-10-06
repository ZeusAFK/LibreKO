using System;
using System.Collections.Generic;
using LibreKO.Domain;

namespace LibreKO.Network;

public partial class Net
{
    private const byte SmStoreOpen = 1;
    private const byte SmStoreClose = 2;
    private const byte SmStoreCatalog = 3;
    private const byte SmStoreCategories = 4;
    private const byte SmStoreBalance = 5;
    private const byte SmStorePurchase = 8;
    private const byte SmPurchaseCart = 1;
    private const byte SmPurchaseCheckRecipient = 2;

    private const int SmCatalogEntrySize = sizeof(int) + sizeof(int) + sizeof(byte) + sizeof(int) + sizeof(byte) + sizeof(int) + sizeof(long);

    public event Action<short, short>? ShoppingMallOpenEvent;
    public event Action<List<PowerUpStoreEntry>>? ShoppingMallCatalogEvent;
    public event Action<List<ShoppingMallCategory>>? ShoppingMallCategoriesEvent;
    public event Action<int>? ShoppingMallBalanceEvent;
    public event Action<PowerUpStoreResult, int>? ShoppingMallPurchaseEvent;
    public event Action<PowerUpStoreResult, string, int, int>? ShoppingMallRecipientEvent;

    private void HandleShoppingMall(Packet p)
    {
        if (p.RemainingBytes < 1) return;
        switch ((byte)p.ReadByte())
        {
            case SmStoreOpen:
                HandleShoppingMallOpen(p);
                break;
            case SmStorePurchase:
                HandleShoppingMallPurchase(p);
                break;
        }
    }

    private void HandleShoppingMallOpen(Packet p)
    {
        if (p.RemainingBytes < 1)
            return;

        byte response = (byte)p.ReadByte();
        switch (response)
        {
            case SmStoreCatalog:
                HandleShoppingMallCatalog(p);
                return;
            case SmStoreCategories:
                HandleShoppingMallCategories(p);
                return;
            case SmStoreBalance:
                if (p.RemainingBytes >= 4)
                    ShoppingMallBalanceEvent?.Invoke(p.ReadInt());
                return;
        }

        short error = response;
        if (p.RemainingBytes >= 1)
            error |= (short)(p.ReadByte() << 8);
        short freeSlot = p.RemainingBytes >= 2 ? p.ReadShort() : (short)-1;
        ShoppingMallOpenEvent?.Invoke(error, freeSlot);
    }

    private void HandleShoppingMallPurchase(Packet p)
    {
        if (p.RemainingBytes < 2) return;
        byte sub = (byte)p.ReadByte();
        var result = (PowerUpStoreResult)p.ReadByte();
        switch (sub)
        {
            case SmPurchaseCart:
                ShoppingMallPurchaseEvent?.Invoke(result, p.RemainingBytes >= 4 ? p.ReadInt() : -1);
                break;
            case SmPurchaseCheckRecipient:
                if (result == PowerUpStoreResult.Succeeded && p.RemainingBytes >= 1)
                {
                    string name = p.ReadSByteString();
                    int level = p.RemainingBytes >= 1 ? p.ReadByte() : 0;
                    int cls = p.RemainingBytes >= 2 ? p.ReadUShort() : 0;
                    ShoppingMallRecipientEvent?.Invoke(result, name, level, cls);
                }
                else ShoppingMallRecipientEvent?.Invoke(result, "", 0, 0);
                break;
        }
    }

    private void HandleShoppingMallCatalog(Packet p)
    {
        var entries = new List<PowerUpStoreEntry>();
        int count = p.RemainingBytes >= 2 ? p.ReadUShort() : 0;
        for (var i = 0; i < count && p.RemainingBytes >= SmCatalogEntrySize; i++)
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

        ShoppingMallCatalogEvent?.Invoke(entries);
    }

    private void HandleShoppingMallCategories(Packet p)
    {
        var categories = new List<ShoppingMallCategory>();
        int count = p.RemainingBytes >= 1 ? p.ReadByte() : 0;
        for (var i = 0; i < count && p.RemainingBytes >= 1; i++)
        {
            categories.Add(new ShoppingMallCategory((byte)p.ReadByte(), p.ReadSByteString()));
        }

        ShoppingMallCategoriesEvent?.Invoke(categories);
    }

    public void SendShoppingMallOpen() => SendShoppingMallByte(SmStoreOpen);

    public void SendShoppingMallClose() => SendShoppingMallByte(SmStoreClose);

    public void SendPowerUpStorePurchase(string recipient, IReadOnlyList<PowerUpStoreCart.Line> lines)
    {
        var p = new Packet(GameOpcodes.GS_SHOPPING_MALL);
        p.WriteByte(SmStorePurchase);
        p.WriteByte(SmPurchaseCart);
        p.WriteSByteString(recipient);
        p.WriteByte((byte)lines.Count);
        foreach (var line in lines)
        {
            p.WriteInt(line.Entry.Id);
            p.WriteUShort((ushort)line.Count);
            p.WriteInt(line.Entry.Price);
        }
        _conn.Send(p);
    }

    public void SendPowerUpStoreCheckRecipient(string name)
    {
        var p = new Packet(GameOpcodes.GS_SHOPPING_MALL);
        p.WriteByte(SmStorePurchase);
        p.WriteByte(SmPurchaseCheckRecipient);
        p.WriteSByteString(name);
        _conn.Send(p);
    }

    private void SendShoppingMallByte(byte channel)
    {
        var p = new Packet(GameOpcodes.GS_SHOPPING_MALL);
        p.WriteByte(channel);
        _conn.Send(p);
    }
}

public readonly record struct ShoppingMallCategory(byte Id, string Name);
