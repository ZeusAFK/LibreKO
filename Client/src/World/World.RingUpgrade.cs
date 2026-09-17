using Godot;
using LibreKO.Domain;
using LibreKO.Network;

namespace LibreKO;

public partial class World
{
    private CanvasLayer _ringUpLayer = null!;
    private HudWindow _ringUpPanel = null!;
    private Label _ringUpSlotLabel = null!;
    private Label _ringUpRateLabel = null!;
    private Label _ringUpStatusLabel = null!;
    private Button _ringUpUpgradeBtn = null!;
    private Button _ringUpRightBtn = null!;
    private Button _ringUpLeftBtn = null!;
    private bool _ringUpShown;
    private int _ringUpSlot = InventoryConstants.RightRing;

    private void RingUpgradeInit()
    {
        _ringUpLayer = new CanvasLayer { Layer = 62 };
        AddChild(_ringUpLayer);
        _ringUpPanel = new HudWindow("ring_upgrade", "Ring Upgrade", new Vector2(220, 150)) { Visible = false };
        _ringUpPanel.Closed += CloseRingUpgrade;
        _ringUpLayer.AddChild(_ringUpPanel);

        var root = _ringUpPanel.Body;
        root.AddThemeConstantOverride("separation", 6);
        root.AddChild(UiTheme.SectionTitle("Accessory Upgrade"));

        var pick = new HBoxContainer();
        pick.AddThemeConstantOverride("separation", 6);
        _ringUpRightBtn = new Button { Text = "Right Ring", FocusMode = Control.FocusModeEnum.None, ToggleMode = true };
        _ringUpLeftBtn = new Button { Text = "Left Ring", FocusMode = Control.FocusModeEnum.None, ToggleMode = true };
        _ringUpRightBtn.Pressed += () => SelectRingSlot(InventoryConstants.RightRing);
        _ringUpLeftBtn.Pressed += () => SelectRingSlot(InventoryConstants.LeftRing);
        pick.AddChild(_ringUpRightBtn);
        pick.AddChild(_ringUpLeftBtn);
        root.AddChild(pick);

        _ringUpSlotLabel = UiTheme.Text("—", 13, UiTheme.TextHi);
        root.AddChild(_ringUpSlotLabel);

        var rateRow = new HBoxContainer();
        rateRow.AddThemeConstantOverride("separation", 8);
        rateRow.AddChild(UiTheme.Text("Success rate:", 13, UiTheme.TextLo));
        _ringUpRateLabel = UiTheme.Text("—", 13, UiTheme.Gold);
        rateRow.AddChild(_ringUpRateLabel);
        root.AddChild(rateRow);

        _ringUpUpgradeBtn = new Button { Text = "Upgrade", FocusMode = Control.FocusModeEnum.None };
        _ringUpUpgradeBtn.Pressed += OnRingUpgradePressed;
        root.AddChild(_ringUpUpgradeBtn);

        _ringUpStatusLabel = UiTheme.Text("", 12, UiTheme.TextLo);
        root.AddChild(_ringUpStatusLabel);

        Net.I.RingUpgradeStatusEvent += OnRingUpgradeStatus;
        Net.I.RingUpgradeResultEvent += OnRingUpgradeResult;
    }

    private void RingUpgradeDispose()
    {
        Net.I.RingUpgradeStatusEvent -= OnRingUpgradeStatus;
        Net.I.RingUpgradeResultEvent -= OnRingUpgradeResult;
    }

    private void ToggleRingUpgrade()
    {
        if (_ringUpShown) { CloseRingUpgrade(); return; }
        _ringUpPanel.Visible = true;
        _ringUpShown = true;
        SelectRingSlot(_ringUpSlot);
    }

    private void CloseRingUpgrade()
    {
        if (!_ringUpShown) return;
        _ringUpShown = false;
        _ringUpPanel.Visible = false;
    }

    private void SelectRingSlot(int slot)
    {
        _ringUpSlot = slot;
        _ringUpRightBtn.ButtonPressed = slot == InventoryConstants.RightRing;
        _ringUpLeftBtn.ButtonPressed = slot == InventoryConstants.LeftRing;

        string side = slot == InventoryConstants.RightRing ? "Right Ring" : "Left Ring";
        int itemId = RingItemIdAt(slot);
        if (itemId != 0)
        {
            _ringUpSlotLabel.Text = $"{side}: {ItemData.DisplayName(itemId)}";
            _ringUpUpgradeBtn.Disabled = false;
            _ringUpStatusLabel.Text = "";
            _ringUpRateLabel.Text = "…";
            Net.I.SendRingUpgradeStatus(slot);
        }
        else
        {
            _ringUpSlotLabel.Text = $"{side}: (empty)";
            _ringUpRateLabel.Text = "—";
            _ringUpUpgradeBtn.Disabled = true;
            _ringUpStatusLabel.Text = "Equip a ring in that slot first.";
        }
    }

    private void OnRingUpgradePressed()
    {
        if (RingItemIdAt(_ringUpSlot) == 0) return;
        Net.I.SendRingUpgrade(_ringUpSlot);
    }

    private void OnRingUpgradeStatus(int ratePct)
    {
        _ringUpRateLabel.Text = $"{ratePct}%";
    }

    private void OnRingUpgradeResult(int result, int newPlus)
    {
        switch (result)
        {
            case Net.RingUpgradeResultSuccess:
                _ringUpStatusLabel.Text = $"Success! Now +{newPlus}.";
                break;
            case Net.RingUpgradeResultInvalid:
                _ringUpStatusLabel.Text = $"Cannot upgrade further (+{newPlus}).";
                break;
            default:
                _ringUpStatusLabel.Text = $"Upgrade failed. Still +{newPlus}.";
                break;
        }
        Net.I.SendRingUpgradeStatus(_ringUpSlot);
    }

    private int RingItemIdAt(int slot)
    {
        var inv = Net.I.LastEnter.Inventory;
        if (inv != null && slot >= 0 && slot < inv.Length)
            return inv[slot].ItemId;
        return 0;
    }
}
