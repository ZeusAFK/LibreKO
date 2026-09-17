using System.Collections.Generic;
using Godot;
using LibreKO.Domain;
using LibreKO.Network;

namespace LibreKO;

public partial class World
{
    private const int PieceResultSlotCount = 3;
    private const double PieceSpinInterval = 0.05;

    private const int TextPlacePiece = 6710;
    private const int TextPressStart = 6711;
    private const int TextPressStop = 6712;
    private const int TextLucky = 6713;
    private const int TextNotBad = 6714;
    private const int TextBetterDays = 6715;
    private const int TextFailed = 6716;
    private const int HelpChaoticGenerator = 3;

    private CanvasLayer _pieceLayer = null!;
    private HudWindow _piecePanel = null!;
    private VBoxContainer _pieceBackpackGrid = null!;
    private Label _pieceMessage = null!;
    private Label _pieceSubMessage = null!;
    private Button _pieceStartBtn = null!;
    private Button _pieceStopBtn = null!;
    private Button _pieceTalkBtn = null!;
    private UpgradeSocket _pieceSocket = null!;

    private readonly UpgradeSocket[] _pieceResultSockets = new UpgradeSocket[PieceResultSlotCount];
    private readonly List<UpgradeBackpackCell> _pieceBackpackCells = new();

    private bool _pieceShown;
    private bool _pieceSpinning;
    private bool _pieceBusy;
    private int _pieceNpcId;
    private int _pieceItemId;
    private int _piecePosition = -1;
    private double _pieceSpinClock;
    private IReadOnlyList<int> _pieceRewards = System.Array.Empty<int>();

    private void PieceChangeInit()
    {
        BuildPiecePanel();
        Net.I.PieceChangeOpenEvent += OnPieceChangeOpen;
        Net.I.PieceExchangeResultEvent += OnPieceExchangeResult;
        Net.I.InventorySlotEvent += OnPieceInventorySlot;
        Net.I.InventoryGridRefreshEvent += OnPieceInventoryGrid;
    }

    private void PieceChangeDispose()
    {
        Net.I.PieceChangeOpenEvent -= OnPieceChangeOpen;
        Net.I.PieceExchangeResultEvent -= OnPieceExchangeResult;
        Net.I.InventorySlotEvent -= OnPieceInventorySlot;
        Net.I.InventoryGridRefreshEvent -= OnPieceInventoryGrid;
    }

    private void BuildPiecePanel()
    {
        _pieceLayer = new CanvasLayer { Layer = 75 };
        AddChild(_pieceLayer);

        _piecePanel = new HudWindow("piecechange", "Chaotic Generator", new Vector2(150, 120), 640) { Visible = false };
        _piecePanel.Closed += ClosePieceChange;
        _pieceLayer.AddChild(_piecePanel);

        var root = _piecePanel.Body;
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
        altar.AddChild(BuildPieceBench());

        _pieceMessage = UiTheme.Text(ItemData.Text(TextPlacePiece, "Place one of the pieces."),
            14, UiTheme.TextHi, HorizontalAlignment.Center);
        _pieceMessage.CustomMinimumSize = new Vector2(0, 30);
        _pieceMessage.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        altar.AddChild(_pieceMessage);

        _pieceSubMessage = UiTheme.Text("", 12, UiTheme.TextLo, HorizontalAlignment.Center);
        _pieceSubMessage.CustomMinimumSize = new Vector2(0, 22);
        _pieceSubMessage.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        altar.AddChild(_pieceSubMessage);

        var buttons = new HBoxContainer();
        buttons.AddThemeConstantOverride("separation", 8);
        altar.AddChild(buttons);

        _pieceStartBtn = PieceButton("Start", StartPieceSpin);
        buttons.AddChild(_pieceStartBtn);
        _pieceStopBtn = PieceButton("Stop", StopPieceSpin);
        buttons.AddChild(_pieceStopBtn);
        _pieceTalkBtn = PieceButton("Talk", TalkToGenerator);
        buttons.AddChild(_pieceTalkBtn);

        var bagPanel = UiTheme.Section();
        bagPanel.CustomMinimumSize = new Vector2(260, 0);
        cols.AddChild(bagPanel);
        var bag = new VBoxContainer();
        bag.AddThemeConstantOverride("separation", 7);
        bagPanel.AddChild(bag);
        bag.AddChild(UiTheme.SectionTitle("Inventory"));
        _pieceBackpackGrid = new VBoxContainer();
        _pieceBackpackGrid.AddThemeConstantOverride("separation", 4);
        bag.AddChild(_pieceBackpackGrid);

        ClearPieceBench();
        RefreshPieceBackpack();
    }

    private static Button PieceButton(string text, System.Action pressed)
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

    private Control BuildPieceBench()
    {
        var bench = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        bench.AddThemeConstantOverride("separation", 16);

        _pieceSocket = new UpgradeSocket("", 64);
        _pieceSocket.Cleared += ClearPieceBench;
        _pieceSocket.Hovered += held => ShowItemTooltip(PieceSocketSlot(), held);
        _pieceSocket.Unhovered += HideItemTooltip;
        bench.AddChild(BuildUpgradeColumn("Piece", _pieceSocket));

        bench.AddChild(UiTheme.Text("=", 26, UiTheme.GoldDark));

        var wheel = new HBoxContainer();
        wheel.AddThemeConstantOverride("separation", 6);
        for (int i = 0; i < _pieceResultSockets.Length; i++)
        {
            var socket = new UpgradeSocket("?", 56, interactive: false);
            socket.Hovered += held => ShowItemTooltip(-1, held);
            socket.Unhovered += HideItemTooltip;
            _pieceResultSockets[i] = socket;
            wheel.AddChild(socket);
        }
        bench.AddChild(BuildUpgradeColumn("Reward", wheel));
        return bench;
    }

    private void OnPieceChangeOpen(int npcId)
    {
        _pieceNpcId = npcId;
        _pieceBusy = false;
        ClearPieceBench();
        RefreshPieceBackpack();
        _piecePanel.Visible = true;
        _pieceShown = true;
        CloseNpcDialog();
        CloseVendor();
    }

    private void ClosePieceChange()
    {
        if (!_pieceShown) return;
        _pieceShown = false;
        _pieceSpinning = false;
        _pieceBusy = false;
        _piecePanel.Visible = false;
        HideItemTooltip();
    }

    private void TalkToGenerator()
    {
        if (_pieceBusy || _pieceSpinning) return;
        var (title, body) = ItemData.UiHelp(HelpChaoticGenerator);
        if (body.Length == 0) return;
        ClosePieceChange();
        Notice.Show(this, body.Replace('|', '\n'), title.Length > 0 ? title : "Chaotic Generator");
    }

    private void ClearPieceBench()
    {
        _pieceItemId = 0;
        _piecePosition = -1;
        _pieceSpinning = false;
        _pieceRewards = System.Array.Empty<int>();
        _pieceSocket?.Clear();
        foreach (var socket in _pieceResultSockets) socket?.Clear();
        SetPieceMessage(ItemData.Text(TextPlacePiece, "Place one of the pieces."), false);
        _pieceSubMessage.Text = "";
        RefreshPieceActions();
    }

    private void PlacePiece(int absSlot)
    {
        if (_pieceBusy || _pieceSpinning) return;
        if (absSlot < GridStart || absSlot >= Inv.Length || Inv[absSlot].IsEmpty) return;

        int rel = absSlot - GridStart;
        if (_piecePosition == rel)
        {
            ClearPieceBench();
            RefreshPieceBackpack();
            return;
        }

        var item = Inv[absSlot];
        if (!ItemData.IsExchangePiece(item.ItemId))
        {
            SetPieceMessage(ItemData.Text(TextPlacePiece, "Place one of the pieces."), true);
            _pieceSubMessage.Text = $"{ItemData.DisplayName(item.ItemId)} is not a generator piece.";
            return;
        }

        _pieceItemId = item.ItemId;
        _piecePosition = rel;
        _pieceRewards = ItemData.PieceRewards(item.ItemId);
        _pieceSocket.Set(item.ItemId, item.Count, item.Durability);
        var blocked = PieceBlockReason();
        SetPieceMessage(blocked ?? ItemData.Text(TextPressStart, "Press Start."), blocked != null);
        _pieceSubMessage.Text = $"{_pieceRewards.Count} possible rewards.";
        RefreshPieceBackpack();
        RefreshPieceActions();
    }

    private void StartPieceSpin()
    {
        if (!CanStartPieceSpin()) return;
        if (PieceBlockReason() is { } blocked)
        {
            SetPieceMessage(blocked, true);
            return;
        }
        _pieceSpinning = true;
        _pieceSpinClock = 0;
        SetPieceMessage(ItemData.Text(TextPressStop, "Press Stop."), false);
        RefreshPieceActions();
    }

    private void StopPieceSpin()
    {
        if (!_pieceSpinning || _pieceBusy) return;
        if (PieceBlockReason() is { } blocked)
        {
            _pieceSpinning = false;
            SetPieceMessage(blocked, true);
            RefreshPieceActions();
            return;
        }
        _pieceSpinning = false;
        _pieceBusy = true;
        RefreshPieceActions();
        Net.I.SendPieceExchange(_pieceNpcId, _pieceItemId, _piecePosition);
    }

    private void PieceChangeTick(double delta)
    {
        if (!_pieceShown || !_pieceSpinning || _pieceRewards.Count == 0) return;

        _pieceSpinClock += delta;
        if (_pieceSpinClock < PieceSpinInterval) return;
        _pieceSpinClock = 0;

        int slot = GD.RandRange(0, PieceResultSlotCount - 1);
        int reward = _pieceRewards[GD.RandRange(0, _pieceRewards.Count - 1)];
        _pieceResultSockets[slot].Set(reward, 1, 0);
    }

    private void OnPieceExchangeResult(PieceExchangeResult result)
    {
        if (!_pieceShown) return;
        _pieceBusy = false;
        _pieceSpinning = false;

        if (result.ResultCode == Net.PieceResultSucceeded && result.RewardItemId != 0)
        {
            foreach (var socket in _pieceResultSockets)
                socket.Set(result.RewardItemId, 1, 0);

            SetPieceMessage(PieceEffectText(result.Effect), false);
            _pieceSubMessage.Text = ItemData.DisplayName(result.RewardItemId);
            CombatNotice($"Chaotic Generator: {ItemData.DisplayName(result.RewardItemId)}");
            _pieceItemId = 0;
            _piecePosition = -1;
            _pieceRewards = System.Array.Empty<int>();
            _pieceSocket.Clear();
        }
        else
        {
            foreach (var socket in _pieceResultSockets) socket.Clear();
            SetPieceMessage(result.ResultCode == Net.PieceResultNoPiece
                ? ItemData.Text(TextPlacePiece, "Place one of the pieces.")
                : ItemData.Text(TextFailed, "The divination has failed."), true);
            _pieceSubMessage.Text = "";
        }

        RefreshPieceBackpack();
        RefreshPieceActions();
        if (CharTabOpen()) RefreshInventoryUI();
    }

    private static string PieceEffectText(byte effect) => effect switch
    {
        Net.PieceEffectWhite => ItemData.Text(TextLucky, "It must be your lucky day."),
        Net.PieceEffectGreen => ItemData.Text(TextNotBad, "Your luck isn't that bad."),
        _ => ItemData.Text(TextBetterDays, "There will be better days."),
    };

    private void RefreshPieceBackpack()
    {
        if (_pieceBackpackGrid == null) return;
        HideItemTooltip();
        _pieceBackpackCells.Clear();
        foreach (var child in _pieceBackpackGrid.GetChildren()) child.QueueFree();

        var grid = new GridContainer { Columns = 5 };
        grid.AddThemeConstantOverride("h_separation", 4);
        grid.AddThemeConstantOverride("v_separation", 4);
        _pieceBackpackGrid.AddChild(grid);

        var usable = UsableBackpackSlots(ItemData.IsExchangePiece);
        foreach (int abs in usable)
        {
            int slot = abs;
            var cell = new UpgradeBackpackCell(slot, Inv[slot], _piecePosition == slot - GridStart);
            _pieceBackpackCells.Add(cell);
            cell.Pressed += () => PlacePiece(slot);
            cell.Hovered += (slot, item) => ShowItemTooltip(slot, item);
            cell.Unhovered += HideItemTooltip;
            grid.AddChild(cell);
        }
        for (int i = usable.Count; i < FilteredBackpackMinCells; i++)
            grid.AddChild(new UpgradeBackpackCell(-1, default, false));

        var footer = new HBoxContainer();
        footer.AddThemeConstantOverride("separation", 6);
        footer.AddChild(UiTheme.Pill($"{usable.Count} usable", UiTheme.Gold));
        footer.AddChild(UiTheme.Pill($"{BackpackUsedCount()}/{GridCount}", UiTheme.Edge));
        footer.AddChild(UiTheme.Text($"{Sheet.Gold:n0} gold", 12, UiTheme.Gold, HorizontalAlignment.Right));
        _pieceBackpackGrid.AddChild(footer);
    }

    private void SetPieceMessage(string text, bool warn)
    {
        _pieceMessage.Text = text;
        _pieceMessage.AddThemeColorOverride("font_color", warn ? UiTheme.Bad : UiTheme.TextHi);
    }

    private string? PieceBlockReason()
    {
        if (Sheet.MaxWeight > 0 && CarriedWeight() >= Sheet.MaxWeight)
            return "You are carrying too much.";

        for (int abs = GridStart; abs < GridStart + GridCount && abs < Inv.Length; abs++)
            if (Inv[abs].IsEmpty) return null;

        return "Your bag is full.";
    }

    private bool CanStartPieceSpin()
        => _pieceShown && !_pieceBusy && !_pieceSpinning && _pieceNpcId != 0
           && _pieceItemId != 0 && _pieceRewards.Count > 0;

    private void RefreshPieceActions()
    {
        if (_pieceStartBtn == null) return;
        _pieceStartBtn.Disabled = !CanStartPieceSpin();
        _pieceStopBtn.Disabled = !_pieceSpinning || _pieceBusy;
        _pieceTalkBtn.Disabled = _pieceBusy || _pieceSpinning;
    }

    private int PieceSocketSlot() => _piecePosition >= 0 ? GridStart + _piecePosition : -1;

    private void OnPieceInventorySlot(int absSlot, ItemSlot item)
    {
        if (!_pieceShown) return;
        if (absSlot < GridStart || absSlot >= GridStart + GridCount) return;

        if (_piecePosition == absSlot - GridStart && (item.IsEmpty || item.ItemId != _pieceItemId))
        {
            _pieceItemId = 0;
            _piecePosition = -1;
            _pieceSpinning = false;
            _pieceRewards = System.Array.Empty<int>();
            _pieceSocket?.Clear();
            RefreshPieceActions();
        }

        RefreshPieceBackpack();
    }

    private void OnPieceInventoryGrid(ItemSlot[] items)
    {
        if (!_pieceShown) return;
        RefreshPieceBackpack();
    }

    public string PieceChangeReport()
    {
        var parts = new List<string> { $"shown={_pieceShown}", $"npc={_pieceNpcId}",
            $"piece={_pieceItemId}@{_piecePosition}", $"rewards={_pieceRewards.Count}",
            $"spinning={_pieceSpinning}", $"busy={_pieceBusy}" };
        return string.Join(" ", parts);
    }

    public bool StagePieceExchange(int pieceItemId)
    {
        if (!_pieceShown) return false;
        int slot = FindBackpackSlot(pieceItemId);
        if (slot < 0) return false;
        PlacePiece(slot);
        return _pieceItemId == pieceItemId;
    }

    public void TalkForTest() => TalkToGenerator();

    public void StartPieceSpinForTest() => StartPieceSpin();

    public void StopPieceSpinForTest() => StopPieceSpin();
}
