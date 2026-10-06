using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using LibreKO.Domain;
using LibreKO.Network;

namespace LibreKO;

public partial class World
{
    private const int PusLayerIndex = 73;
    private const string PusRechargeUrl = "https://libreko.org";
    private const int PusColumnGap = 10;
    private const int PusCashIconSize = 15;
    private const int PusCashFontSize = 14;
    private const double PusTimerTickSeconds = 15;
    private const float PusStrikeWidth = 1.5f;
    private const float PusStrikeHeight = 0.55f;

    private static readonly Color PusWindowFill = new(0.066f, 0.068f, 0.075f, 0.975f);
    private static readonly Color PusWellFill = new(0.022f, 0.022f, 0.027f, 0.8f);
    private static readonly Color PusCardFill = new(0.072f, 0.071f, 0.080f, 0.9f);

    private enum PusPurchase { None, Cart, BuyNow }

    private sealed record PusRecipient(string Name, int Level, int Class);

    private CanvasLayer _pusLayer = null!;
    private HudWindow _pusWindow = null!;
    private Viewport? _pusViewport;
    private Label _pusCashValue = null!;
    private Godot.Timer _pusTicker = null!;
    private bool _pusShown;

    private readonly List<PowerUpStoreEntry> _pusCatalog = new();
    private readonly List<ShoppingMallCategory> _pusCategories = new();
    private readonly PowerUpStoreCart _pusCart = new();
    private bool _pusLoaded;
    private bool _pusRefreshing;
    private string _pusStoreError = "";
    private PusPurchase _pusPending;

    private void ShoppingMallInit()
    {
        BuildPowerUpStore();
        Net.I.ShoppingMallOpenEvent += OnShoppingMallOpen;
        Net.I.ShoppingMallCatalogEvent += OnShoppingMallCatalog;
        Net.I.ShoppingMallCategoriesEvent += OnShoppingMallCategories;
        Net.I.ShoppingMallBalanceEvent += OnShoppingMallBalance;
        Net.I.ShoppingMallPurchaseEvent += OnShoppingMallPurchase;
        Net.I.ShoppingMallRecipientEvent += OnShoppingMallRecipient;
        Net.I.FriendListEvent += OnPusFriends;
        Net.I.ClanMembersEvent += OnPusClanMembers;
        _pusViewport = GetViewport();
        if (_pusViewport != null) _pusViewport.SizeChanged += FitPowerUpStore;
    }

    private void ShoppingMallDispose()
    {
        Net.I.ShoppingMallOpenEvent -= OnShoppingMallOpen;
        Net.I.ShoppingMallCatalogEvent -= OnShoppingMallCatalog;
        Net.I.ShoppingMallCategoriesEvent -= OnShoppingMallCategories;
        Net.I.ShoppingMallBalanceEvent -= OnShoppingMallBalance;
        Net.I.ShoppingMallPurchaseEvent -= OnShoppingMallPurchase;
        Net.I.ShoppingMallRecipientEvent -= OnShoppingMallRecipient;
        Net.I.FriendListEvent -= OnPusFriends;
        Net.I.ClanMembersEvent -= OnPusClanMembers;
        if (_pusViewport != null && IsInstanceValid(_pusViewport))
            _pusViewport.SizeChanged -= FitPowerUpStore;
        _pusViewport = null;
    }

    private void BuildPowerUpStore()
    {
        _pusLayer = new CanvasLayer { Layer = PusLayerIndex };
        AddChild(_pusLayer);

        _pusWindow = new HudWindow("shoppingmall", "Power-Up Store", persistLayout: false) { Visible = false };
        var panel = UiTheme.WindowPanel();
        panel.BgColor = PusWindowFill;
        _pusWindow.AddThemeStyleboxOverride("panel", panel);
        _pusWindow.Closed += CloseShoppingMall;
        _pusLayer.AddChild(_pusWindow);

        var columns = new HBoxContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        columns.AddThemeConstantOverride("separation", PusColumnGap);
        _pusWindow.Body.AddChild(columns);
        columns.AddChild(BuildPusCategoryColumn());
        columns.AddChild(BuildPusGridColumn());
        columns.AddChild(BuildPusCartColumn());

        BuildPusModal();

        _pusTicker = new Godot.Timer { WaitTime = PusTimerTickSeconds };
        _pusTicker.Timeout += TickPusTimers;
        _pusLayer.AddChild(_pusTicker);
    }

    private Control BuildPusCashRow()
    {
        var row = new HBoxContainer { SizeFlagsVertical = Control.SizeFlags.ShrinkCenter };
        row.AddThemeConstantOverride("separation", 6);
        var pill = new PanelContainer { TooltipText = "Your cash", MouseFilter = Control.MouseFilterEnum.Pass };
        var style = UiTheme.Chip();
        style.ContentMarginTop = style.ContentMarginBottom = 2;
        pill.AddThemeStyleboxOverride("panel", style);
        var cash = new HBoxContainer();
        cash.AddThemeConstantOverride("separation", 5);
        pill.AddChild(cash);
        cash.AddChild(PusGem(PusCashIconSize));
        _pusCashValue = UiTheme.Text("0", PusCashFontSize, UiTheme.TextHi);
        cash.AddChild(_pusCashValue);
        row.AddChild(pill);
        var recharge = UiTheme.IconButton("+", "Get more cash on libreko.org");
        recharge.Pressed += () => OS.ShellOpen(PusRechargeUrl);
        row.AddChild(recharge);
        return row;
    }

    private static TextureRect PusGem(int size) => new()
    {
        Texture = UiIcons.Get("system/gem"),
        CustomMinimumSize = new Vector2(size, size),
        ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
        StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
        SelfModulate = UiTheme.Premium,
        SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
        MouseFilter = Control.MouseFilterEnum.Ignore,
    };

    private static HBoxContainer PusPrice(long amount, int fontSize, Color? color = null)
    {
        var row = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        row.AddThemeConstantOverride("separation", 4);
        row.AddChild(PusGem(fontSize));
        row.AddChild(UiTheme.Text(amount.ToString("n0"), fontSize, color ?? UiTheme.Premium));
        return row;
    }

    private static HBoxContainer PusPriceTag(PowerUpStoreEntry entry, int fontSize, Color? color = null)
    {
        if (!entry.Discounted) return PusPrice(entry.Price, fontSize, color);
        var row = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        row.AddThemeConstantOverride("separation", 4);
        row.AddChild(PusGem(fontSize));
        var was = UiTheme.Text(entry.BasePrice.ToString("n0"), fontSize - 2, UiTheme.TextDim);
        was.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        var strike = new ColorRect
        {
            Color = UiTheme.TextDim,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            AnchorRight = 1f,
            AnchorTop = PusStrikeHeight,
            AnchorBottom = PusStrikeHeight,
            OffsetTop = -PusStrikeWidth / 2,
            OffsetBottom = PusStrikeWidth / 2,
        };
        was.AddChild(strike);
        row.AddChild(was);
        row.AddChild(UiTheme.Text("→", fontSize - 2, UiTheme.TextDim));
        row.AddChild(UiTheme.Text(entry.Price.ToString("n0"), fontSize, color ?? UiTheme.Premium));
        return row;
    }

    public void ToggleShoppingMall()
    {
        if (_pusShown) CloseShoppingMall();
        else OpenShoppingMall();
    }

    public void OpenPowerUpStore() => OpenShoppingMall();

    public void OpenShoppingMall()
    {
        if (_pusShown) return;
        _pusShown = true;
        _pusStoreError = "";
        _pusWindow.Visible = true;
        UpdatePusWallet();
        RenderPusCategories();
        RenderPusGrid();
        RenderPusCart();
        _pusTicker.Start();
        Callable.From(FitPowerUpStore).CallDeferred();
        Net.I.SendShoppingMallOpen();
    }

    public void CloseShoppingMall()
    {
        if (!_pusShown) return;
        _pusShown = false;
        _pusGiftChecking = "";
        ShowPusModal(PusModal.None);
        _pusTicker.Stop();
        _pusWindow.Visible = false;
        Net.I.SendShoppingMallClose();
    }

    private void FitPowerUpStore()
    {
        if (!_pusShown || !IsInstanceValid(_pusWindow) || !_pusWindow.IsInsideTree()) return;
        var target = PowerUpStoreLayout.WindowSize(_pusWindow.GetViewportRect().Size, Platform.TouchUi);
        var chrome = _pusWindow.GetCombinedMinimumSize() - _pusWindow.Body.GetCombinedMinimumSize();
        _pusWindow.Body.CustomMinimumSize = (target - chrome).Max(Vector2.Zero);
        _pusWindow.ResetSize();
        CentrePowerUpStore();
        _pusWindow.GetTree().Connect(SceneTree.SignalName.ProcessFrame, Callable.From(CentrePowerUpStore),
            (uint)ConnectFlags.OneShot);
    }

    private void CentrePowerUpStore()
    {
        if (!_pusShown || !IsInstanceValid(_pusWindow)) return;
        _pusWindow.Position = WindowPlacement.Centre(_pusWindow.GetViewportRect().Size, _pusWindow.Size);
    }

    private void OnShoppingMallOpen(short error, short freeSlot)
    {
        _pusStoreError = error switch
        {
            1 => "",
            -2 => "You can't shop while dead.",
            -3 => "Close your trade first.",
            -4 => "Close your stall first.",
            -5 => "The store is closed in this zone.",
            _ => "The store couldn't open.",
        };
        RenderPusGrid();
    }

    private void OnShoppingMallCatalog(List<PowerUpStoreEntry> entries)
    {
        _pusCatalog.Clear();
        foreach (var entry in entries)
        {
            var item = ItemData.Get(entry.ItemId);
            if (item == null) continue;
            _pusCatalog.Add(entry with
            {
                Name = ItemData.DisplayName(entry.ItemId),
                Description = string.Join("\n", SplitDescription(item.Desc)),
            });
        }
        _pusLoaded = true;
        var repriced = _pusCart.Reprice(_pusCatalog);
        if (_pusRefreshing && repriced)
            SetPusCartStatus("Prices changed. Check your cart before buying.", true);
        _pusRefreshing = false;
        if (!_pusCatalog.Exists(e => e.Featured) && _pusCategory == PowerUpStoreCatalog.FeaturedCategory && _pusCategories.Count > 0)
            _pusCategory = _pusCategories[0].Id;
        RenderPusCategories();
        RenderPusGrid();
        RenderPusCart();
    }

    private void OnShoppingMallCategories(List<ShoppingMallCategory> categories)
    {
        _pusCategories.Clear();
        _pusCategories.AddRange(categories);
        if (_pusCategory != PowerUpStoreCatalog.FeaturedCategory
            && !_pusCategories.Exists(c => c.Id == _pusCategory) && _pusCategories.Count > 0)
            _pusCategory = _pusCategories[0].Id;
        if (!_pusCatalog.Exists(e => e.Featured) && _pusCategory == PowerUpStoreCatalog.FeaturedCategory && _pusCategories.Count > 0)
            _pusCategory = _pusCategories[0].Id;
        RenderPusCategories();
        RenderPusGrid();
    }

    private void RefreshPusCatalogue()
    {
        if (_pusRefreshing) return;
        _pusRefreshing = true;
        Net.I.SendShoppingMallOpen();
    }

    private void OnShoppingMallBalance(int knightCash)
    {
        Sheet.SetKnightCash(knightCash);
        UpdateStatusHud();
        UpdatePusWallet();
        RefreshAdminCash();
        RenderPusCart();
        RenderPusDetails();
    }

    private void UpdatePusWallet()
    {
        if (!IsInstanceValid(_pusCashValue)) return;
        _pusCashValue.Text = Sheet.KnightCash.ToString("n0");
    }

    private void TickPusTimers()
    {
        var now = DateTime.UtcNow;
        var ended = false;
        var timers = _pusDetailsTimer is { } details ? _pusTimerLabels.Append(details) : _pusTimerLabels;
        foreach (var (label, endsAt) in timers)
        {
            if (!IsInstanceValid(label)) continue;
            var text = PowerUpStoreTimer.Label(endsAt - now);
            label.Text = text;
            ended |= text.Length == 0;
        }
        if (ended && _pusShown) RefreshPusCatalogue();
    }

    private void OnShoppingMallPurchase(PowerUpStoreResult result, int knightCash)
    {
        var pending = _pusPending;
        _pusPending = PusPurchase.None;
        if (knightCash >= 0) OnShoppingMallBalance(knightCash);

        if (result == PowerUpStoreResult.Succeeded)
        {
            string message;
            if (pending == PusPurchase.BuyNow)
            {
                ShowPusModal(PusModal.None);
                message = "Bought. The item is waiting in your mailbox.";
            }
            else if (_pusGiftRecipient is { } recipient)
            {
                message = $"Gift sent. {recipient.Name} receives it by mail.";
                _pusCart.Clear();
                _pusGiftRecipient = null;
            }
            else
            {
                message = "Bought. Your items are waiting in your mailbox.";
                _pusCart.Clear();
            }
            SetPusCartStatus(message, false);
            CombatNotice(message);
            RenderPusCart();
            return;
        }

        if (result == PowerUpStoreResult.PriceChanged)
            RefreshPusCatalogue();
        var failure = PusFailureText(result, pending);
        if (pending == PusPurchase.BuyNow) SetPusDetailsStatus(failure);
        else SetPusCartStatus(failure, true);
        RenderPusCart();
        RenderPusDetails();
    }

    private static string PusFailureText(PowerUpStoreResult result, PusPurchase pending) => result switch
    {
        PowerUpStoreResult.NotEnoughCash => "You don't have enough cash for this.",
        PowerUpStoreResult.Unavailable => pending == PusPurchase.BuyNow
            ? "This item is no longer sold."
            : "Something in your cart is no longer sold. Remove it and try again.",
        PowerUpStoreResult.RecipientNotFound => "The gift recipient no longer exists. Choose the recipient again.",
        PowerUpStoreResult.RecipientIsSelf => "You can't send a gift to yourself.",
        PowerUpStoreResult.RecipientNotAllowed => "Gifts go only to friends and clan members. Choose the recipient again.",
        PowerUpStoreResult.PriceChanged => "A price changed. The store was refreshed; check the price and buy again.",
        _ => "The purchase didn't go through. Try again.",
    };
}
