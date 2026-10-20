using System.Linq;
using Godot;
using LibreKO.Network;
using LibreKO.Network.Requests;
using LibreKO.Scene;

namespace LibreKO.Features.Windows;

internal sealed class SlaveMerchantFeature : IFeature
{
    private readonly IWorldContext context;
    private readonly WorldRoot world;
    private readonly SelfSession self;
    private readonly IChatRequests chatRequests;
    private readonly MerchantSession merchantSession;

    public const int VoucherOfflineMerchant = 700111000;
    public const int ItemOfflineMerchant = 924041913;
    public const int ItemSlaveScroll = 751730000;

    public SlaveMerchantFeature(IWorldContext context, WorldRoot world, SelfSession self, IChatRequests chatRequests, MerchantSession merchantSession)
    {
        this.context = context;
        this.world = world;
        this.self = self;
        this.chatRequests = chatRequests;
        this.merchantSession = merchantSession;
    }

    private const string Title = "Slave Merchant";
    private const int BodyWidth = 360;

    internal CanvasLayer Layer { get; set; } = null!;
    internal HudWindow Window { get; private set; } = null!;
    private Label statusLabel = null!;
    private Label tipLabel = null!;
    private Label stallHeaderLabel = null!;
    private MerchantCell[] stallCells = null!;

    public static bool IsSlaveMerchantItem(int itemId) =>
        itemId is VoucherOfflineMerchant or ItemOfflineMerchant or ItemSlaveScroll;

    public void Build()
    {
        Layer = new CanvasLayer { Layer = UiLayers.SideWindows };
        context.Root.AddChild(Layer);
        BuildWindow();

        merchantSession.MerchantListEvent += OnMerchantList;
        merchantSession.MerchantStallClosedEvent += OnStallClosed;
    }

    public void Dispose()
    {
        merchantSession.MerchantListEvent -= OnMerchantList;
        merchantSession.MerchantStallClosedEvent -= OnStallClosed;
    }

    private void BuildWindow()
    {
        Window = new HudWindow("slave_merchant", Title, BodyWidth) { Visible = false };
        Window.Closed += Close;
        Layer.AddChild(Window);

        var body = new VBoxContainer();
        body.AddThemeConstantOverride("separation", 10);
        Window.Body.AddChild(body);

        // Header info card
        var card = UiTheme.Section();
        var cardBox = new VBoxContainer();
        cardBox.AddThemeConstantOverride("separation", 6);
        card.AddChild(cardBox);

        var titleHeader = UiTheme.Text("Slave Merchant System", 14, UiTheme.GoldBright, HorizontalAlignment.Center);
        cardBox.AddChild(titleHeader);

        tipLabel = UiTheme.Text(
            "Deploy a slave bot to sell items automatically in the market.\n" +
            "Lasts for 1 hour per Voucher. Once active, you are free to explore or log out.",
            11,
            UiTheme.TextLo,
            HorizontalAlignment.Center);
        tipLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        cardBox.AddChild(tipLabel);

        statusLabel = UiTheme.Text("Status: Ready to Open Slave Stall", 12, UiTheme.Good, HorizontalAlignment.Center);
        cardBox.AddChild(statusLabel);

        body.AddChild(card);

        // Stall items section (bar merchant yang sedang jualan)
        var stallCard = UiTheme.Section();
        var stallBox = new VBoxContainer();
        stallBox.AddThemeConstantOverride("separation", 6);
        stallCard.AddChild(stallBox);

        stallHeaderLabel = UiTheme.Text("Stall Items on Sale", 12, UiTheme.Gold, HorizontalAlignment.Center);
        stallBox.AddChild(stallHeaderLabel);

        stallCells = MerchantUi.BuildGrid(context.Tooltips, stallBox, MerchantUi.StallSlots, MerchantUi.StallColumns, null);

        body.AddChild(stallCard);

        // Actions container
        var actionsBox = new VBoxContainer();
        actionsBox.AddThemeConstantOverride("separation", 8);

        var startButton = UiTheme.ActionButton("Open Slave Stall (Start Selling)", "Deploy a 1-hour slave merchant stall using 1x Voucher.");
        startButton.CustomMinimumSize = new Vector2(0, 32);
        startButton.Pressed += OnStartStall;
        actionsBox.AddChild(startButton);

        var buttonRow = new HBoxContainer();
        buttonRow.AddThemeConstantOverride("separation", 8);

        var claimButton = UiTheme.SmallButton("Claim Coins", "Collect all Noah earnings from your slave merchant.");
        claimButton.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        claimButton.CustomMinimumSize = new Vector2(0, 28);
        claimButton.Pressed += OnClaimNoah;
        buttonRow.AddChild(claimButton);

        var closeButton = UiTheme.SmallButton("Close Shop", "Close your slave merchant and retrieve unsold items and coins.");
        closeButton.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        closeButton.CustomMinimumSize = new Vector2(0, 28);
        closeButton.Pressed += OnCloseSlave;
        buttonRow.AddChild(closeButton);

        actionsBox.AddChild(buttonRow);

        var refreshButton = UiTheme.SmallButton("Check Status", "Refresh status and inspect items in your slave stall.");
        refreshButton.CustomMinimumSize = new Vector2(0, 28);
        refreshButton.Pressed += OnRefreshStatus;
        actionsBox.AddChild(refreshButton);

        body.AddChild(actionsBox);
    }

    public void Open()
    {
        UiKit.ShowOnTop(Window);
        OnRefreshStatus();
    }

    public void Close()
    {
        Window.Visible = false;
    }

    public void Toggle()
    {
        if (Window.Visible) Close();
        else Open();
    }

    private void OnStartStall()
    {
        if (context.Self.Dead)
        {
            context.Log.Notice("You cannot open a stall while dead.", UiTheme.Bad);
            return;
        }

        if (!HasVoucher())
        {
            context.Log.Notice("You need 1x Voucher of Offline Merchant to open a Slave Merchant.", UiTheme.Bad);
            return;
        }

        // Enable slave mode on server
        chatRequests.Send("+slave");

        // Close HUD and open the merchant stall window
        Close();
        world.Merchant.OpenSellStall();
    }

    private bool HasVoucher()
    {
        for (int i = Inventory.GridStart; i < Inventory.GridStart + Inventory.GridCount; i++)
        {
            var item = self.Inventory[i];
            if (!item.IsEmpty && IsSlaveMerchantItem(item.ItemId) && item.Count > 0)
                return true;
        }
        return false;
    }

    private void OnClaimNoah()
    {
        chatRequests.Send("+slaveclaim");
    }

    private void OnCloseSlave()
    {
        ClearStallDisplay();
        chatRequests.Send("+slaveclose");
    }

    private void OnRefreshStatus()
    {
        chatRequests.Send("+slavestatus");
    }

    private void ClearStallDisplay()
    {
        if (stallCells != null)
        {
            for (int i = 0; i < stallCells.Length; i++)
            {
                stallCells[i].Set(default, "");
            }
        }
        if (statusLabel != null)
        {
            statusLabel.Text = "Status: Ready to Open Slave Stall";
            statusLabel.Modulate = UiTheme.Good;
        }
        if (stallHeaderLabel != null)
        {
            stallHeaderLabel.Text = "Stall Items on Sale";
        }
    }

    private void OnStallClosed(int charId)
    {
        ClearStallDisplay();
    }

    private void OnMerchantList(int merchantId, MerchantStallItem[] items)
    {
        int listed = 0;
        for (int i = 0; i < stallCells.Length && i < items.Length; i++)
        {
            var item = items[i];
            stallCells[i].Set(MerchantUi.StallSlot(item), MerchantUi.PriceNote("Price", item.Price, item.Count));
            if (!item.IsEmpty) listed++;
        }

        if (listed > 0)
        {
            statusLabel.Text = $"Status: Active | {listed} item(s) on sale";
            statusLabel.Modulate = UiTheme.Good;
            stallHeaderLabel.Text = $"Stall Items on Sale ({listed} listed)";
        }
        else
        {
            ClearStallDisplay();
        }
    }
}
