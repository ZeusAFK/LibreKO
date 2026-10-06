using System;
using System.Linq;
using Godot;
using LibreKO.Domain;
using LibreKO.Network;

namespace LibreKO;

public partial class World
{
    private const int PusPreviewCash = 12_500;

    private static readonly ShoppingMallCategory[] PusPreviewCategories =
    [
        new(1, "Farm, Growth & Collection"),
        new(2, "Premium & Packages"),
        new(3, "PK, Scroll & Potion"),
        new(4, "Services & Extra"),
    ];

    private static PowerUpStoreEntry[] PusPreviewCatalog() =>
    [
        new(1, 700002000, "", "", 1, 800, Featured: true, DiscountPrice: 640),
        new(2, 379258000, "", "", 1, 400),
        new(3, 810166000, "", "", 1, 360),
        new(4, 700011000, "", "", 1, 89, DiscountPrice: 69, DiscountEndsAt: DateTime.UtcNow.AddHours(4).AddMinutes(48)),
        new(5, 399282000, "", "", 2, 2796, Featured: true),
        new(6, 800880000, "", "", 2, 2796),
        new(7, 800003000, "", "", 3, 169),
        new(8, 800078000, "", "", 3, 249),
        new(9, 800442000, "", "", 1, 349, Featured: true),
        new(10, 810594000, "", "", 4, 299),
    ];

    internal CanvasLayer BuildPowerUpStoreUiPreview(string variant)
    {
        ItemData.EnsureLoaded();
        BuildPowerUpStore();
        Sheet.SetKnightCash(PusPreviewCash);
        _pusShown = true;
        UpdatePusWallet();

        if (variant != "pus-loading")
        {
            OnShoppingMallCatalog([.. PusPreviewCatalog()]);
            OnShoppingMallCategories([.. PusPreviewCategories]);
        }
        else
        {
            RenderPusCategories();
            RenderPusGrid();
        }

        if (variant is not ("pus-loading" or "pus-empty"))
        {
            var catalogue = _pusCatalog.ToDictionary(e => e.Id);
            _pusCart.Add(catalogue[1], 3);
            _pusCart.Add(catalogue[5]);
            _pusCart.Add(catalogue[4], 2);
        }

        _pusFriends.AddRange(
        [
            new GiftContact("Rikka", 72, 211, true, GiftContactSource.Friend),
            new GiftContact("Ariel", 64, 104, false, GiftContactSource.Friend),
            new GiftContact("Marduk", 0, 0, false, GiftContactSource.Friend),
        ]);
        _pusClanmates.AddRange(
        [
            new GiftContact("rikka", 72, 211, true, GiftContactSource.Clan),
            new GiftContact("Rikkard", 58, 107, true, GiftContactSource.Clan),
            new GiftContact("Selene", 81, 212, false, GiftContactSource.Clan),
        ]);

        switch (variant)
        {
            case "pus-category":
                _pusCategory = 1;
                RenderPusCategories();
                RenderPusGrid();
                break;
            case "pus-details":
                OpenPusDetails(_pusCatalog.First(e => e.Id == 4));
                break;
            case "pus-gift":
            case "pus-gift-search":
            case "pus-gift-card":
                _pusGiftSearch.Text = variant == "pus-gift-search" ? "rik" : "";
                ShowPusModal(PusModal.Gift);
                ShowPusGiftList();
                if (variant == "pus-gift-card")
                {
                    _pusGiftChecking = "Rikka";
                    OnShoppingMallRecipient(PowerUpStoreResult.Succeeded, "Rikka", 72, 211);
                }
                break;
            case "pus-gift-set":
                _pusGiftRecipient = new PusRecipient("Rikka", 72, 211);
                break;
            case "pus-short":
                Sheet.SetKnightCash(250);
                UpdatePusWallet();
                break;
        }

        RenderPusCart();
        RenderPusDetails();
        RemoveChild(_pusLayer);
        Callable.From(() =>
        {
            _pusWindow.Visible = true;
            FitPowerUpStore();
        }).CallDeferred();
        return _pusLayer;
    }

    internal Control BuildMailReadManyUiPreview()
    {
        ItemData.EnsureLoaded();
        BuildMailWindow();
        BuildMailComposeWindow();
        OnMailList([PusPreviewMail()]);
        SelectMail(9);
        OnMailRead(9, true, "Zeus sent you a gift from the Power-Up Store. It is attached to this mail.");
        return DetachPreviewControl(_mailReadWindow);
    }

    internal Control BuildMailStoreUiPreview()
    {
        ItemData.EnsureLoaded();
        BuildMailWindow();
        BuildMailComposeWindow();
        _mailShown = true;
        _mailWindow.Visible = true;
        OnMailList([PusPreviewMail(), .. MailUiPreviewEntries()]);
        Callable.From(FitMailList).CallDeferred();
        return DetachPreviewControl(_mailWindow);
    }

    private static MailEntry PusPreviewMail()
    {
        var mail = new MailEntry
        {
            Id = 9,
            Sender = "LibreKO",
            Subject = "A gift from Zeus",
            Read = false,
            Attachments = MailAttachmentState.Pending,
            Kind = MailKind.Store,
            SentAt = DateTime.UtcNow.AddMinutes(-3),
        };
        foreach (var entry in PusPreviewCatalog().Where(e => ItemData.Get(e.ItemId) != null))
            mail.Items.Add(new MailAttachment { Kind = MailAttachmentKind.Item, ItemId = entry.ItemId, Count = 2, Claimed = entry.Id <= 2 ? 2 : 0 });
        mail.Items[2].Claimed = 1;
        return mail;
    }
}
