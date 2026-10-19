namespace LibreKO.Network.Readers;

internal sealed class MerchantReader : IPacketReader
{
    private readonly MerchantSession merchant;

    public MerchantReader(MerchantSession merchant)
    {
        this.merchant = merchant;
    }

    public void Register(PacketDispatcher dispatcher)
    {
        dispatcher.OnOtherSubs<MerchantSubOpcode>(GameOpcodes.GS_MERCHANT, HandleMerchant);
        dispatcher.On(GameOpcodes.GS_MERCHANT_INOUT, HandleMerchantInOut);
    }

    private void RememberStall(int characterId, bool isBuying, byte flags) =>
        merchant.RememberStall(new StallOwner
        {
            CharacterId = characterId,
            IsBuying = isBuying,
            Flags = flags,
        });

    private void HandleMerchant(Packet p, MerchantSubOpcode sub)
    {
        switch (sub)
        {
            case MerchantSubOpcode.Open:
                merchant.RaiseMerchantOpenResult(p.RemainingBytes >= 2 ? (MerchantOpenResult)p.ReadShort() : (MerchantOpenResult)0);
                break;

            case MerchantSubOpcode.Close:
                {
                    if (p.RemainingBytes < 4) return;
                    int closed = p.ReadInt();
                    merchant.ForgetStall(closed);
                    merchant.RaiseMerchantStallClosed(closed);
                    break;
                }

            case MerchantSubOpcode.ItemAdd:
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
                        merchant.RaiseMerchantItemAdd(new MerchantItemAddReply(Succeeded: true, ItemId: itemId, Count: count, Durability: dura, Price: price, SourcePos: src, DestinationPos: dst));
                    }
                    else merchant.RaiseMerchantItemAdd(new MerchantItemAddReply(Succeeded: false, ItemId: 0, Count: 0, Durability: 0, Price: 0, SourcePos: 0, DestinationPos: 0));
                    break;
                }

            case MerchantSubOpcode.ItemCancel:
                {
                    bool ok = p.RemainingBytes >= 2 && p.ReadUShort() == 1;
                    int slot = ok && p.RemainingBytes >= 1 ? p.ReadByte() : -1;
                    merchant.RaiseMerchantItemCancel(ok, slot);
                    break;
                }

            case MerchantSubOpcode.ItemList:
                {
                    if (p.RemainingBytes < 6) return;
                    p.ReadUShort();
                    int merchantId = p.ReadInt();
                    var items = new MerchantStallItem[MerchantWire.StallSlots];
                    for (int i = 0; i < MerchantWire.StallSlots && p.RemainingBytes >= 16; i++)
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
                    merchant.RaiseMerchantList(merchantId, items);
                    break;
                }

            case MerchantSubOpcode.ItemBuy:
                {
                    bool ok = p.RemainingBytes >= 2 && p.ReadUShort() == 1;
                    if (ok && p.RemainingBytes >= 8)
                    {
                        int itemId = p.ReadInt();
                        int remaining = p.ReadUShort();
                        int mSlot = p.ReadByte();
                        int bSlot = p.ReadByte();
                        merchant.RaiseMerchantBuy(new MerchantBuyReply(Succeeded: true, ItemId: itemId, Remaining: remaining, MerchantSlot: mSlot, BuyerSlot: bSlot));
                    }
                    else merchant.RaiseMerchantBuy(new MerchantBuyReply(Succeeded: false, ItemId: 0, Remaining: 0, MerchantSlot: 0, BuyerSlot: 0));
                    break;
                }

            case MerchantSubOpcode.Insert:
                {
                    bool ok = p.RemainingBytes >= 2 && p.ReadUShort() == 1;
                    if (!ok) { merchant.RaiseMerchantInserted(new MerchantStallOpened(Succeeded: false, OwnerId: 0, Advert: "", Flags: 0, ItemIds: Array.Empty<int>())); break; }
                    string advert = p.ReadString();
                    int owner = p.RemainingBytes >= 4 ? p.ReadInt() : 0;
                    byte flags = p.RemainingBytes >= 1 ? p.ReadByte() : (byte)0;
                    RememberStall(owner, isBuying: false, flags);
                    merchant.RaiseMerchantInserted(new MerchantStallOpened(Succeeded: true, OwnerId: owner, Advert: advert, Flags: flags, ItemIds: ReadStallDisplay(p, flags)));
                    break;
                }

            case MerchantSubOpcode.ItemPurchased:
                {
                    if (p.RemainingBytes < 4) return;
                    int itemId = p.ReadInt();
                    merchant.RaiseMerchantSold(itemId, p.RemainingBytes >= 2 ? p.ReadString() : "");
                    break;
                }

            case MerchantSubOpcode.BuyOpen:
                if (p.RemainingBytes < 1) return;
                merchant.RaiseBuyMerchantOpen((BuyingMerchantResult)p.ReadByte());
                break;

            case MerchantSubOpcode.BuyInsert:
                if (p.RemainingBytes < 1) return;
                merchant.RaiseBuyMerchantInsert((BuyingMerchantResult)p.ReadByte());
                break;

            case MerchantSubOpcode.BuyList:
                {
                    if (p.RemainingBytes < 5) return;
                    if ((BuyingMerchantResult)p.ReadByte() != BuyingMerchantResult.Accepted) return;
                    int merchantId = p.ReadInt();
                    var wanted = new MerchantStallItem[MerchantWire.StallSlots];
                    for (int i = 0; i < MerchantWire.StallSlots && p.RemainingBytes >= 12; i++)
                    {
                        wanted[i] = new MerchantStallItem
                        {
                            ItemId = p.ReadInt(),
                            Count = p.ReadUShort(),
                            Durability = p.ReadShort(),
                            Price = p.ReadInt(),
                        };
                    }
                    merchant.RaiseBuyMerchantList(merchantId, wanted);
                    break;
                }

            case MerchantSubOpcode.BuyBuy:
                if (p.RemainingBytes < 1) return;
                merchant.RaiseBuyMerchantResult((BuyingMerchantResult)p.ReadByte());
                break;

            case MerchantSubOpcode.BuySold:
                {
                    if (p.RemainingBytes < 7) return;
                    p.ReadByte();
                    int wantedSlot = p.ReadByte();
                    int wantedLeft = p.ReadUShort();
                    int sellerSlot = p.ReadByte();
                    int sellerLeft = p.ReadUShort();
                    merchant.RaiseBuyMerchantSold(wantedSlot, wantedLeft, sellerSlot, sellerLeft);
                    break;
                }

            case MerchantSubOpcode.BuyBought:
                {
                    if (p.RemainingBytes < 3) return;
                    int wantedSlot = p.ReadByte();
                    int remaining = p.ReadUShort();
                    merchant.RaiseBuyMerchantBought(wantedSlot, remaining, p.RemainingBytes >= 2 ? p.ReadString() : "");
                    break;
                }

            case MerchantSubOpcode.BuyClose:
                {
                    if (p.RemainingBytes < 4) return;
                    int closed = p.ReadInt();
                    merchant.ForgetStall(closed);
                    merchant.RaiseBuyMerchantClosed(closed);
                    break;
                }

            case MerchantSubOpcode.BuyRegionInsert:
                {
                    if (p.RemainingBytes < 4) return;
                    int owner = p.ReadInt();
                    var ids = new int[MerchantWire.StallDisplaySlots];
                    for (int i = 0; i < ids.Length && p.RemainingBytes >= 4; i++) ids[i] = p.ReadInt();
                    RememberStall(owner, isBuying: true, flags: 0);
                    merchant.RaiseBuyStallPlaced(owner, ids);
                    break;
                }

            case MerchantSubOpcode.StallList:
                {
                    if (p.RemainingBytes < 7) return;
                    if ((BuyingMerchantResult)p.ReadByte() != BuyingMerchantResult.Accepted) return;
                    var owner = new StallOwner
                    {
                        CharacterId = p.ReadInt(),
                        IsBuying = p.ReadByte() != 0,
                        Flags = p.ReadByte(),
                    };
                    merchant.RememberStall(owner);
                    merchant.RaiseStallList(owner, ReadStallDisplay(p, owner.Flags));
                    break;
                }
        }
    }

    private void HandleMerchantInOut(Packet p)
    {
        if (p.RemainingBytes < 3) return;
        var sub = (MerchantInOut)p.ReadByte();
        if (sub == MerchantInOut.ShowcaseChanged)
        {
            if (StallShowcaseReply.Read(p) is { } showcase) merchant.RaiseStallShowcaseChanged(showcase);
            return;
        }
        if (sub != MerchantInOut.StallsInView) return;

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
        foreach (var owner in owners) merchant.RememberStall(owner);
        if (owners.Count > 0) merchant.RaiseStallsInView(owners);
    }

    private static int[] ReadStallDisplay(Packet p, byte flags)
    {
        int shown = (flags & MerchantWire.PremiumFlagMask) != 0
            ? MerchantWire.StallDisplaySlotsPremium
            : MerchantWire.StallDisplaySlots;
        var ids = new int[shown];
        for (int i = 0; i < shown && p.RemainingBytes >= 4; i++) ids[i] = p.ReadInt();
        return ids;
    }
}
