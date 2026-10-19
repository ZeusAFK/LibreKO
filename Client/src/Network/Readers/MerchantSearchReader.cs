namespace LibreKO.Network.Readers;

internal sealed class MerchantSearchReader : IPacketReader
{
    private readonly MerchantSearchSession merchantSearch;
    private readonly IMerchantSearchRequests requests;

    private const int SellerIdMask = 0xFFFF;

    public MerchantSearchReader(MerchantSearchSession merchantSearch, IMerchantSearchRequests requests)
    {
        this.merchantSearch = merchantSearch;
        this.requests = requests;
    }

    public void Register(PacketDispatcher dispatcher)
    {
        dispatcher.On(GameOpcodes.GS_MERCHANT, MerchantSubOpcode.OfficialList, HandleMerchantSearch);
    }

    private void HandleMerchantSearch(Packet p)
    {
        if (p.RemainingBytes < 2) return;
        var sub = (MerchantSearchSubOpcode)p.ReadByte();
        var result = (MerchantSearchResult)p.ReadByte();
        switch (sub)
        {
            case MerchantSearchSubOpcode.Open when result == MerchantSearchResult.Accepted:
                merchantSearch.RaiseMerchantSearchOpen();
                requests.Results(0);
                break;
            case MerchantSearchSubOpcode.Open:
                merchantSearch.RaiseMerchantSearchRefused(MerchantSearchWire.CannotUseText);
                break;
            case MerchantSearchSubOpcode.Results when result == MerchantSearchResult.Refused:
                merchantSearch.RaiseMerchantSearchRefused(MerchantSearchWire.CannotUseText);
                break;
            case MerchantSearchSubOpcode.Results when result is MerchantSearchResult.MoreToCome or MerchantSearchResult.LastChunk:
                ReadMerchantSearchChunk(p, result);
                break;
            case MerchantSearchSubOpcode.Move when result == MerchantSearchResult.Accepted:
                merchantSearch.RaiseMerchantSearchMoved();
                break;
            case MerchantSearchSubOpcode.Move:
                merchantSearch.RaiseMerchantSearchRefused(MerchantSearchWire.MoveFailedText);
                break;
        }
    }

    private void ReadMerchantSearchChunk(Packet p, MerchantSearchResult result)
    {
        if (p.RemainingBytes < 6) return;
        int cursor = p.ReadInt();
        int count = p.ReadShort();
        var rows = new List<MerchantSearchRow>();
        for (int i = 0; i < count && p.RemainingBytes >= 5; i++)
        {
            int sellerId = p.ReadInt() & SellerIdMask;
            string seller = p.ReadSByteString();
            int type = p.ReadByte();
            for (int slot = 0; slot < MerchantSearch.SlotsPerStall && p.RemainingBytes >= 8; slot++)
            {
                int itemId = p.ReadInt();
                int price = p.ReadInt();
                if (itemId != 0)
                    rows.Add(new MerchantSearchRow(sellerId, seller, itemId, price, type, ItemData.DisplayName(itemId)));
            }
        }
        merchantSearch.RaiseMerchantSearchRows(rows);
        if (result == MerchantSearchResult.MoreToCome) requests.Results(cursor);
        else merchantSearch.RaiseMerchantSearchLoaded();
    }
}
