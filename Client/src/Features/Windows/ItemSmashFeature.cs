using Godot;

namespace LibreKO.Features.Windows;

internal sealed class ItemSmashFeature : IFeature, ILateTickFeature
{
    private readonly IWorldContext context;
    private readonly ItemSmashSession itemSmash;
    private readonly SelfSession self;
    private readonly IItemSmashRequests requests;
    private readonly VendorFeature vendor;

    public ItemSmashFeature(IWorldContext context, ItemSmashSession itemSmash, SelfSession self, IItemSmashRequests requests, VendorFeature vendor)
    {
        this.context = context;
        this.itemSmash = itemSmash;
        this.self = self;
        this.requests = requests;
        this.vendor = vendor;
    }

    public void LateTick(double delta, double nowSec)
    {
        ItemTick(delta);
    }

    private const string File = "res://assets/items/item_smash.json";
    private const string ItemTitle = "Disassemble";
    private const double SpinInterval = 0.05;
    private const int GridColumns = 3;

    private CanvasLayer layer = null!;
    internal HudWindow Panel { get; private set; } = null!;
    private BagCompanion? companion;
    private Label message = null!;
    private Label subMessage = null!;
    private Button startBtn = null!;
    private Button stopBtn = null!;
    private Button cancelBtn = null!;
    private UpgradeSocket socket = null!;
    private readonly UpgradeSocket[] results = new UpgradeSocket[ItemSmash.ResultSlots];
    private SmashTable? table;
    private bool shown;

    internal bool IsOpen => shown;

    private bool spinning;
    private bool busy;
    private int npcTemplate;
    private int npcId = ItemCombine.NoCraftsman;
    private int slot = -1;
    private double spinClock;
    private IReadOnlyList<int> pool = System.Array.Empty<int>();

    public void Build()
    {
        BuildPanel();
        itemSmash.ItemSmashReplyEvent += OnReply;
    }

    public void Dispose()
    {
        itemSmash.ItemSmashReplyEvent -= OnReply;
    }

    private SmashTable Data => table ??= LoadTable();

    private static SmashTable LoadTable()
    {
        using var file = Godot.FileAccess.Open(File, Godot.FileAccess.ModeFlags.Read);
        if (file == null) return SmashTable.Empty;
        var root = Json.ParseString(file.GetAsText()).AsGodotDictionary();
        var classes = new Dictionary<int, SmashClass>();
        foreach (var (grade, row) in root["classes"].AsGodotDictionary())
        {
            var entry = row.AsGodotDictionary();
            classes[int.Parse(grade.AsString())] = new SmashClass(entry["band"].AsInt32(), entry["rolls"].AsInt32());
        }
        var bands = new Dictionary<int, IReadOnlyList<int>>();
        foreach (var (band, items) in root["bands"].AsGodotDictionary())
            bands[int.Parse(band.AsString())] = items.AsGodotArray().Select(item => item.AsInt32()).ToList();
        return new SmashTable(classes, bands);
    }

    internal void BuildPanel()
    {
        layer = new CanvasLayer { Layer = UiLayers.ServiceWindows };
        context.Root.AddChild(layer);

        Panel = new HudWindow("itemsmash", ItemTitle, bodyMinWidth: 460) { Visible = false };
        Panel.Closed += Close;
        layer.AddChild(Panel);

        var section = UiTheme.Section();
        Panel.Body.AddChild(section);
        var altar = new VBoxContainer();
        altar.AddThemeConstantOverride("separation", 10);
        section.AddChild(altar);

        var bench = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        bench.AddThemeConstantOverride("separation", 16);
        altar.AddChild(bench);

        socket = new UpgradeSocket("", 64);
        socket.Cleared += Cancel;
        socket.Hovered += held => context.Tooltips.Show(slot, held);
        socket.Unhovered += context.Tooltips.Hide;
        bench.AddChild(UiKit.UpgradeColumn("Item", socket));
        bench.AddChild(UiTheme.Text("=", 26, UiTheme.GoldDark));

        var grid = new GridContainer { Columns = GridColumns };
        grid.AddThemeConstantOverride("h_separation", 6);
        grid.AddThemeConstantOverride("v_separation", 6);
        for (int i = 0; i < results.Length; i++)
        {
            var socket = new UpgradeSocket("", 48, interactive: false);
            socket.Hovered += held => context.Tooltips.Show(-1, held);
            socket.Unhovered += context.Tooltips.Hide;
            results[i] = socket;
            grid.AddChild(socket);
        }
        bench.AddChild(UiKit.UpgradeColumn("Pieces", grid));

        message = UiTheme.Text("", 14, UiTheme.TextHi, HorizontalAlignment.Center);
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
        startBtn = PieceChangeFeature.Button("Start", Start);
        buttons.AddChild(startBtn);
        stopBtn = PieceChangeFeature.Button("Stop", Stop);
        buttons.AddChild(stopBtn);
        cancelBtn = PieceChangeFeature.Button("Cancel", Cancel);
        buttons.AddChild(cancelBtn);

        ClearBench();
    }

    internal void Open(int npcTemplate, int talkedTo)
    {
        this.npcTemplate = npcTemplate;
        npcId = talkedTo >= 0 ? talkedTo : Npc();
        busy = false;
        vendor.Close();
        ClearBench();
        foreach (var socket in results) socket.Clear();
        Show();
        companion ??= new BagCompanion(TakeFromBag, SmashBagFit, _ => "", Close);
        context.BagCompanions.Attach(companion);
    }

    private int Npc() => context.Self.Body == null
    ? ItemCombine.NoCraftsman
    : ItemCombine.Craftsman(
        context.Entities.All().Where(e => !e.Dead).Select(e => (e.Id, e.NpcId, Coord.FlatDistance(context.Self.Body.Position, e.Position))),
        npcTemplate);

    private void Close()
    {
        if (!shown) return;
        shown = false;
        spinning = false;
        busy = false;
        Panel.Visible = false;
        context.Tooltips.Hide();
        if (companion != null) context.BagCompanions.Detach(companion);
    }

    private void ClearBench()
    {
        slot = -1;
        spinning = false;
        pool = System.Array.Empty<int>();
        socket?.Clear();
        SetMessage(ItemData.Text(ItemSmash.PutItemText, "Please put an item to disassemble."), false);
        subMessage.Text = "";
        RefreshActions();
        context.BagCompanions.RefreshFit();
    }

    private bool TakeFromBag(int abs)
    {
        if (!self.Inventory.InMainBag(abs)) return false;
        Place(abs);
        return true;
    }

    private BagFit SmashBagFit(int abs) =>
        abs == slot ? BagFit.Staged
        : CheckOf(self.Inventory[abs]) == SmashCheck.Allowed ? BagFit.Normal
        : BagFit.Unfit;

    private static SmashCheck CheckOf(ItemSlot itemSlot)
    {
        if (itemSlot.IsEmpty || ItemData.Get(itemSlot.ItemId) is not { } def) return SmashCheck.NotGear;
        return ItemSmash.Check(itemSlot.ItemId, def.Kind, def.Race, ItemData.GradeClass(def, ItemData.ExtFor(itemSlot.ItemId)), itemSlot.State);
    }

    internal void Show()
    {
        Panel.Visible = true;
        shown = true;
    }

    internal void Place(int abs)
    {
        if (busy || spinning || abs >= self.Inventory.Length || self.Inventory[abs].IsEmpty) return;
        if (abs == slot)
        {
            ClearBench();
            return;
        }
        var check = CheckOf(self.Inventory[abs]);
        if (check == SmashCheck.Ignored) return;
        if (ItemSmash.CheckText(check) is { } refusal)
        {
            SetMessage(ItemData.Text(refusal, ""), true);
            return;
        }
        var itemSlot = self.Inventory[abs];
        slot = abs;
        socket.Set(itemSlot.ItemId, itemSlot.Count, itemSlot.Durability);
        foreach (var socket in results) socket.Clear();
        SetMessage(ItemData.Text(GameText.PressStart, "Press Start."), false);
        subMessage.Text = ItemData.DisplayName(itemSlot.ItemId);
        RefreshActions();
        context.BagCompanions.RefreshFit();
    }

    private void Start()
    {
        if (!CanStart()) return;
        SetMessage(ItemData.Text(ItemSmash.OkStartsText, "When you press the OK button, disassembly begins."), false);
        Notice.Confirm(context.Root,
            ItemData.Text(ItemSmash.WarningText, "Once you disassemble an item, it cannot be restored nor repurchased."),
            "OK", "Cancel", BeginSpin, title: ItemData.Text(ItemSmash.WarningTitleText, ItemTitle));
    }

    private void BeginSpin()
    {
        if (!CanStart() || ItemData.Get(self.Inventory[slot].ItemId) is not { } def) return;
        if (Data.ClassOf(ItemData.GradeClass(def, ItemData.ExtFor(self.Inventory[slot].ItemId))) is not { } smashClass)
        {
            ClearBench();
            return;
        }
        pool = Data.RewardsOf(smashClass.Band);
        spinning = true;
        spinClock = 0;
        SetMessage(ItemData.Text(ItemSmash.PressStopText, "Press Stop."), false);
        Audio.PlayUi(ItemSmash.SpinSound);
        RefreshActions();
    }

    private void Stop()
    {
        if (!spinning || busy || slot < 0) return;
        spinning = false;
        busy = true;
        RefreshActions();
        requests.Send(self.Inventory[slot].ItemId, slot - Inventory.GridStart, npcId);
    }

    private void Cancel()
    {
        if (busy) return;
        ClearBench();
        foreach (var socket in results) socket.Clear();
    }

    private void ItemTick(double delta)
    {
        if (!shown || !spinning || pool.Count == 0) return;
        spinClock += delta;
        if (spinClock < SpinInterval) return;
        spinClock = 0;
        int slot = GD.RandRange(0, results.Length - 1);
        results[slot].Set(pool[GD.RandRange(0, pool.Count - 1)], 1, 0);
    }

    internal void OnReply(ItemSmashReply reply)
    {
        if (!shown) return;
        busy = false;
        spinning = false;
        foreach (var socket in results) socket.Clear();

        if (reply.Result != ItemSmash.Succeeded)
        {
            string text = ItemSmash.ReplyText(reply.Result) is { } textId ? ItemData.Text(textId, "") : "";
            ClearBench();
            if (reply.Result == ItemSmash.WrongNpc)
            {
                if (text.Length > 0) context.Log.Add(text, CombatLogKind.Status, UiKit.WhRefusalTint);
                Close();
                return;
            }
            if (text.Length > 0) SetMessage(text, true);
            return;
        }

        int abs = Inventory.GridStart + reply.BagSlot;
        if (abs < self.Inventory.Length && self.Inventory[abs].ItemId == reply.ItemId)
            self.SetInventorySlot(abs, default);
        ClearBench();
        for (int i = 0; i < reply.Pieces.Count && i < results.Length; i++)
            results[i].Set(reply.Pieces[i].ItemId, (short)reply.Pieces[i].Count, 0);
        subMessage.Text = string.Join(", ", reply.Pieces.Select(piece => ItemData.DisplayName(piece.ItemId)));
        if (context.MainWindows.CharTabOpen()) context.Inventory.Refresh();
    }

    private void SetMessage(string text, bool warn)
    {
        message.Text = text;
        message.AddThemeColorOverride("font_color", warn ? UiTheme.Bad : UiTheme.TextHi);
    }

    private bool CanStart() =>
        shown && !busy && !spinning && slot >= 0 && slot < self.Inventory.Length && !self.Inventory[slot].IsEmpty;

    private void RefreshActions()
    {
        if (startBtn == null) return;
        startBtn.Disabled = !CanStart();
        stopBtn.Disabled = !spinning || busy;
        cancelBtn.Disabled = busy;
    }
}
