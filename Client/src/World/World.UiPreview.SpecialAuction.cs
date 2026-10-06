using Godot;
using LibreKO.Domain;
using LibreKO.Network;

namespace LibreKO;

public partial class World
{
    private const int AuctionPreviewDay = 2;
    private const int AuctionPreviewSeconds = 2 * 3600 + 14 * 60 + 9;
    private const int AgilityNecklace = 1320510810;
    private const int StrengthNecklace = 1320610820;
    private const int ShadowPiece = 700009000;
    private const int ElfMetalEarrings = 1310610106;

    internal Control BuildSpecialAuctionUiPreview(string view)
    {
        ItemData.EnsureLoaded();
        var me = Net.I.LastEnter;
        me.Name = "Zeus";
        Net.I.SeedPreviewEnter(me);
        Sheet.SetGold(845_300_000);
        Inv.EnsureLength(GridStart + GridCount);
        Inv[GridStart + 3] = PreviewItem(SpecialAuction.MythrilCheck, 1, 1);
        Inv[GridStart + 7] = PreviewItem(SpecialAuction.MythrilCheck, 1, 1);

        EnsureAuctionTable();
        BuildSpecialAuctionPanel();
        ResetSpecialAuction();
        _auctionOpening = true;
        OnAuctionToday(new AuctionToday(SpecialAuction.Bidding, 1, AuctionPreviewSeconds, AuctionPreviewDay,
        [
            new AuctionOffer(0, AgilityNecklace, 0, ""),
            new AuctionOffer(1, StrengthNecklace, 1_012_000_000, "Rikka"),
            new AuctionOffer(2, ShadowPiece, 230_000_000, "Zeus"),
        ]));
        SelectAuctionLot(1);
        _auctionMillions.Value = 13;
        _auctionCheckInput.Value = 1;
        RefreshAuctionTotal();

        switch (view)
        {
            case "schedule":
                ShowAuctionTab(AuctionTab.Schedule);
                break;
            case "myinfo":
                OnAuctionMyInfo(SpecialAuction.Success,
                [
                    new AuctionBidRow(1, 2, 2, ShadowPiece, 1, 9779, 0, 230_000_000, SpecialAuction.TopBidder),
                    new AuctionBidRow(1, 2, 1, StrengthNecklace, 1, 9779, 0, 1_005_000_000, SpecialAuction.Outbid),
                    new AuctionBidRow(1, 1, 0, ElfMetalEarrings, 1, 9778, 0, 6_000_000, SpecialAuction.Won),
                ]);
                break;
            case "log":
                OnAuctionLog(
                [
                    [new AuctionResultLine(ElfMetalEarrings, 6_000_000, SpecialAuction.Won), new AuctionResultLine(1310514122, 0, SpecialAuction.Cancelled),
                     new AuctionResultLine(1399312170, 41_000_000, SpecialAuction.Won)],
                    [new AuctionResultLine(1310510104, 2_150_000_000, SpecialAuction.Won), new AuctionResultLine(1310513121, 17_000_000, SpecialAuction.Won),
                     new AuctionResultLine(1340410115, 0, SpecialAuction.Cancelled)],
                ]);
                break;
        }
        return DetachPreviewControl(_specialAuctionPanel);
    }
}
