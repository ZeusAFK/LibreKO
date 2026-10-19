using Godot;

namespace LibreKO.Features.Windows;

internal sealed partial class PieceChangeFeature : IFeature, ILateTickFeature, IClosableWindow
{
    private readonly IWorldContext context;
    private readonly ItemsSession items;
    private readonly PieceChangeSession pieceChange;
    private readonly SelfSession self;
    private readonly IPieceChangeRequests requests;
    private readonly VendorFeature vendor;
    private readonly UpgradeFeature upgrade;
    private readonly IInteractionDialogs interactionDialogs;

    public PieceChangeFeature(IWorldContext context, ItemsSession items, PieceChangeSession pieceChange, SelfSession self, IPieceChangeRequests requests, VendorFeature vendor, UpgradeFeature upgrade, IInteractionDialogs interactionDialogs)
    {
        this.context = context;
        this.items = items;
        this.pieceChange = pieceChange;
        this.self = self;
        this.requests = requests;
        this.vendor = vendor;
        this.upgrade = upgrade;
        this.interactionDialogs = interactionDialogs;
    }

    public void LateTick(double delta, double nowSec)
    {
        PieceChangeTick(delta);
    }

    private const int ResultSlotCount = 3;
    private const double SpinInterval = 0.05;
    private const int TextPlace = 6710;
    private const int TextPressStop = 6712;
    private const int TextLucky = 6713;
    private const int TextNotBad = 6714;
    private const int TextBetterDays = 6715;
    private const int TextFailed = 6716;
    private const int HelpChaoticGenerator = 3;

    private CanvasLayer layer = null!;
    private HudWindow panel = null!;
    private BagCompanion? companion;
    private Label message = null!;
    private Label subMessage = null!;
    private Button startBtn = null!;
    private Button stopBtn = null!;
    private Button talkBtn = null!;
    private UpgradeSocket socket = null!;
    private readonly UpgradeSocket[] resultSockets = new UpgradeSocket[ResultSlotCount];
    private bool shown;

    public bool IsOpen => shown;
    private bool spinning;
    private bool busy;
    private int npcId;
    private int itemId;
    private int position = -1;
    private double spinClock;
    private IReadOnlyList<int> rewards = System.Array.Empty<int>();

    public void Build()
    {
        interactionDialogs.InteractionCloses(this);
        BuildPanel();
        pieceChange.PieceChangeOpenEvent += OnOpen;
        pieceChange.PieceExchangeResultEvent += OnExchangeResult;
        items.InventorySlotEvent += OnInventorySlot;
        items.InventoryGridRefreshEvent += OnInventoryGrid;
    }

    public void Dispose()
    {
        pieceChange.PieceChangeOpenEvent -= OnOpen;
        pieceChange.PieceExchangeResultEvent -= OnExchangeResult;
        items.InventorySlotEvent -= OnInventorySlot;
        items.InventoryGridRefreshEvent -= OnInventoryGrid;
    }

    private void BuildPanel()
    {
        layer = new CanvasLayer { Layer = UiLayers.ServiceWindows };
        context.Root.AddChild(layer);

        panel = new HudWindow("piecechange", "Chaotic Generator", bodyMinWidth: 640) { Visible = false };
        panel.Closed += Close;
        layer.AddChild(panel);

        var root = panel.Body;
        root.AddThemeConstantOverride("separation", 10);

        var cols = new HBoxContainer();
        cols.AddThemeConstantOverride("separation", 14);
        root.AddChild(cols);

        var altarPanel = UiTheme.Section();
        altarPanel.CustomMinimumSize = new Vector2(340, 0);
        cols.AddChild(altarPanel);

        var altar = new VBoxContainer();
        altar.AddThemeConstantOverride("separation", 10);
        altarPanel.AddChild(altar);
        altar.AddChild(BuildBench());

        message = UiTheme.Text(ItemData.Text(TextPlace, "Place one of the pieces."),
            14, UiTheme.TextHi, HorizontalAlignment.Center);
        message.CustomMinimumSize = new Vector2(0, 30);
        message.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        altar.AddChild(message);

        subMessage = UiTheme.Text("", 12, UiTheme.TextLo, HorizontalAlignment.Center);
        subMessage.CustomMinimumSize = new Vector2(0, 22);
        subMessage.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        altar.AddChild(subMessage);

        var buttons = new HBoxContainer();
        buttons.AddThemeConstantOverride("separation", 8);
        altar.AddChild(buttons);

        startBtn = Button("Start", StartSpin);
        buttons.AddChild(startBtn);
        stopBtn = Button("Stop", StopSpin);
        buttons.AddChild(stopBtn);
        talkBtn = Button("Talk", TalkToGenerator);
        buttons.AddChild(talkBtn);

        ClearBench();
    }

    internal static Button Button(string text, System.Action pressed)
    {
        var button = new Button
        {
            Text = text,
            FocusMode = Control.FocusModeEnum.None,
            CustomMinimumSize = new Vector2(0, 34),
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        button.Pressed += pressed;
        return button;
    }

    private Control BuildBench()
    {
        var bench = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        bench.AddThemeConstantOverride("separation", 16);

        socket = new UpgradeSocket("", 64);
        socket.Cleared += ClearBench;
        socket.Hovered += held => context.Tooltips.Show(SocketSlot(), held);
        socket.Unhovered += context.Tooltips.Hide;
        bench.AddChild(UiKit.UpgradeColumn("Piece", socket));

        bench.AddChild(UiTheme.Text("=", 26, UiTheme.GoldDark));

        var wheel = new HBoxContainer();
        wheel.AddThemeConstantOverride("separation", 6);
        for (int i = 0; i < resultSockets.Length; i++)
        {
            var socket = new UpgradeSocket("?", 56, interactive: false);
            socket.Hovered += held => context.Tooltips.Show(-1, held);
            socket.Unhovered += context.Tooltips.Hide;
            resultSockets[i] = socket;
            wheel.AddChild(socket);
        }
        bench.AddChild(UiKit.UpgradeColumn("Reward", wheel));
        return bench;
    }

    private void OnOpen(int npcId)
    {
        this.npcId = npcId;
        busy = false;
        ClearBench();
        RefreshBackpack();
        panel.Visible = true;
        shown = true;
        context.NpcDialog.Close();
        vendor.Close();
        companion ??= new BagCompanion(TakeFromBag, PieceBagFit, _ => "", Close);
        context.BagCompanions.Attach(companion);
    }

    public void Close()
    {
        if (!shown) return;
        shown = false;
        spinning = false;
        busy = false;
        panel.Visible = false;
        context.Tooltips.Hide();
        if (companion != null) context.BagCompanions.Detach(companion);
    }

    private void TalkToGenerator()
    {
        if (busy || spinning) return;
        var (title, body) = ItemData.UiHelp(HelpChaoticGenerator);
        if (body.Length == 0) return;
        Close();
        Notice.Show(context.Root, body.Replace('|', '\n'), title.Length > 0 ? title : "Chaotic Generator");
    }

    private void ClearBench()
    {
        itemId = 0;
        position = -1;
        spinning = false;
        rewards = System.Array.Empty<int>();
        socket?.Clear();
        foreach (var socket in resultSockets) socket?.Clear();
        SetMessage(ItemData.Text(TextPlace, "Place one of the pieces."), false);
        subMessage.Text = "";
        RefreshActions();
    }

    private void Place(int absSlot)
    {
        if (busy || spinning) return;
        if (absSlot < Inventory.GridStart || absSlot >= self.Inventory.Length || self.Inventory[absSlot].IsEmpty) return;

        int rel = absSlot - Inventory.GridStart;
        if (position == rel)
        {
            ClearBench();
            RefreshBackpack();
            return;
        }

        var item = self.Inventory[absSlot];
        if (!ItemData.IsExchangePiece(item.ItemId))
        {
            SetMessage(ItemData.Text(TextPlace, "Place one of the pieces."), true);
            subMessage.Text = $"{ItemData.DisplayName(item.ItemId)} is not a generator piece.";
            return;
        }

        itemId = item.ItemId;
        position = rel;
        rewards = ItemData.PieceRewards(item.ItemId);
        socket.Set(item.ItemId, item.Count, item.Durability);
        var blocked = BlockReason();
        SetMessage(blocked ?? ItemData.Text(GameText.PressStart, "Press Start."), blocked != null);
        subMessage.Text = $"{rewards.Count} possible rewards.";
        RefreshBackpack();
        RefreshActions();
    }

    private void StartSpin()
    {
        if (!CanStartSpin()) return;
        if (BlockReason() is { } blocked)
        {
            SetMessage(blocked, true);
            return;
        }
        spinning = true;
        spinClock = 0;
        SetMessage(ItemData.Text(TextPressStop, "Press Stop."), false);
        RefreshActions();
    }

    private void StopSpin()
    {
        if (!spinning || busy) return;
        if (BlockReason() is { } blocked)
        {
            spinning = false;
            SetMessage(blocked, true);
            RefreshActions();
            return;
        }
        spinning = false;
        busy = true;
        RefreshActions();
        requests.PieceExchange(npcId, itemId, position);
    }

    private void PieceChangeTick(double delta)
    {
        if (!shown || !spinning || rewards.Count == 0) return;

        spinClock += delta;
        if (spinClock < SpinInterval) return;
        spinClock = 0;

        int slot = GD.RandRange(0, ResultSlotCount - 1);
        int reward = rewards[GD.RandRange(0, rewards.Count - 1)];
        resultSockets[slot].Set(reward, 1, 0);
    }

    private void OnExchangeResult(PieceExchangeResult result)
    {
        if (!shown) return;
        busy = false;
        spinning = false;

        if (result.ResultCode == BifrostResult.Succeeded && result.RewardItemId != 0)
        {
            foreach (var socket in resultSockets)
                socket.Set(result.RewardItemId, 1, 0);

            SetMessage(EffectText((BifrostRarity)result.Effect), false);
            subMessage.Text = ItemData.DisplayName(result.RewardItemId);
            context.Log.Notice($"Chaotic Generator: {ItemData.DisplayName(result.RewardItemId)}");
            itemId = 0;
            position = -1;
            rewards = System.Array.Empty<int>();
            socket.Clear();
        }
        else
        {
            foreach (var socket in resultSockets) socket.Clear();
            SetMessage(result.ResultCode == BifrostResult.NoPiece
                ? ItemData.Text(TextPlace, "Place one of the pieces.")
                : ItemData.Text(TextFailed, "The divination has failed."), true);
            subMessage.Text = "";
        }

        RefreshBackpack();
        RefreshActions();
        if (context.MainWindows.CharTabOpen()) context.Inventory.Refresh();
    }

    private static string EffectText(BifrostRarity effect) => effect switch
    {
        BifrostRarity.White => ItemData.Text(TextLucky, "It must be your lucky day."),
        BifrostRarity.Green => ItemData.Text(TextNotBad, "Your luck isn't that bad."),
        _ => ItemData.Text(TextBetterDays, "There will be better days."),
    };

    private void RefreshBackpack() => context.BagCompanions.RefreshFit();

    private bool TakeFromBag(int abs)
    {
        if (!self.Inventory.InMainBag(abs)) return false;
        Place(abs);
        return true;
    }

    private BagFit PieceBagFit(int abs) =>
        abs - Inventory.GridStart == position ? BagFit.Staged
        : ItemData.IsExchangePiece(self.Inventory[abs].ItemId) ? BagFit.Normal
        : BagFit.Unfit;

    private void SetMessage(string text, bool warn)
    {
        message.Text = text;
        message.AddThemeColorOverride("font_color", warn ? UiTheme.Bad : UiTheme.TextHi);
    }

    private string? BlockReason()
    {
        if (self.Sheet.MaxWeight > 0 && context.Inventory.CarriedWeight() >= self.Sheet.MaxWeight)
            return "You are carrying too much.";

        for (int abs = Inventory.GridStart; abs < Inventory.GridStart + Inventory.GridCount && abs < self.Inventory.Length; abs++)
            if (self.Inventory[abs].IsEmpty) return null;

        return "Your bag is full.";
    }

    private bool CanStartSpin()
    => shown && !busy && !spinning && npcId != 0
       && itemId != 0 && rewards.Count > 0;

    private void RefreshActions()
    {
        if (startBtn == null) return;
        startBtn.Disabled = !CanStartSpin();
        stopBtn.Disabled = !spinning || busy;
        talkBtn.Disabled = busy || spinning;
    }

    private int SocketSlot() => position >= 0 ? Inventory.GridStart + position : -1;

    private void OnInventorySlot(int absSlot, ItemSlot item)
    {
        if (!shown) return;
        if (absSlot < Inventory.GridStart || absSlot >= Inventory.GridStart + Inventory.GridCount) return;

        if (position == absSlot - Inventory.GridStart && (item.IsEmpty || item.ItemId != itemId))
        {
            itemId = 0;
            position = -1;
            spinning = false;
            rewards = System.Array.Empty<int>();
            socket?.Clear();
            RefreshActions();
        }

        RefreshBackpack();
    }

    private void OnInventoryGrid(ItemSlot[] itemSlots)
    {
        if (!shown) return;
        RefreshBackpack();
    }

    public string Report()
    {
        var parts = new List<string> { $"shown={shown}", $"npc={npcId}",
            $"piece={itemId}@{position}", $"rewards={rewards.Count}",
            $"spinning={spinning}", $"busy={busy}" };
        return string.Join(" ", parts);
    }

    public bool StageExchange(int pieceItemId)
    {
        if (!shown) return false;
        int slot = upgrade.FindBackpackSlot(pieceItemId);
        if (slot < 0) return false;
        Place(slot);
        return itemId == pieceItemId;
    }

    public void TalkForTest() => TalkToGenerator();

    public void StartSpinForTest() => StartSpin();

    public void StopSpinForTest() => StopSpin();

    private sealed partial class UpgradeBackpackCell : PanelContainer
    {
        public event System.Action? Pressed;
        public event System.Action<int, ItemSlot>? Hovered;
        public event System.Action? Unhovered;
        private readonly ItemSlot item;

        public UpgradeBackpackCell(int absSlot, ItemSlot item, bool staged)
        {
            this.item = item;
            MouseEntered += () => { if (!this.item.IsEmpty) Hovered?.Invoke(absSlot, this.item); };
            MouseExited += () => Unhovered?.Invoke();
            CustomMinimumSize = new Vector2(48, 48);
            AddThemeStyleboxOverride("panel",
                item.IsEmpty ? UiTheme.Slot() : UiTheme.Slot(staged ? UiTheme.Gold : UiTheme.Edge));

            if (item.IsEmpty)
                return;

            var icon = new TextureRect
            {
                Texture = ItemIcons.Get(item.ItemId),
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                MouseFilter = MouseFilterEnum.Ignore,
                Modulate = staged ? new Color(1f, 1f, 1f, 0.4f) : Colors.White,
            };
            icon.SetAnchorsPreset(LayoutPreset.FullRect);
            AddChild(icon);
            if (UpgradeBadge.Show(this, item.ItemId) is { } badge) badge.Modulate = icon.Modulate;

            if (item.Count > 1)
            {
                var count = HudStyle.Label(11, HorizontalAlignment.Right);
                count.Text = item.Count.ToString();
                count.SetAnchorsPreset(LayoutPreset.BottomRight);
                count.MouseFilter = MouseFilterEnum.Ignore;
                AddChild(count);
            }
        }

        public override void _GuiInput(InputEvent ev)
        {
            if (!item.IsEmpty && ev is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left })
                Pressed?.Invoke();
        }
    }
}
