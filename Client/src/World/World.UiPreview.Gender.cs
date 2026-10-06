using Godot;
using LibreKO.Network;

namespace LibreKO;

public partial class World
{
    internal Control BuildGenderChangeUiPreview()
    {
        ItemData.EnsureLoaded();
        var me = Net.I.LastEnter;
        me.Name = "Zeus";
        me.Nation = 2;
        me.Class = 208;
        me.Race = 12;
        me.Face = 1;
        me.Hair = (2 << 24) | 0x5A3820;
        Net.I.SeedPreviewEnter(me);
        BuildGenderPanel();
        OpenGenderChange();
        return DetachPreviewControl(_genderPanel);
    }

    internal Control BuildNationTransferUiPreview()
    {
        ItemData.EnsureLoaded();
        BuildNationTransferPanel();
        OpenNationTransfer(new[]
        {
            new NationTransferCandidate(0, "Zeus", 2, 1, 108, 1, (2 << 24) | 0x5A3820),
            new NationTransferCandidate(1, "Rikka", 4, 1, 111, 2, (1 << 24) | 0x302010),
            new NationTransferCandidate(2, "Ares", 1, 1, 106, 0, 0x101010),
        });
        return DetachPreviewControl(_transferPanel);
    }

    internal Control BuildMerchantSearchUiPreview()
    {
        ItemData.EnsureLoaded();
        BuildMerchantSearchPanel();
        OnMerchantSearchOpen();
        int[] items = [120010000, 389010000, 389040000, 810166000, 700002000, 379258000, 800003000, 810117000, 508073000, 800032000, 810164000, 700047000];
        var rows = new System.Collections.Generic.List<MerchantSearchRow>();
        for (int i = 0; i < items.Length; i++)
            rows.Add(new MerchantSearchRow(200 + i % 3, $"Seller{i % 3}", items[i], 1000 * (i + 1) + 70, i % 4 == 3 ? MerchantSearch.BuyingType : MerchantSearch.SellingType, ItemData.DisplayName(items[i])));
        OnMerchantSearchRows(rows);
        OnMerchantSearchLoaded();
        return DetachPreviewControl(_merchantSearchPanel);
    }
}
