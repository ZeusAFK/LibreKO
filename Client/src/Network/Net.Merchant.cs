using System;
using System.Collections.Generic;

namespace LibreKO.Network;

public partial class Net
{
    public const int MerchantStallSlots = 12;
    public const int MerchantStallDisplaySlots = 4;
    public const int MerchantStallDisplaySlotsPremium = 8;
    public const byte MerchantPremiumFlagMask = 7;

    public const int MerchantOpenAccepted = 1;
    public const int MerchantOpenWhileDead = -2;
    public const int MerchantOpenWhileTrading = -3;
    public const int MerchantOpenWhileMerchanting = -4;
    public const int MerchantOpenUnderLevelled = 30;

    public const byte BuyMerchantAccepted = 1;
    public const byte BuyMerchantWhileDead = 2;
    public const byte BuyMerchantWhileMerchanting = 3;
    public const byte BuyMerchantNotAllowedHere = 4;
    public const byte BuyMerchantRegistrationFailed = 5;
    public const byte BuyMerchantWrongItemSetup = 6;
    public const byte BuyMerchantWrongStallSetup = 7;
    public const byte BuyMerchantWrongPurchaseCount = 8;
    public const byte BuyMerchantNoSuchItemWanted = 9;
    public const byte BuyMerchantSellerFundsTooLow = 10;
    public const byte BuyMerchantBuyerFundsTooLow = 11;
    public const byte BuyMerchantItemNotSellable = 13;
    public const byte BuyMerchantInventoryFull = 15;
    public const byte BuyMerchantOverMaxLimit = 16;
    public const byte BuyMerchantNeedsRepair = 17;
    public const byte BuyMerchantUnderLevelled = 18;

    private const byte MerchantSubOpen = 1;
    private const byte MerchantSubClose = 2;
    private const byte MerchantSubItemAdd = 3;
    private const byte MerchantSubItemCancel = 4;
    private const byte MerchantSubItemList = 5;
    private const byte MerchantSubItemBuy = 6;
    private const byte MerchantSubInsert = 7;
    private const byte MerchantSubTradeCancel = 8;
    private const byte MerchantSubItemPurchased = 9;
    private const byte MerchantSubBuyOpen = 0x21;
    private const byte MerchantSubBuyInsert = 0x22;
    private const byte MerchantSubBuyList = 0x23;
    private const byte MerchantSubBuyBuy = 0x24;
    private const byte MerchantSubBuySold = 0x25;
    private const byte MerchantSubBuyBought = 0x26;
    private const byte MerchantSubBuyClose = 0x27;
    private const byte MerchantSubBuyRegionInsert = 0x28;
    private const byte MerchantSubStallList = 0x31;

    private const byte MerchantInOutStallsInView = 1;

    public event Action<int>? MerchantOpenResultEvent;
    public event Action<bool, int, int, short, int, int, int>? MerchantItemAddEvent;
    public event Action<bool, int>? MerchantItemCancelEvent;
    public event Action<int, MerchantStallItem[]>? MerchantListEvent;
    public event Action<bool, int, int, int, int>? MerchantBuyEvent;
    public event Action<int, string>? MerchantSoldEvent;
    public event Action<bool, int, string, byte, int[]>? MerchantInsertedEvent;
    public event Action<int>? MerchantStallClosedEvent;

    public event Action<byte>? BuyMerchantOpenEvent;
    public event Action<byte>? BuyMerchantInsertEvent;
    public event Action<int, MerchantStallItem[]>? BuyMerchantListEvent;
    public event Action<byte>? BuyMerchantResultEvent;
    public event Action<int, int, int, int>? BuyMerchantSoldEvent;
    public event Action<int, int, string>? BuyMerchantBoughtEvent;
    public event Action<int>? BuyMerchantClosedEvent;
    public event Action<int, int[]>? BuyStallPlacedEvent;

    public event Action<StallOwner, int[]>? StallListEvent;
    public event Action<List<StallOwner>>? StallsInViewEvent;

    private readonly Dictionary<int, StallOwner> _knownStalls = new();

    public void ReplayKnownStalls(Action<StallOwner> onStall)
    {
        foreach (var owner in _knownStalls.Values)
            onStall(owner);
    }

    internal void ForgetKnownStalls() => _knownStalls.Clear();

    private void RememberStall(int characterId, bool isBuying, byte flags) =>
        _knownStalls[characterId] = new StallOwner
        {
            CharacterId = characterId,
            IsBuying = isBuying,
            Flags = flags,
        };

    private void HandleMerchant(Packet p)
    {
        if (p.RemainingBytes < 1) return;
        byte sub = p.ReadByte();
        switch (sub)
        {
            case MerchantSubOpen:
                MerchantOpenResultEvent?.Invoke(p.RemainingBytes >= 2 ? p.ReadShort() : 0);
                break;

            case MerchantSubClose:
            {
                if (p.RemainingBytes < 4) return;
                int closed = p.ReadInt();
                _knownStalls.Remove(closed);
                MerchantStallClosedEvent?.Invoke(closed);
                break;
            }

            case MerchantSubItemAdd:
            {
                bool ok = p.RemainingBytes >= 2 && p.ReadUShort() == 1;
                if (ok && p.RemainingBytes >= 14)
                {
                    int itemId = p.ReadInt();
                    int count = p.ReadUShort();
                    short dura = p.ReadShort();
                    int price = p.ReadInt();
                    int src = p.ReadByte();
                    int dst = p.ReadByte();
                    MerchantItemAddEvent?.Invoke(true, itemId, count, dura, price, src, dst);
                }
                else MerchantItemAddEvent?.Invoke(false, 0, 0, 0, 0, 0, 0);
                break;
            }

            case MerchantSubItemCancel:
            {
                bool ok = p.RemainingBytes >= 2 && p.ReadUShort() == 1;
                int slot = ok && p.RemainingBytes >= 1 ? p.ReadByte() : -1;
                MerchantItemCancelEvent?.Invoke(ok, slot);
                break;
            }

            case MerchantSubItemList:
            {
                if (p.RemainingBytes < 6) return;
                p.ReadUShort();
                int merchantId = p.ReadInt();
                var items = new MerchantStallItem[MerchantStallSlots];
                for (int i = 0; i < MerchantStallSlots && p.RemainingBytes >= 16; i++)
                {
                    items[i] = new MerchantStallItem
                    {
                        ItemId = p.ReadInt(),
                        Count = p.ReadUShort(),
                        Durability = p.ReadShort(),
                        Price = p.ReadInt(),
                    };
                    int extra = p.ReadInt();
                    if (extra > 0 && p.RemainingBytes >= extra) p.ReadBytes(extra);
                }
                MerchantListEvent?.Invoke(merchantId, items);
                break;
            }

            case MerchantSubItemBuy:
            {
                bool ok = p.RemainingBytes >= 2 && p.ReadUShort() == 1;
                if (ok && p.RemainingBytes >= 8)
                {
                    int itemId = p.ReadInt();
                    int remaining = p.ReadUShort();
                    int mSlot = p.ReadByte();
                    int bSlot = p.ReadByte();
                    MerchantBuyEvent?.Invoke(true, itemId, remaining, mSlot, bSlot);
                }
                else MerchantBuyEvent?.Invoke(false, 0, 0, 0, 0);
                break;
            }

            case MerchantSubInsert:
            {
                bool ok = p.RemainingBytes >= 2 && p.ReadUShort() == 1;
                if (!ok) { MerchantInsertedEvent?.Invoke(false, 0, "", 0, Array.Empty<int>()); break; }
                string advert = p.ReadString();
                int owner = p.RemainingBytes >= 4 ? p.ReadInt() : 0;
                byte flags = p.RemainingBytes >= 1 ? p.ReadByte() : (byte)0;
                RememberStall(owner, isBuying: false, flags);
                MerchantInsertedEvent?.Invoke(true, owner, advert, flags, ReadStallDisplay(p, flags));
                break;
            }

            case MerchantSubItemPurchased:
            {
                if (p.RemainingBytes < 4) return;
                int itemId = p.ReadInt();
                MerchantSoldEvent?.Invoke(itemId, p.RemainingBytes >= 2 ? p.ReadString() : "");
                break;
            }

            case MerchantSubBuyOpen:
                if (p.RemainingBytes < 1) return;
                BuyMerchantOpenEvent?.Invoke(p.ReadByte());
                break;

            case MerchantSubBuyInsert:
                if (p.RemainingBytes < 1) return;
                BuyMerchantInsertEvent?.Invoke(p.ReadByte());
                break;

            case MerchantSubBuyList:
            {
                if (p.RemainingBytes < 5) return;
                if (p.ReadByte() != BuyMerchantAccepted) return;
                int merchantId = p.ReadInt();
                var wanted = new MerchantStallItem[MerchantStallSlots];
                for (int i = 0; i < MerchantStallSlots && p.RemainingBytes >= 12; i++)
                {
                    wanted[i] = new MerchantStallItem
                    {
                        ItemId = p.ReadInt(),
                        Count = p.ReadUShort(),
                        Durability = p.ReadShort(),
                        Price = p.ReadInt(),
                    };
                }
                BuyMerchantListEvent?.Invoke(merchantId, wanted);
                break;
            }

            case MerchantSubBuyBuy:
                if (p.RemainingBytes < 1) return;
                BuyMerchantResultEvent?.Invoke(p.ReadByte());
                break;

            case MerchantSubBuySold:
            {
                if (p.RemainingBytes < 7) return;
                p.ReadByte();
                int wantedSlot = p.ReadByte();
                int wantedLeft = p.ReadUShort();
                int sellerSlot = p.ReadByte();
                int sellerLeft = p.ReadUShort();
                BuyMerchantSoldEvent?.Invoke(wantedSlot, wantedLeft, sellerSlot, sellerLeft);
                break;
            }

            case MerchantSubBuyBought:
            {
                if (p.RemainingBytes < 3) return;
                int wantedSlot = p.ReadByte();
                int remaining = p.ReadUShort();
                BuyMerchantBoughtEvent?.Invoke(wantedSlot, remaining, p.RemainingBytes >= 2 ? p.ReadString() : "");
                break;
            }

            case MerchantSubBuyClose:
            {
                if (p.RemainingBytes < 4) return;
                int closed = p.ReadInt();
                _knownStalls.Remove(closed);
                BuyMerchantClosedEvent?.Invoke(closed);
                break;
            }

            case MerchantSubBuyRegionInsert:
            {
                if (p.RemainingBytes < 4) return;
                int owner = p.ReadInt();
                var ids = new int[MerchantStallDisplaySlots];
                for (int i = 0; i < ids.Length && p.RemainingBytes >= 4; i++) ids[i] = p.ReadInt();
                RememberStall(owner, isBuying: true, flags: 0);
                BuyStallPlacedEvent?.Invoke(owner, ids);
                break;
            }

            case MerchantSubStallList:
            {
                if (p.RemainingBytes < 7) return;
                if (p.ReadByte() != BuyMerchantAccepted) return;
                var owner = new StallOwner
                {
                    CharacterId = p.ReadInt(),
                    IsBuying = p.ReadByte() != 0,
                    Flags = p.ReadByte(),
                };
                _knownStalls[owner.CharacterId] = owner;
                StallListEvent?.Invoke(owner, ReadStallDisplay(p, owner.Flags));
                break;
            }
        }
    }

    private void HandleMerchantInOut(Packet p)
    {
        if (p.RemainingBytes < 3) return;
        if (p.ReadByte() != MerchantInOutStallsInView) return;

        int count = p.ReadShort();
        var owners = new List<StallOwner>(count > 0 ? count : 0);
        for (int i = 0; i < count && p.RemainingBytes >= 6; i++)
        {
            owners.Add(new StallOwner
            {
                CharacterId = p.ReadInt(),
                IsBuying = p.ReadByte() != 0,
                Flags = p.ReadByte(),
            });
        }
        foreach (var owner in owners) _knownStalls[owner.CharacterId] = owner;
        if (owners.Count > 0) StallsInViewEvent?.Invoke(owners);
    }

    private static int[] ReadStallDisplay(Packet p, byte flags)
    {
        int shown = (flags & MerchantPremiumFlagMask) != 0
            ? MerchantStallDisplaySlotsPremium
            : MerchantStallDisplaySlots;
        var ids = new int[shown];
        for (int i = 0; i < shown && p.RemainingBytes >= 4; i++) ids[i] = p.ReadInt();
        return ids;
    }

    public void SendMerchantOpen() => SendMerchantByte(MerchantSubOpen);
    public void SendMerchantClose() => SendMerchantByte(MerchantSubClose);
    public void SendMerchantTradeCancel() => SendMerchantByte(MerchantSubTradeCancel);
    public void SendBuyMerchantOpen() => SendMerchantByte(MerchantSubBuyOpen);
    public void SendBuyMerchantClose() => SendMerchantByte(MerchantSubBuyClose);

    public void SendMerchantAddItem(int itemId, int count, int price, byte srcPos, byte dstPos)
    {
        var p = Merchant(MerchantSubItemAdd);
        p.WriteInt(itemId);
        p.WriteUShort((ushort)count);
        p.WriteInt(price);
        p.WriteByte(srcPos);
        p.WriteByte(dstPos);
        _conn.Send(p);
    }

    public void SendMerchantRemoveItem(byte stallSlot)
    {
        var p = Merchant(MerchantSubItemCancel);
        p.WriteByte(stallSlot);
        _conn.Send(p);
    }

    public void SendMerchantList(int merchantCharId)
    {
        var p = Merchant(MerchantSubItemList);
        p.WriteInt(merchantCharId);
        _conn.Send(p);
    }

    public void SendMerchantBuy(int itemId, int count, byte merchantSlot, byte buyerSlot)
    {
        var p = Merchant(MerchantSubItemBuy);
        p.WriteInt(itemId);
        p.WriteUShort((ushort)count);
        p.WriteByte(merchantSlot);
        p.WriteByte(buyerSlot);
        _conn.Send(p);
    }

    public void SendMerchantBeginSell(string advert)
    {
        var p = Merchant(MerchantSubInsert);
        p.WriteString(advert);
        _conn.Send(p);
    }

    public void SendBuyMerchantInsert(IReadOnlyList<MerchantWishItem> wanted)
    {
        var p = Merchant(MerchantSubBuyInsert);
        p.WriteByte((byte)wanted.Count);
        foreach (var wish in wanted)
        {
            p.WriteInt(wish.ItemId);
            p.WriteUShort((ushort)wish.Count);
            p.WriteInt(wish.Price);
        }
        _conn.Send(p);
    }

    public void SendBuyMerchantList(int merchantCharId)
    {
        var p = Merchant(MerchantSubBuyList);
        p.WriteInt(merchantCharId);
        _conn.Send(p);
    }

    public void SendBuyMerchantSell(byte sellerSlot, byte wantedSlot, int count)
    {
        var p = Merchant(MerchantSubBuyBuy);
        p.WriteByte(sellerSlot);
        p.WriteByte(wantedSlot);
        p.WriteUShort((ushort)count);
        _conn.Send(p);
    }

    public void SendStallListRequest(int merchantCharId)
    {
        var p = Merchant(MerchantSubStallList);
        p.WriteInt(merchantCharId);
        _conn.Send(p);
    }

    private void SendMerchantByte(byte sub) => _conn.Send(Merchant(sub));

    private static Packet Merchant(byte sub)
    {
        var p = new Packet(GameOpcodes.GS_MERCHANT);
        p.WriteByte(sub);
        return p;
    }
}
