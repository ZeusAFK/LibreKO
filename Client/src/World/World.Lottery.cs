using System;
using System.Collections.Generic;
using Godot;
using LibreKO.Domain;
using LibreKO.Network;

namespace LibreKO;

public partial class World
{
    private const int LotteryBodyWidth = 320;
    private const int LotteryScreenMargin = 40;
    private const int LotteryLayerIndex = 73;

    private CanvasLayer _lotteryLayer = null!;
    private HudWindow _lotteryWindow = null!;
    private Label _lotteryTimerLabel = null!;
    private Label _lotterySoldLabel = null!;
    private Label _lotteryWinnersLabel = null!;
    private Label _lotteryMyTicketsLabel = null!;
    private Label _lotteryPlayerHeader = null!;
    private TextureRect _lotteryReqIcon = null!;
    private Label _lotteryCostLabel = null!;
    private Button _lotteryBuyBtn = null!;
    private HBoxContainer _lotteryRewardsBox = null!;
    private Label _lotteryStatusLabel = null!;
    private Godot.Timer _lotteryTimer = null!;

    private LotteryState? _lotteryState;
    private bool _lotteryShown;

    private void LotteryInit()
    {
        BuildLotteryWindow();

        _lotteryTimer = new Godot.Timer { WaitTime = 1.0f, Autostart = true };
        _lotteryTimer.Timeout += OnLotteryTimerTick;
        AddChild(_lotteryTimer);

        Net.I.LotteryStateEvent += OnLotteryState;
        Net.I.LotteryJoinEvent += OnLotteryJoin;
        Net.I.LotteryProgressEvent += OnLotteryProgress;
        Net.I.LotteryEndedEvent += OnLotteryEnded;
        Net.I.LotteryCloseEvent += OnLotteryClose;

        Net.I.SendLotteryStateRequest();
    }

    private void LotteryDispose()
    {
        Net.I.LotteryStateEvent -= OnLotteryState;
        Net.I.LotteryJoinEvent -= OnLotteryJoin;
        Net.I.LotteryProgressEvent -= OnLotteryProgress;
        Net.I.LotteryEndedEvent -= OnLotteryEnded;
        Net.I.LotteryCloseEvent -= OnLotteryClose;

        if (GodotObject.IsInstanceValid(_lotteryWindow)) _lotteryWindow.QueueFree();
        if (GodotObject.IsInstanceValid(_lotteryTimer)) _lotteryTimer.QueueFree();
    }

    private void BuildLotteryWindow()
    {
        _lotteryLayer = new CanvasLayer { Layer = LotteryLayerIndex };
        AddChild(_lotteryLayer);

        var viewport = GetViewport()?.GetVisibleRect().Size ?? Vector2.Zero;
        var pos = new Vector2(
            Math.Max(LotteryScreenMargin, (viewport.X - LotteryBodyWidth) / 2f),
            120);

        _lotteryWindow = new HudWindow("lottery", "Lottery", pos, bodyMinWidth: LotteryBodyWidth, minimizable: true, closable: false)
        {
            Visible = false
        };
        _lotteryWindow.SetBackgroundAlpha(UiTheme.TranslucentWindowAlpha);
        _lotteryWindow.Closed += () => _lotteryShown = false;
        _lotteryLayer.AddChild(_lotteryWindow);

        var body = _lotteryWindow.Body;
        body.AddThemeConstantOverride("separation", 6);

        // 1. Details Section: Time Remaining, Tickets Sold, Number of People to Win
        var detailsSection = UiTheme.Section();
        var detailsBox = new VBoxContainer();
        detailsBox.AddThemeConstantOverride("separation", 3);

        var timeRow = QuestValueRow("Time Remaining :", "-- : --", UiTheme.Good);
        _lotteryTimerLabel = timeRow.GetChild<Label>(timeRow.GetChildCount() - 1);
        detailsBox.AddChild(timeRow);

        var soldRow = QuestValueRow("Tickets Sold :", "0", UiTheme.TextHi);
        _lotterySoldLabel = soldRow.GetChild<Label>(soldRow.GetChildCount() - 1);
        detailsBox.AddChild(soldRow);

        var winRow = QuestValueRow("Number of People to Win :", "4", new Color(0.35f, 1f, 0.45f));
        _lotteryWinnersLabel = winRow.GetChild<Label>(winRow.GetChildCount() - 1);
        detailsBox.AddChild(winRow);

        detailsSection.AddChild(detailsBox);
        body.AddChild(detailsSection);

        // 2. Purchased Ticket Section
        var purchasedSection = UiTheme.Section();
        var purchasedRow = new HBoxContainer();
        purchasedRow.AddThemeConstantOverride("separation", 8);

        var pLabel = UiTheme.Text("Purchased Ticket :", 13, UiTheme.TextHi);
        purchasedRow.AddChild(pLabel);

        var ticketIcon = new TextureRect
        {
            Texture = UiIcons.Get("system/gift"),
            CustomMinimumSize = new Vector2(20, 20),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
        };
        purchasedRow.AddChild(ticketIcon);

        _lotteryMyTicketsLabel = UiTheme.Text("0 / 100", 14, new Color(0.35f, 1f, 0.45f));
        _lotteryMyTicketsLabel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _lotteryMyTicketsLabel.HorizontalAlignment = HorizontalAlignment.Right;
        purchasedRow.AddChild(_lotteryMyTicketsLabel);

        purchasedSection.AddChild(purchasedRow);
        body.AddChild(purchasedSection);

        // 3. Buy Section
        var buySection = UiTheme.Section();
        var buyBox = new VBoxContainer();
        buyBox.AddThemeConstantOverride("separation", 6);

        _lotteryPlayerHeader = UiTheme.Text("", 13, UiTheme.TextDim, HorizontalAlignment.Center);
        buyBox.AddChild(_lotteryPlayerHeader);

        var buyContent = new HBoxContainer();
        buyContent.AddThemeConstantOverride("separation", 10);

        // Requirement slot
        var reqSlot = new PanelContainer
        {
            CustomMinimumSize = new Vector2(52, 52),
        };
        var reqSlotStyle = new StyleBoxFlat
        {
            BgColor = new Color(0.08f, 0.09f, 0.11f, 0.95f),
            BorderColor = new Color(0.45f, 0.45f, 0.45f, 0.8f),
            BorderWidthLeft = 1,
            BorderWidthTop = 1,
            BorderWidthRight = 1,
            BorderWidthBottom = 1,
            CornerRadiusTopLeft = 3,
            CornerRadiusTopRight = 3,
            CornerRadiusBottomRight = 3,
            CornerRadiusBottomLeft = 3,
        };
        reqSlot.AddThemeStyleboxOverride("panel", reqSlotStyle);

        _lotteryReqIcon = new TextureRect
        {
            Texture = ItemData.Icon(QuestData.CoinItemId),
            CustomMinimumSize = new Vector2(44, 44),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter,
            SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            SelfModulate = Colors.White,
        };
        reqSlot.AddChild(_lotteryReqIcon);
        buyContent.AddChild(reqSlot);

        // Right column: Price and Buy button
        var rightCol = new VBoxContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        rightCol.AddThemeConstantOverride("separation", 4);

        var priceBox = new PanelContainer();
        var priceStyle = new StyleBoxFlat
        {
            BgColor = new Color(0.05f, 0.05f, 0.07f, 0.9f),
            BorderColor = new Color(0.35f, 0.35f, 0.35f, 0.5f),
            BorderWidthLeft = 1,
            BorderWidthTop = 1,
            BorderWidthRight = 1,
            BorderWidthBottom = 1,
            CornerRadiusTopLeft = 2,
            CornerRadiusTopRight = 2,
            CornerRadiusBottomRight = 2,
            CornerRadiusBottomLeft = 2,
        };
        priceBox.AddThemeStyleboxOverride("panel", priceStyle);

        _lotteryCostLabel = UiTheme.Text("0", 16, UiTheme.GoldBright, HorizontalAlignment.Center);
        priceBox.AddChild(_lotteryCostLabel);
        rightCol.AddChild(priceBox);

        _lotteryBuyBtn = UiTheme.ActionButton("Buy 1x Ticket", "Purchase 1 ticket to enter the lottery draw");
        _lotteryBuyBtn.Pressed += OnBuyTicketPressed;
        rightCol.AddChild(_lotteryBuyBtn);

        buyContent.AddChild(rightCol);
        buyBox.AddChild(buyContent);

        _lotteryStatusLabel = UiTheme.Text("", 11, UiTheme.Good, HorizontalAlignment.Center);
        buyBox.AddChild(_lotteryStatusLabel);

        buySection.AddChild(buyBox);
        body.AddChild(buySection);

        // 4. Prize Section Header
        var prizeTitleSection = new PanelContainer();
        var prizeHeaderStyle = new StyleBoxFlat
        {
            BgColor = new Color(0.12f, 0.09f, 0.09f, 0.9f),
            BorderColor = new Color(0.55f, 0.35f, 0.35f, 0.7f),
            BorderWidthLeft = 1,
            BorderWidthTop = 1,
            BorderWidthRight = 1,
            BorderWidthBottom = 1,
        };
        prizeTitleSection.AddThemeStyleboxOverride("panel", prizeHeaderStyle);

        var prizeTitleRow = new HBoxContainer
        {
            Alignment = BoxContainer.AlignmentMode.Center,
        };
        prizeTitleRow.AddThemeConstantOverride("separation", 14);

        var bagLeft = new TextureRect
        {
            Texture = UiIcons.Get("system/gift"),
            CustomMinimumSize = new Vector2(20, 20),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
        };
        var prizeText = UiTheme.Text("Prize", 15, new Color(0.95f, 0.65f, 0.65f), HorizontalAlignment.Center);
        var bagRight = new TextureRect
        {
            Texture = UiIcons.Get("system/gift"),
            CustomMinimumSize = new Vector2(20, 20),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
        };

        prizeTitleRow.AddChild(bagLeft);
        prizeTitleRow.AddChild(prizeText);
        prizeTitleRow.AddChild(bagRight);
        prizeTitleSection.AddChild(prizeTitleRow);
        body.AddChild(prizeTitleSection);

        // Rewards slots container
        var rewardsSection = UiTheme.Section();
        _lotteryRewardsBox = new HBoxContainer
        {
            Alignment = BoxContainer.AlignmentMode.Center,
        };
        _lotteryRewardsBox.AddThemeConstantOverride("separation", 8);
        rewardsSection.AddChild(_lotteryRewardsBox);
        body.AddChild(rewardsSection);
    }

    private Control CreateRewardSlotCard(int itemId, int count)
    {
        var card = new PanelContainer
        {
            CustomMinimumSize = new Vector2(56, 56),
        };
        var slotStyle = new StyleBoxFlat
        {
            BgColor = new Color(0.08f, 0.09f, 0.11f, 0.95f),
            BorderColor = new Color(0.5f, 0.5f, 0.5f, 0.8f),
            BorderWidthLeft = 1,
            BorderWidthTop = 1,
            BorderWidthRight = 1,
            BorderWidthBottom = 1,
            CornerRadiusTopLeft = 3,
            CornerRadiusTopRight = 3,
            CornerRadiusBottomRight = 3,
            CornerRadiusBottomLeft = 3,
        };
        card.AddThemeStyleboxOverride("panel", slotStyle);

        int displayItemId = itemId is QuestData.CoinItemId or <= 0 ? QuestData.CoinItemId : itemId;
        var iconTex = ItemData.Icon(displayItemId);

        var iconRect = new TextureRect
        {
            Texture = iconTex,
            CustomMinimumSize = new Vector2(46, 46),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter,
            SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            SelfModulate = Colors.White,
        };
        card.AddChild(iconRect);

        if (count > 0)
        {
            var countLabel = UiTheme.Text(count.ToString(), 12, Colors.White);
            countLabel.SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin;
            countLabel.SizeFlagsVertical = Control.SizeFlags.ShrinkEnd;
            countLabel.MouseFilter = Control.MouseFilterEnum.Ignore;
            countLabel.AddThemeConstantOverride("outline_size", 3);
            countLabel.AddThemeColorOverride("font_outline_color", Colors.Black);
            card.AddChild(countLabel);
        }

        return QuestItemHover(card, itemId);
    }

    private void OnBuyTicketPressed()
    {
        _lotteryStatusLabel.Text = "Purchasing ticket...";
        _lotteryStatusLabel.SelfModulate = UiTheme.TextLo;
        Net.I.SendLotteryBuyTicket();
    }

    private void OnLotteryTimerTick()
    {
        if (_lotteryState == null || !_lotteryState.Active) return;
        if (_lotteryState.RemainingSeconds > 0)
        {
            _lotteryState.RemainingSeconds--;
            UpdateLotteryTimerDisplay();
        }
    }

    private void UpdateLotteryTimerDisplay()
    {
        if (_lotteryTimerLabel == null || !GodotObject.IsInstanceValid(_lotteryTimerLabel)) return;
        if (_lotteryState == null || !_lotteryState.Active)
        {
            _lotteryTimerLabel.Text = "Inactive";
            _lotteryTimerLabel.SelfModulate = UiTheme.TextDim;
            return;
        }

        int s = _lotteryState.RemainingSeconds;
        int m = s / 60;
        int sec = s % 60;
        _lotteryTimerLabel.Text = $"{m:D2} : {sec:D2}";
        _lotteryTimerLabel.SelfModulate = s <= 60 ? UiTheme.Bad : UiTheme.Good;
    }

    private void OnLotteryState(LotteryState state)
    {
        bool wasInactive = _lotteryState == null || !_lotteryState.Active;
        _lotteryState = state;
        RenderLottery();

        if (state.Active && wasInactive)
        {
            OpenLottery();
        }
    }

    private void RenderLottery()
    {
        if (_lotteryWindow == null || !GodotObject.IsInstanceValid(_lotteryWindow)) return;

        if (_lotteryState == null || !_lotteryState.Active)
        {
            _lotteryWindow.Title = "Lottery";
            _lotteryTimerLabel.Text = "Inactive";
            _lotterySoldLabel.Text = "0";
            _lotteryWinnersLabel.Text = "-";
            _lotteryMyTicketsLabel.Text = "0 / 0";
            string pName = Net.I.LastEnter.Name ?? "";
            _lotteryPlayerHeader.Text = !string.IsNullOrEmpty(pName) ? $"{pName} ({Sheet.Level})" : "";
            _lotteryCostLabel.Text = "-";
            _lotteryBuyBtn.Disabled = true;
            _lotteryReqIcon.Texture = ItemData.Icon(QuestData.CoinItemId);
            _lotteryReqIcon.SelfModulate = Colors.White;
            ClearChildren(_lotteryRewardsBox);
            return;
        }

        _lotteryWindow.Title = !string.IsNullOrEmpty(_lotteryState.Name) ? _lotteryState.Name : "Lottery";
        UpdateLotteryTimerDisplay();

        _lotterySoldLabel.Text = $"{_lotteryState.TotalTickets:N0}";
        _lotteryWinnersLabel.Text = _lotteryState.Rewards.Count > 0 ? _lotteryState.Rewards.Count.ToString() : "1";
        _lotteryMyTicketsLabel.Text = $"{_lotteryState.MyTickets} / {_lotteryState.UserLimit}";

        string myName = Net.I.LastEnter.Name ?? "";
        _lotteryPlayerHeader.Text = !string.IsNullOrEmpty(myName) ? $"{myName} ({Sheet.Level})" : _lotteryState.Name;

        // Req item / coin
        if (_lotteryState.ReqItemId is QuestData.CoinItemId or 0)
        {
            _lotteryReqIcon.Texture = ItemData.Icon(QuestData.CoinItemId);
            _lotteryReqIcon.SelfModulate = Colors.White;
            _lotteryCostLabel.Text = $"{_lotteryState.ReqItemCount:N0}";
        }
        else
        {
            _lotteryReqIcon.Texture = ItemData.Icon(_lotteryState.ReqItemId);
            _lotteryReqIcon.SelfModulate = Colors.White;
            _lotteryCostLabel.Text = $"{_lotteryState.ReqItemCount:N0} {_lotteryState.ReqItemName}";
        }

        _lotteryBuyBtn.Disabled = _lotteryState.MyTickets >= _lotteryState.UserLimit || _lotteryState.RemainingSeconds <= 0;

        // Rewards row
        ClearChildren(_lotteryRewardsBox);
        foreach (var r in _lotteryState.Rewards)
        {
            _lotteryRewardsBox.AddChild(CreateRewardSlotCard(r.ItemId, r.ItemCount));
        }
    }

    private void OnLotteryJoin(LotteryJoinResult res)
    {
        _lotteryStatusLabel.Text = res.Message;
        _lotteryStatusLabel.SelfModulate = res.Success ? UiTheme.Good : UiTheme.Bad;
        if (_lotteryState != null)
        {
            _lotteryState.MyTickets = res.MyTickets;
            _lotteryState.TotalTickets = res.TotalTickets;
            RenderLottery();
        }
    }

    private void OnLotteryProgress(int totalTickets)
    {
        if (_lotteryState != null)
        {
            _lotteryState.TotalTickets = totalTickets;
            RenderLottery();
        }
    }

    private void OnLotteryEnded(string message, List<LotteryWinner> winners)
    {
        _lotteryStatusLabel.Text = message;
        _lotteryStatusLabel.SelfModulate = UiTheme.Good;
        if (_lotteryState != null)
        {
            _lotteryState.Active = false;
            RenderLottery();
        }
    }

    private void OnLotteryClose()
    {
        CloseLottery();
    }

    public void ToggleLottery()
    {
        if (_lotteryShown) CloseLottery();
        else OpenLottery();
    }

    public void OpenLottery()
    {
        _lotteryShown = true;
        _lotteryWindow.Visible = true;
        Net.I.SendLotteryStateRequest();
        RenderLottery();
    }

    public void CloseLottery()
    {
        _lotteryShown = false;
        _lotteryWindow.Visible = false;
    }
}
