using Godot;

namespace LibreKO.Features.Windows;

internal sealed class LotteryFeature : IFeature, IClosableWindow
{
    private readonly IWorldContext context;
    private readonly TopIconsFeature topIcons;
    private readonly LotterySession lottery;
    private readonly SelfSession self;
    private readonly ILotteryRequests requests;

    public LotteryFeature(IWorldContext context, LotterySession lottery, SelfSession self, ILotteryRequests requests, TopIconsFeature topIcons)
    {
        this.context = context;
        this.lottery = lottery;
        this.self = self;
        this.requests = requests;
        this.topIcons = topIcons;
    }

    private const int BodyWidth = 320;
    private const int ScreenMargin = 40;

    private CanvasLayer layer = null!;
    private HudWindow window = null!;
    private Label timerLabel = null!;
    private Label soldLabel = null!;
    private Label winnersLabel = null!;
    private Label myTicketsLabel = null!;
    private Label playerHeader = null!;
    private TextureRect reqIcon = null!;
    private Label costLabel = null!;
    private Button buyBtn = null!;
    private HBoxContainer rewardsBox = null!;
    private Label statusLabel = null!;
    private Godot.Timer timer = null!;
    internal LotteryState? State { get; set; }
    private bool shown;

    public bool IsOpen => shown;

    public void Build()
    {
        BuildWindow();

        timer = new Godot.Timer { WaitTime = 1.0f, Autostart = true };
        timer.Timeout += OnTimerTick;
        context.Root.AddChild(timer);

        lottery.LotteryStateEvent += OnState;
        lottery.LotteryJoinEvent += OnJoin;
        lottery.LotteryProgressEvent += OnProgress;
        lottery.LotteryEndedEvent += OnEnded;
        lottery.LotteryCloseEvent += OnClose;

        requests.StateRequest();
    }

    public void Dispose()
    {
        lottery.LotteryStateEvent -= OnState;
        lottery.LotteryJoinEvent -= OnJoin;
        lottery.LotteryProgressEvent -= OnProgress;
        lottery.LotteryEndedEvent -= OnEnded;
        lottery.LotteryCloseEvent -= OnClose;

        if (GodotObject.IsInstanceValid(window)) window.QueueFree();
        if (GodotObject.IsInstanceValid(timer)) timer.QueueFree();
    }

    private void BuildWindow()
    {
        layer = new CanvasLayer { Layer = UiLayers.Boards };
        context.Root.AddChild(layer);

        var viewport = context.Root.GetViewport()?.GetVisibleRect().Size ?? Vector2.Zero;
        var pos = new Vector2(
            Math.Max(ScreenMargin, (viewport.X - BodyWidth) / 2f),
            120);

        window = new HudWindow("lottery", "Lottery", pos, bodyMinWidth: BodyWidth, minimizable: true, closable: false)
        {
            Visible = false
        };
        window.SetBackgroundAlpha(UiTheme.TranslucentWindowAlpha);
        window.Closed += () => shown = false;
        window.MinimizedChanged += folded =>
        {
            if (State is { Active: true }) lottery.Window.SetFolded(WindowKey(State.LotteryId), folded);
        };
        layer.AddChild(window);

        var body = window.Body;
        body.AddThemeConstantOverride("separation", 6);

        // 1. Details Section: Time Remaining, Tickets Sold, Number of People to Win
        var detailsSection = UiTheme.Section();
        var detailsBox = new VBoxContainer();
        detailsBox.AddThemeConstantOverride("separation", 3);

        var timeRow = UiKit.ValueRow("Time Remaining :", "-- : --", UiTheme.Good);
        timerLabel = timeRow.GetChild<Label>(timeRow.GetChildCount() - 1);
        detailsBox.AddChild(timeRow);

        var soldRow = UiKit.ValueRow("Tickets Sold :", "0", UiTheme.TextHi);
        soldLabel = soldRow.GetChild<Label>(soldRow.GetChildCount() - 1);
        detailsBox.AddChild(soldRow);

        var winRow = UiKit.ValueRow("Number of People to Win :", "4", new Color(0.35f, 1f, 0.45f));
        winnersLabel = winRow.GetChild<Label>(winRow.GetChildCount() - 1);
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

        myTicketsLabel = UiTheme.Text("0 / 100", 14, new Color(0.35f, 1f, 0.45f));
        myTicketsLabel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        myTicketsLabel.HorizontalAlignment = HorizontalAlignment.Right;
        purchasedRow.AddChild(myTicketsLabel);

        purchasedSection.AddChild(purchasedRow);
        body.AddChild(purchasedSection);

        // 3. Buy Section
        var buySection = UiTheme.Section();
        var buyBox = new VBoxContainer();
        buyBox.AddThemeConstantOverride("separation", 6);

        playerHeader = UiTheme.Text("", 13, UiTheme.TextDim, HorizontalAlignment.Center);
        buyBox.AddChild(playerHeader);

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

        reqIcon = new TextureRect
        {
            Texture = ItemIcons.Get(QuestData.CoinItemId),
            CustomMinimumSize = new Vector2(44, 44),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter,
            SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            SelfModulate = Colors.White,
        };
        reqSlot.AddChild(reqIcon);
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

        costLabel = UiTheme.Text("0", 16, UiTheme.GoldBright, HorizontalAlignment.Center);
        priceBox.AddChild(costLabel);
        rightCol.AddChild(priceBox);

        buyBtn = UiTheme.ActionButton("Buy 1x Ticket", "Purchase 1 ticket to enter the lottery draw");
        buyBtn.Pressed += OnBuyTicketPressed;
        rightCol.AddChild(buyBtn);

        buyContent.AddChild(rightCol);
        buyBox.AddChild(buyContent);

        statusLabel = UiTheme.Text("", 11, UiTheme.Good, HorizontalAlignment.Center);
        buyBox.AddChild(statusLabel);

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
        rewardsBox = new HBoxContainer
        {
            Alignment = BoxContainer.AlignmentMode.Center,
        };
        rewardsBox.AddThemeConstantOverride("separation", 8);
        rewardsSection.AddChild(rewardsBox);
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
        var iconTex = ItemIcons.Get(displayItemId);

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

        return context.Tooltips.Hover(card, itemId);
    }

    private void OnBuyTicketPressed()
    {
        statusLabel.Text = "Purchasing ticket...";
        statusLabel.SelfModulate = UiTheme.TextLo;
        requests.BuyTicket();
    }

    private void OnTimerTick()
    {
        if (State == null || !State.Active) return;
        if (State.RemainingSeconds > 0)
        {
            State.RemainingSeconds--;
            UpdateTimerDisplay();
        }
    }

    private void UpdateTimerDisplay()
    {
        if (timerLabel == null || !GodotObject.IsInstanceValid(timerLabel)) return;
        if (State == null || !State.Active)
        {
            timerLabel.Text = "Inactive";
            timerLabel.SelfModulate = UiTheme.TextDim;
            return;
        }

        int s = State.RemainingSeconds;
        int m = s / 60;
        int sec = s % 60;
        timerLabel.Text = $"{m:D2} : {sec:D2}";
        timerLabel.SelfModulate = s <= 60 ? UiTheme.Bad : UiTheme.Good;
    }

    private void OnState(LotteryState state)
    {
        bool wasInactive = this.State == null || !this.State.Active;
        this.State = state;
        topIcons.RefreshLottery(this.State?.Active == true);
        Render();

        if (!state.Active) return;
        string key = WindowKey(state.LotteryId);
        bool newRun = lottery.Window.Observe(key, FrameClock.Now(), state.RemainingSeconds);
        if (!newRun && (!wasInactive || lottery.Window.Hidden(key))) return;
        Open();
        window.SetMinimized(lottery.Window.Folded(key));
    }

    private string WindowKey(int lotteryId) => $"{self.MyCharId}:{lotteryId}";

    private void Render()
    {
        if (window == null || !GodotObject.IsInstanceValid(window)) return;

        if (State == null || !State.Active)
        {
            window.Title = "Lottery";
            timerLabel.Text = "Inactive";
            soldLabel.Text = "0";
            winnersLabel.Text = "-";
            myTicketsLabel.Text = "0 / 0";
            string pName = self.LastEnter.Name ?? "";
            playerHeader.Text = !string.IsNullOrEmpty(pName) ? $"{pName} ({self.Sheet.Level})" : "";
            costLabel.Text = "-";
            buyBtn.Disabled = true;
            reqIcon.Texture = ItemIcons.Get(QuestData.CoinItemId);
            reqIcon.SelfModulate = Colors.White;
            UiKit.ClearChildren(rewardsBox);
            return;
        }

        window.Title = !string.IsNullOrEmpty(State.Name) ? State.Name : "Lottery";
        UpdateTimerDisplay();

        soldLabel.Text = $"{State.TotalTickets:N0}";
        winnersLabel.Text = State.Rewards.Count > 0 ? State.Rewards.Count.ToString() : "1";
        myTicketsLabel.Text = $"{State.MyTickets} / {State.UserLimit}";

        string myName = self.LastEnter.Name ?? "";
        playerHeader.Text = !string.IsNullOrEmpty(myName) ? $"{myName} ({self.Sheet.Level})" : State.Name;

        // Req item / coin
        if (State.ReqItemId is QuestData.CoinItemId or 0)
        {
            reqIcon.Texture = ItemIcons.Get(QuestData.CoinItemId);
            reqIcon.SelfModulate = Colors.White;
            costLabel.Text = $"{State.ReqItemCount:N0}";
        }
        else
        {
            reqIcon.Texture = ItemIcons.Get(State.ReqItemId);
            reqIcon.SelfModulate = Colors.White;
            costLabel.Text = $"{State.ReqItemCount:N0} {State.ReqItemName}";
        }

        buyBtn.Disabled = State.MyTickets >= State.UserLimit || State.RemainingSeconds <= 0;

        // Rewards row
        UiKit.ClearChildren(rewardsBox);
        foreach (var r in State.Rewards)
        {
            rewardsBox.AddChild(CreateRewardSlotCard(r.ItemId, r.ItemCount));
        }
    }

    private void OnJoin(LotteryJoinResult res)
    {
        statusLabel.Text = res.Message;
        statusLabel.SelfModulate = res.Success ? UiTheme.Good : UiTheme.Bad;
        if (State != null)
        {
            State.MyTickets = res.MyTickets;
            State.TotalTickets = res.TotalTickets;
            Render();
        }
    }

    private void OnProgress(int totalTickets)
    {
        if (State != null)
        {
            State.TotalTickets = totalTickets;
            Render();
        }
    }

    private void OnEnded(string message, List<LotteryWinner> winners)
    {
        statusLabel.Text = message;
        statusLabel.SelfModulate = UiTheme.Good;
        if (State != null)
        {
            State.Active = false;
            topIcons.RefreshLottery(State?.Active == true);
            Render();
        }
    }

    private void OnClose()
    {
        Close();
    }

    public void Toggle()
    {
        if (shown) Close();
        else Open();
    }

    public void Open()
    {
        shown = true;
        window.Visible = true;
        if (State is { Active: true }) lottery.Window.SetHidden(WindowKey(State.LotteryId), false);
        requests.StateRequest();
        Render();
    }

    public void Close()
    {
        shown = false;
        window.Visible = false;
        if (State is { Active: true }) lottery.Window.SetHidden(WindowKey(State.LotteryId), true);
    }
}
