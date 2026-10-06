using System;
using Godot;
using LibreKO.Domain;
using LibreKO.Network;

namespace LibreKO;

public partial class World
{
    private const int MarketPricePreviewItem = 120010000;
    private const int DisguisePreviewSkill = 472001;

    private static MarketPriceDay[] MarketPricePreviewDays() =>
    [
        new(1_250_000, 1_500_000, 1_100_000),
        new(1_180_000, 1_300_000, 1_050_000),
        new(1_320_000, 1_600_000, 1_200_000),
        new(990_000, 1_100_000, 900_000),
        new(1_050_000, 1_250_000, 950_000),
    ];

    internal Control BuildMarketPriceUiPreview(bool empty)
    {
        ItemData.EnsureLoaded();
        BuildMarketPricePanel();
        if (empty)
        {
            ClearMarketPriceItem();
            SetMarketPriceStatus(MarketPriceText(MarketPriceSelectText, "Select the item and click Search button"));
        }
        else
        {
            _marketPriceSearch.SetQuery("sword");
            _marketPriceSearch.Run();
            SelectMarketPriceItem(MarketPricePreviewItem);
            OnMarketPrice(new MarketPriceReply(MarketPrice.History, MarketPricePreviewItem, MarketPricePreviewDays(), 37, DateTime.UtcNow));
        }
        ShowMarketPricePanel();
        return DetachPreviewControl(_marketPricePanel);
    }

    internal Control BuildFortuneUiPreview(bool revealed)
    {
        ItemData.EnsureLoaded();
        FortuneInit();
        OpenFortune();
        if (revealed && _fortuneDeck.Readings.Length > 0)
        {
            var reading = _fortuneDeck.Readings[^1];
            _fortuneReading = reading;
            _fortunePick.Visible = false;
            _fortuneStage.Visible = true;
            RevealFortune(reading);
            _fortuneFinal.Stop();
        }
        return DetachPreviewControl(_fortunePanel);
    }

    internal Control BuildDisguiseUiPreview()
    {
        ItemData.EnsureLoaded();
        _disguiseTable = LoadDisguiseTable();
        BuildDisguisePanel();
        SkillData.EnsureLoaded();
        OpenDisguise(DisguisePreviewSkill);
        PickDisguiseGroup(2);
        return DetachPreviewControl(_disguisePanel);
    }

    internal Control BuildMarketPricePromptUiPreview()
    {
        Net.I.SeedPreviewPremium(1, 1, 720);
        var prompt = BuildPricePromptUiPreview(false);
        OnMarketPrice(new MarketPriceReply(MarketPrice.History, _amountMarketItem, MarketPricePreviewDays(), 12, DateTime.UtcNow));
        return prompt;
    }
}
