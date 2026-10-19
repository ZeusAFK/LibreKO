using Godot;

namespace LibreKO.Features.Hud;

internal sealed class GenieHammerFeature : IFeature
{
    private readonly IWorldContext context;
    private readonly IGenieRequests requests;
    private readonly SelfSession self;
    private readonly GenieSession genieSession;

    public GenieHammerFeature(IWorldContext context, IGenieRequests requests, SelfSession self, GenieSession genieSession)
    {
        this.context = context;
        this.requests = requests;
        this.self = self;
        this.genieSession = genieSession;
    }

    private IGenieRunner runner = null!;

    internal void AttachRunner(IGenieRunner runner) => this.runner = runner;

    internal void DetachRunner() => runner = null!;

    public void Build()
    {
        genieSession.GenieHammerResult += OnHammerResult;
    }

    public void Dispose()
    {
        genieSession.GenieHammerResult -= OnHammerResult;
    }

    private static readonly int[] HammerItems = [810227000, 810935000, 900819000];
    private static readonly int[] WeaponSlots = [InventoryConstants.RightHand, InventoryConstants.LeftHand];

    private static readonly int[] ArmourSlots =
    [InventoryConstants.Head, InventoryConstants.Breast, InventoryConstants.Leg, InventoryConstants.Glove, InventoryConstants.Foot];

    private const double HammerCheckInterval = 10;

    private SpinBox hammerThreshold = null!;
    private Label hammerStatus = null!;
    private double hammerNextAt;

    internal void BuildHammerOptions(VBoxContainer parent, GenieControls builder)
    {
        parent.AddChild(UiTheme.SectionTitle("Genie Hammer"));
        hammerThreshold = builder.Percent(parent, "Repair below durability (%)", GenieSetup.MinHammerPercent,
            GenieSetup.MaxHammerPercent, v => runner.Setup.HammerPercent = v);
        hammerStatus = UiTheme.Text("Uses one hammer charge to repair worn equipment. Turn it on for weapons or armour under Set Others.",
            11, UiTheme.TextLo);
        hammerStatus.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        parent.AddChild(hammerStatus);
    }

    internal void SyncHammerControls() => hammerThreshold.SetValueNoSignal(runner.Setup.HammerPercent);

    internal static void CarryHammerSettings(ConfigFile config, GenieSetup setup)
    {
        bool auto = config.GetValue("genie", "auto_hammer", false).AsBool();
        setup.Set(GenieOther.HammerWeapons, auto);
        setup.Set(GenieOther.HammerArmour, auto);
        int threshold = config.GetValue("genie", "hammer_threshold", GenieSetup.DefaultHammerPercent).AsInt32();
        setup.HammerPercent = threshold is >= GenieSetup.MinHammerPercent and <= GenieSetup.MaxHammerPercent
            ? threshold : GenieSetup.DefaultHammerPercent;
    }

    private void OnHammerResult(bool ok)
    {
        hammerStatus.Text = ok ? "Equipment repaired. One hammer charge used."
            : "Repair was not needed or no usable Genie Hammer was available.";
    }

    internal void HammerTick(double now)
    {
        bool weapons = runner.Setup.Has(GenieOther.HammerWeapons), armour = runner.Setup.Has(GenieOther.HammerArmour);
        if ((!weapons && !armour) || now < hammerNextAt) return;
        hammerNextAt = now + HammerCheckInterval;
        if (!(weapons && Worn(WeaponSlots)) && !(armour && Worn(ArmourSlots))) return;
        bool hammer = false;
        for (int i = InventoryFeature.GridStart; i < self.Inventory.Length; i++)
        {
            if (i >= InventoryConstants.CospreStart && i < InventoryConstants.MagicBagStart) continue;
            if ((System.Array.IndexOf(HammerItems, self.Inventory[i].ItemId) >= 0) && self.Inventory[i].Durability > 0)
            { hammer = true; break; }
        }
        if (!hammer) { hammerStatus.Text = "No Genie Hammer charges in inventory."; return; }
        hammerStatus.Text = "Repairing equipment…";
        requests.Hammer((byte)runner.Setup.HammerPercent);
    }

    private bool Worn(int[] wornSlots)
    {
        foreach (int i in wornSlots)
        {
            var slot = self.Inventory[i];
            if (slot.ItemId > 0 && ItemData.Get(slot.ItemId) is { Duration: > 1 } item
                && slot.Durability * 100.0 <= item.Duration * runner.Setup.HammerPercent)
                return true;
        }
        return false;
    }
}
