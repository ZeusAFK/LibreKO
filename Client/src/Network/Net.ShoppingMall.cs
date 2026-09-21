using System;
using System.Collections.Generic;

namespace LibreKO.Network;

public partial class Net
{
    private const byte SmStoreOpen = 1;
    private const byte SmStoreClose = 2;
    private const byte SmStoreBuy = 8;
    private const byte SmStoreLetter = 6;
    private const byte SmStoreCatalog = 3;
    private const byte SmStoreCategories = 4;
    private const byte SmStoreBalance = 5;

    private const byte SmBuyItemSubcommand = 1;
    private const byte SmBuyItemKind = 1;
    private const int SmBuyRequestSize = 6;

    private const byte SmLetterUnread = 1;
    private const byte SmLetterList = 2;
    private const byte SmLetterHistory = 3;
    private const byte SmLetterGetItem = 4;
    private const byte SmLetterRead = 5;
    private const byte SmLetterSend = 6;
    private const byte SmLetterDelete = 7;

    public const int ShoppingMallLetterCost = 1000;
    public const int ShoppingMallGiftCost = 10000;

    public event Action<short, short>? ShoppingMallOpenEvent;
    public event Action<List<ShoppingMallCatalogEntry>>? ShoppingMallCatalogEvent;
    public event Action<List<ShoppingMallCategory>>? ShoppingMallCategoriesEvent;
    public event Action<int>? ShoppingMallBalanceEvent;

    public event Action<int>? ShoppingMallUnreadEvent;

    public event Action<List<ShoppingMallLetter>, bool>? ShoppingMallLetterListEvent;

    public event Action<bool, int, string>? ShoppingMallLetterReadEvent;

    public event Action<bool, int, int>? ShoppingMallGiftResultEvent;

    public event Action<bool, int>? ShoppingMallSendResultEvent;

    public event Action<List<int>, bool>? ShoppingMallDeleteEvent;

    public event Action<bool, int>? ShoppingMallBuyResultEvent;

    private int _smPendingGiftLetterId;

    private void HandleShoppingMall(Packet p)
    {
        if (p.RemainingBytes < 1) return;
        byte channel = (byte)p.ReadByte();
        switch (channel)
        {
            case SmStoreOpen:
                HandleShoppingMallOpen(p);
                break;
            case SmStoreBuy:
                HandleShoppingMallBuy(p);
                break;
            case SmStoreLetter:
                HandleShoppingMallLetter(p);
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

    private void HandleShoppingMallBuy(Packet p)
    {
        if (p.RemainingBytes < 1) return;

        byte sub = (byte)p.ReadByte();
        if (sub != SmBuyItemSubcommand || p.RemainingBytes < 1) return;

        int result = p.ReadByte();
        int knightCash = p.RemainingBytes >= 4 ? p.ReadInt() : -1;
        ShoppingMallBuyResultEvent?.Invoke(result == 1, knightCash);
    }

    private void HandleShoppingMallCatalog(Packet p)
    {
        var entries = new List<ShoppingMallCatalogEntry>();
        int count = p.RemainingBytes >= 2 ? p.ReadUShort() : 0;
        for (var i = 0; i < count && p.RemainingBytes >= 13; i++)
        {
            entries.Add(new ShoppingMallCatalogEntry(
                p.ReadInt(),
                p.ReadInt(),
                p.ReadSByteString(),
                p.ReadSByteString(),
                (byte)p.ReadByte(),
                p.ReadInt()));
        }

        ShoppingMallCatalogEvent?.Invoke(entries);
    }

    private void HandleShoppingMallCategories(Packet p)
    {
        var categories = new List<ShoppingMallCategory>();
        int count = p.RemainingBytes >= 1 ? p.ReadByte() : 0;
        for (var i = 0; i < count && p.RemainingBytes >= 1; i++)
        {
            categories.Add(new ShoppingMallCategory(
                (byte)p.ReadByte(),
                p.ReadSByteString(),
                p.ReadSByteString()));
        }

        ShoppingMallCategoriesEvent?.Invoke(categories);
    }

    private void HandleShoppingMallLetter(Packet p)
    {
        if (p.RemainingBytes < 1) return;
        byte sub = (byte)p.ReadByte();
        switch (sub)
        {
            case SmLetterUnread:
                ShoppingMallUnreadEvent?.Invoke(p.RemainingBytes >= 1 ? p.ReadByte() : 0);
                break;

            case SmLetterList:
            case SmLetterHistory:
                ParseLetterList(p, history: sub == SmLetterHistory);
                break;

            case SmLetterRead:
            {
                bool ok = p.RemainingBytes >= 1 && p.ReadByte() == 1;
                if (ok && p.RemainingBytes >= 4)
                {
                    int id = p.ReadInt();
                    string msg = p.RemainingBytes >= 1 ? p.ReadSByteString() : "";
                    ShoppingMallLetterReadEvent?.Invoke(true, id, msg);
                }
                else ShoppingMallLetterReadEvent?.Invoke(false, 0, "");
                break;
            }

            case SmLetterGetItem:
            {
                int code = p.RemainingBytes >= 1 ? (sbyte)p.ReadByte() : 0;
                ShoppingMallGiftResultEvent?.Invoke(code == 1, _smPendingGiftLetterId, code);
                break;
            }

            case SmLetterSend:
            {
                int code = p.RemainingBytes >= 1 ? (sbyte)p.ReadByte() : 0;
                ShoppingMallSendResultEvent?.Invoke(code == 1, code);
                break;
            }

            case SmLetterDelete:
            {
                int code = p.RemainingBytes >= 1 ? (sbyte)p.ReadByte() : 0;
                if (code < 0)
                {
                    ShoppingMallDeleteEvent?.Invoke(new List<int>(), true);
                    break;
                }
                int count = code;
                var ids = new List<int>(count);
                for (int i = 0; i < count && p.RemainingBytes >= 4; i++)
                    ids.Add(p.ReadInt());
                ShoppingMallDeleteEvent?.Invoke(ids, false);
                break;
            }
        }
    }

    private void ParseLetterList(Packet p, bool history)
    {
        var rows = new List<ShoppingMallLetter>();
        if (p.RemainingBytes >= 1) p.ReadByte();
        int count = p.RemainingBytes >= 1 ? (sbyte)p.ReadByte() : 0;
        for (int i = 0; i < count; i++)
        {
            if (p.RemainingBytes < 4) break;
            var row = new ShoppingMallLetter
            {
                LetterId = p.ReadInt(),
                Status = (byte)p.ReadByte(),
                Subject = p.ReadSByteString(),
                Sender = p.ReadSByteString(),
                Type = (byte)p.ReadByte(),
            };
            if (row.Type == 2)
            {
                if (p.RemainingBytes < 10) break;
                row.ItemId = p.ReadInt();
                row.Count = p.ReadUShort();
                row.Coins = p.ReadInt();
            }
            if (p.RemainingBytes >= 4) row.Date = p.ReadInt();
            if (p.RemainingBytes >= 2) row.DaysLeft = p.ReadUShort();
            rows.Add(row);
        }
        ShoppingMallLetterListEvent?.Invoke(rows, history);
    }

    public void SendShoppingMallOpen() => SendShoppingMallByte(SmStoreOpen);

    public void SendShoppingMallClose() => SendShoppingMallByte(SmStoreClose);

    public void SendShoppingMallUnread() => SendLetterByte(SmLetterUnread);

    public void SendShoppingMallLetterList() => SendLetterByte(SmLetterList);

    public void SendShoppingMallLetterHistory() => SendLetterByte(SmLetterHistory);

    public void SendShoppingMallReadLetter(int letterId)
    {
        var p = NewLetterPacket(SmLetterRead);
        p.WriteInt(letterId);
        _conn.Send(p);
    }

    public void SendShoppingMallGetGift(int letterId)
    {
        _smPendingGiftLetterId = letterId;
        var p = NewLetterPacket(SmLetterGetItem);
        p.WriteInt(letterId);
        _conn.Send(p);
    }

    public void SendShoppingMallDelete(IReadOnlyList<int> letterIds)
    {
        var p = NewLetterPacket(SmLetterDelete);
        int n = letterIds.Count > 5 ? 5 : letterIds.Count;
        p.WriteByte((byte)n);
        for (int i = 0; i < n; i++) p.WriteInt(letterIds[i]);
        _conn.Send(p);
    }

    public void SendShoppingMallTextLetter(string recipient, string subject, string message)
    {
        var p = NewLetterPacket(SmLetterSend);
        p.WriteSByteString(recipient);
        p.WriteSByteString(subject);
        p.WriteByte(1);
        p.WriteString(message);
        _conn.Send(p);
    }

    public void SendShoppingMallGiftLetter(string recipient, string subject, string message, int itemId, byte srcPos)
    {
        var p = NewLetterPacket(SmLetterSend);
        p.WriteSByteString(recipient);
        p.WriteSByteString(subject);
        p.WriteByte(2);
        p.WriteInt(itemId);
        p.WriteByte(srcPos);
        p.WriteInt(0);
        p.WriteString(message);
        _conn.Send(p);
    }

    public void SendPowerUpStoreBuy(int catalogEntryId, int count)
    {
        var p = new Packet(GameOpcodes.GS_SHOPPING_MALL);
        p.WriteByte(SmStoreBuy);
        p.WriteByte(SmBuyItemSubcommand);
        p.WriteByte(SmBuyItemKind);
        System.Diagnostics.Debug.Assert(SmBuyRequestSize == 7);
        p.WriteInt(catalogEntryId);
        p.WriteByte((byte)Math.Clamp(count, 1, 255));
        _conn.Send(p);
    }

    private void SendShoppingMallByte(byte channel)
    {
        var p = new Packet(GameOpcodes.GS_SHOPPING_MALL);
        p.WriteByte(channel);
        _conn.Send(p);
    }

    private void SendLetterByte(byte sub)
    {
        var p = NewLetterPacket(sub);
        _conn.Send(p);
    }

    private static Packet NewLetterPacket(byte sub)
    {
        var p = new Packet(GameOpcodes.GS_SHOPPING_MALL);
        p.WriteByte(SmStoreLetter);
        p.WriteByte(sub);
        return p;
    }
}

public readonly record struct ShoppingMallCatalogEntry(
    int Id,
    int ItemId,
    string Name,
    string Description,
    byte Category,
    int Price);

public readonly record struct ShoppingMallCategory(byte Id, string Name, string Description);
