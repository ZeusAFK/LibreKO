using Godot;

namespace LibreKO;

public partial class World
{
    private static readonly int[] GenieHammerItems = [810227000, 810935000, 900819000];
    private const int MinRepairThreshold = 1;
    private const int MaxRepairThreshold = 50;
    private const double GenieHammerCheckInterval = 10;

    private CheckButton _genieAutoHammer = null!;
    private SpinBox _genieHammerThreshold = null!;
    private Label _genieHammerStatus = null!;
    private double _genieHammerNextAt;

    private void BuildGenieHammerOptions(VBoxContainer parent)
    {
        parent.AddChild(UiTheme.SectionTitle("Genie Hammer"));
        _genieAutoHammer = new CheckButton { Text = "Auto Genie Hammer" };
        parent.AddChild(_genieAutoHammer);
        _genieHammerThreshold = GeniePercent(parent, "Repair below durability (%)", 10, MinRepairThreshold, MaxRepairThreshold);
        _genieHammerStatus = UiTheme.Text("Uses one hammer charge to repair worn equipment.", 11, UiTheme.TextLo);
        parent.AddChild(_genieHammerStatus);
        Net.I.GenieHammerResult += OnGenieHammerResult;
    }

    private void OnGenieHammerResult(bool ok)
    {
        _genieHammerStatus.Text = ok ? "Equipment repaired. One hammer charge used."
            : "Repair was not needed or no usable Genie Hammer was available.";
    }

    private void GenieHammerTick(double now)
    {
        if (!_genieAutoHammer.ButtonPressed || now < _genieHammerNextAt) return;
        _genieHammerNextAt = now + GenieHammerCheckInterval;
        bool worn = false;
        for (int i = 0; i < InventoryConstants.SlotMax; i++)
        {
            var slot = Inv[i];
            var item = ItemData.Get(slot.ItemId);
            if (slot.ItemId > 0 && item is { Duration: > 1 }
                && slot.Durability * 100.0 <= item.Duration * _genieHammerThreshold.Value)
            { worn = true; break; }
        }
        if (!worn) return;
        bool hammer = false;
        for (int i = GridStart; i < Inv.Length; i++)
        {
            if (i >= InventoryConstants.CospreStart && i < InventoryConstants.MagicBagStart) continue;
            if ((System.Array.IndexOf(GenieHammerItems, Inv[i].ItemId) >= 0) && Inv[i].Durability > 0)
            { hammer = true; break; }
        }
        if (!hammer) { _genieHammerStatus.Text = "No Genie Hammer charges in inventory."; return; }
        _genieHammerStatus.Text = "Repairing equipment…";
        Net.I.SendGenieHammer((byte)_genieHammerThreshold.Value);
    }
}
