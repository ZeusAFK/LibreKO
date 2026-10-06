using System;
using System.Collections.Generic;
using LibreKO.Domain;

namespace LibreKO.Network;

public partial class Net
{
    public const byte MerchantSubOfficialList = 0x30;
    public const byte MerchantSearchMove = 2;
    public const byte MerchantSearchOpen = 5;
    public const byte MerchantSearchResults = 6;
    public const byte MerchantSearchAccepted = 1;
    public const byte MerchantSearchMoreToCome = 2;
    public const byte MerchantSearchLastChunk = 3;
    public const int MerchantSearchCannotUseText = 30524;
    public const int MerchantSearchMoveFailedText = 30526;
    private const int MerchantSearchSellerIdMask = 0xFFFF;

    public event Action? MerchantSearchOpenEvent;
    public event Action<IReadOnlyList<MerchantSearchRow>>? MerchantSearchRowsEvent;
    public event Action? MerchantSearchLoadedEvent;
    public event Action<int>? MerchantSearchRefusedEvent;
    public event Action? MerchantSearchMovedEvent;

    private void HandleMerchantSearch(Packet p)
    {
        if (p.RemainingBytes < 2) return;
        byte sub = p.ReadByte();
        byte result = p.ReadByte();
        switch (sub)
        {
            case MerchantSearchOpen when result == MerchantSearchAccepted:
                MerchantSearchOpenEvent?.Invoke();
                SendMerchantSearchResults(0);
                break;
            case MerchantSearchOpen:
                MerchantSearchRefusedEvent?.Invoke(MerchantSearchCannotUseText);
                break;
            case MerchantSearchResults when result == 0:
                MerchantSearchRefusedEvent?.Invoke(MerchantSearchCannotUseText);
                break;
            case MerchantSearchResults when result is MerchantSearchMoreToCome or MerchantSearchLastChunk:
                ReadMerchantSearchChunk(p, result);
                break;
            case MerchantSearchMove when result == MerchantSearchAccepted:
                MerchantSearchMovedEvent?.Invoke();
                break;
            case MerchantSearchMove:
                MerchantSearchRefusedEvent?.Invoke(MerchantSearchMoveFailedText);
                break;
        }
    }

    private void ReadMerchantSearchChunk(Packet p, byte result)
    {
        if (p.RemainingBytes < 6) return;
        int cursor = p.ReadInt();
        int count = p.ReadShort();
        var rows = new List<MerchantSearchRow>();
        for (int i = 0; i < count && p.RemainingBytes >= 5; i++)
        {
            int sellerId = p.ReadInt() & MerchantSearchSellerIdMask;
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
        MerchantSearchRowsEvent?.Invoke(rows);
        if (result == MerchantSearchMoreToCome) SendMerchantSearchResults(cursor);
        else MerchantSearchLoadedEvent?.Invoke();
    }

    public void SendMerchantSearchOpen(int itemId)
    {
        var p = new Packet(GameOpcodes.GS_MERCHANT);
        p.WriteByte(MerchantSubOfficialList);
        p.WriteByte(MerchantSearchOpen);
        p.WriteInt(itemId);
        _conn.Send(p);
    }

    public void SendMerchantSearchResults(int cursor)
    {
        var p = new Packet(GameOpcodes.GS_MERCHANT);
        p.WriteByte(MerchantSubOfficialList);
        p.WriteByte(MerchantSearchResults);
        p.WriteInt(cursor);
        _conn.Send(p);
    }

    public void SendMerchantSearchMove(int sellerId)
    {
        var p = new Packet(GameOpcodes.GS_MERCHANT);
        p.WriteByte(MerchantSubOfficialList);
        p.WriteByte(MerchantSearchMove);
        p.WriteShort((short)sellerId);
        _conn.Send(p);
    }
}
