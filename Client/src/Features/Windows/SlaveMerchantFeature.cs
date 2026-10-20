using Godot;

namespace LibreKO.Features.Windows;

internal sealed class SlaveMerchantFeature : IFeature, IEscapable
{
    private readonly IWorldContext context;
    private readonly SelfSession self;
    private readonly SlaveMerchantSession slaveMerchant;
    private readonly ISlaveMerchantRequests requests;
    private readonly MerchantFeature merchantWindow;

    public SlaveMerchantFeature(IWorldContext context, SelfSession self, SlaveMerchantSession slaveMerchant, ISlaveMerchantRequests requests, MerchantFeature merchantWindow)
    {
        this.context = context;
        this.self = self;
        this.slaveMerchant = slaveMerchant;
        this.requests = requests;
        this.merchantWindow = merchantWindow;
    }

    private const string Title = "Slave Merchant";
    private const int HintWidth = 300;

    internal CanvasLayer Layer { get; set; } = null!;
    internal HudWindow Window { get; private set; } = null!;
    private MerchantCell[] cells = null!;
    private Label state = null!;
    private Label coins = null!;
    private Label hours = null!;
    private Button collect = null!;
    private Notice? redeemAsk;

    public void Build()
    {
        Layer = new CanvasLayer { Layer = UiLayers.ServiceWindows };
        context.Root.AddChild(Layer);
        BuildWindow();
        merchantWindow.SlaveMerchantRequested += Open;
        slaveMerchant.StatusEvent += OnStatus;
        slaveMerchant.HoursEvent += OnHours;
        slaveMerchant.RedeemEvent += OnRedeem;
        slaveMerchant.DeployEvent += OnDeploy;
        slaveMerchant.CollectEvent += OnCollect;
        slaveMerchant.SaleEvent += OnSale;
        slaveMerchant.ClosedEvent += OnClosed;
    }

    public void Dispose()
    {
        merchantWindow.SlaveMerchantRequested -= Open;
        slaveMerchant.StatusEvent -= OnStatus;
        slaveMerchant.HoursEvent -= OnHours;
        slaveMerchant.RedeemEvent -= OnRedeem;
        slaveMerchant.DeployEvent -= OnDeploy;
        slaveMerchant.CollectEvent -= OnCollect;
        slaveMerchant.SaleEvent -= OnSale;
        slaveMerchant.ClosedEvent -= OnClosed;
        redeemAsk?.QueueFree();
    }

    bool IEscapable.CloseOnEscape()
    {
        if (!Window.Visible) return false;
        Close();
        return true;
    }

    internal void BuildWindow()
    {
        Window = new HudWindow("slavemerchant", Title) { Visible = false };
        Window.Closed += Close;
        Layer.AddChild(Window);

        var root = Window.Body;
        root.AddThemeConstantOverride("separation", 8);

        root.AddChild(UiTheme.SectionTitle("On the stall"));
        cells = MerchantUi.BuildGrid(context.Tooltips, root, MerchantUi.StallSlots, MerchantUi.StallColumns, null);

        state = UiTheme.Text("", 12, UiTheme.TextLo);
        state.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        state.CustomMinimumSize = new Vector2(HintWidth, 0);
        root.AddChild(state);

        var money = UiTheme.Section();
        root.AddChild(money);
        var moneyBox = new VBoxContainer();
        moneyBox.AddThemeConstantOverride("separation", 3);
        money.AddChild(moneyBox);
        moneyBox.AddChild(MerchantUi.MoneyRow("Coins", out coins, UiTheme.GoldBright));
        moneyBox.AddChild(MerchantUi.MoneyRow("Slave Merchant time", out hours, UiTheme.TextHi));

        var hint = UiTheme.Text("Tick Slave Merchant when you open a selling stall to hand it over. Each hour of selling uses one hour of time.",
            11, UiTheme.TextDim);
        hint.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        hint.CustomMinimumSize = new Vector2(HintWidth, 0);
        root.AddChild(hint);

        var footer = new HBoxContainer();
        footer.AddThemeConstantOverride("separation", 8);
        footer.Alignment = BoxContainer.AlignmentMode.Center;
        root.AddChild(footer);
        collect = new Button { Text = "Close and collect", FocusMode = Control.FocusModeEnum.None,
            TooltipText = "Close the Slave Merchant: unsold items come back to your bag and its coins are yours." };
        collect.CustomMinimumSize = new Vector2(150, 28);
        collect.Pressed += Collect;
        footer.AddChild(collect);
        var close = new Button { Text = "Close", FocusMode = Control.FocusModeEnum.None };
        close.CustomMinimumSize = new Vector2(104, 28);
        close.Pressed += Close;
        footer.AddChild(close);

        ShowStatus(slaveMerchant.Status);
    }

    internal void Open()
    {
        if (context.Self.Dead) return;
        UiKit.ShowOnTop(Window);
        ShowStatus(slaveMerchant.Status);
        requests.Status();
    }

    internal void Close()
    {
        context.Tooltips.Hide();
        Window.Visible = false;
    }

    private void Collect()
    {
        if (context.Self.Dead) return;
        requests.Collect();
    }

    internal bool AskRedeem(int absSlot)
    {
        if (ItemMove.RegionOf(absSlot) != ItemMove.Region.Grid) return false;
        int itemId = self.Inventory[absSlot].ItemId;
        int added = SlaveMerchant.HoursOf(itemId);
        if (added == 0) return false;
        if (context.Self.Dead) return true;

        redeemAsk?.QueueFree();
        byte bagPosition = (byte)(absSlot - Inventory.GridStart);
        redeemAsk = Notice.Confirm(context.Root, $"Use the certificate to add {added} hours of Slave Merchant time?", "Yes", "No", () =>
        {
            redeemAsk = null;
            if (self.Inventory[absSlot].ItemId == itemId) requests.Redeem(bagPosition, itemId);
        }, () => redeemAsk = null, title: Title);
        return true;
    }

    private void OnStatus(SlaveMerchantStatus status) => ShowStatus(status);

    internal void ShowStatus(SlaveMerchantStatus? status)
    {
        for (int i = 0; i < cells.Length; i++)
        {
            var line = status != null && i < status.Lines.Length ? status.Lines[i] : default;
            cells[i].Set(line.IsEmpty ? default : new ItemSlot { ItemId = line.ItemId, Count = (short)Math.Min(line.Count, short.MaxValue) },
                line.IsEmpty ? "" : MerchantUi.PriceNote("Price", line.Price, line.Count));
        }

        bool holds = status != null && (status.Coins > 0 || status.Lines.Any(line => !line.IsEmpty));
        state.Text = status switch
        {
            { Open: true } => "Your Slave Merchant is selling.",
            _ when holds => "Your Slave Merchant has closed. Collect its coins and the items it did not sell.",
            _ => "No Slave Merchant is open.",
        };
        coins.Text = MerchantUi.Money(status?.Coins ?? 0);
        collect.Disabled = status == null || (!status.Open && !holds);
        OnHours(slaveMerchant.Hours);
    }

    private void OnHours(int value) =>
        hours.Text = value == SlaveMerchantSession.UnknownHours ? "-" : $"{value} {(value == 1 ? "hour" : "hours")}";

    private void OnRedeem(SlaveMerchantRedeem redeem) =>
        context.Log.Notice(redeem.Added
            ? $"Slave Merchant time: {redeem.Hours} hours."
            : "The certificate could not be used.", redeem.Added ? null : UiTheme.Bad);

    private void OnDeploy(SlaveMerchantDeployResult result)
    {
        context.Log.Notice(result switch
        {
            SlaveMerchantDeployResult.Deployed => "Your Slave Merchant took over your stall.",
            SlaveMerchantDeployResult.NoTime => "You have no Slave Merchant time. Use a Slave Merchant Certificate.",
            SlaveMerchantDeployResult.AlreadyOpen => "Collect your Slave Merchant before you hand it another stall.",
            SlaveMerchantDeployResult.ItemsMoved => "An item left its bag slot. Open the stall again.",
            _ => "Your stall could not be handed over. Nothing left your bag; try again.",
        }, result == SlaveMerchantDeployResult.Deployed ? null : UiTheme.Bad);
        if (result == SlaveMerchantDeployResult.Deployed && Window.Visible) requests.Status();
    }

    private void OnCollect(SlaveMerchantCollected collected)
    {
        if (!collected.Collected)
        {
            context.Log.Notice("Nothing could be collected right now. Everything stays with your Slave Merchant; try again.", UiTheme.Bad);
            return;
        }

        if (collected.CoinsPaid > 0 || collected.ItemsReturned > 0)
            context.Log.Notice($"Collected {MerchantUi.Money(collected.CoinsPaid)} coins and {collected.ItemsReturned} {Items(collected.ItemsReturned)} from your Slave Merchant.");
        if (collected.ItemsKept > 0)
            context.Log.Notice($"{collected.ItemsKept} {Items(collected.ItemsKept)} stay with your Slave Merchant. Make room in your bag and collect again.", UiTheme.Bad);
        if (collected.CoinsKept > 0)
            context.Log.Notice($"{MerchantUi.Money(collected.CoinsKept)} coins stay with your Slave Merchant: you carry as many as you can.", UiTheme.Bad);
    }

    private void OnSale(SlaveMerchantSale sale)
    {
        context.Log.Notice($"Your Slave Merchant sold {ItemData.DisplayName(sale.ItemId)} x{sale.Count} for {MerchantUi.Money(sale.Coins)} coins.");
        if (Window.Visible) requests.Status();
    }

    private void OnClosed(SlaveMerchantClosedReason reason)
    {
        context.Log.Notice(reason == SlaveMerchantClosedReason.SoldOut
            ? "Your Slave Merchant sold everything. Collect its coins in the Slave Merchant window."
            : "Your Slave Merchant ran out of time. Collect its coins and items in the Slave Merchant window.");
        if (Window.Visible) requests.Status();
    }

    private static string Items(int count) => count == 1 ? "item" : "items";
}
