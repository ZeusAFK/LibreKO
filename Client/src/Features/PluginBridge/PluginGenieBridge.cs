using Godot;
using LibreKO.Features.CombatLog;

namespace LibreKO.Features.PluginBridge;

internal sealed class PluginGenieBridge(GenieFeature genie, GeniePotionsFeature geniePotions, GenieSubFeature genieSub, PluginBridgeFeature pluginBridge, CombatLogFeature log, GenieSession genieSession, SelfSession self, IGenieRequests genieRequests) : IGameGenie
{
    public bool IsOpen => genie.IsOpen;

    public bool Running => genieSession.GenieRunning;

    public bool Waiting => genie.RequestPending;

    public int Points => genie.Points;

    public int PointsPerSpirit => GenieSubFeature.PointsPerSpirit;

    public string RunLine => genie.RunLabel.Text;

    public GenieSetup Setup => genie.Genie.Clone();

    public IReadOnlyList<GenieTarget> Targets =>
        genie.TargetsInOrder().Select(name => new GenieTarget(name, !genie.MonstersOff.Contains(name))).ToList();

    public int TargetRows => GenieSubFeature.SubRows;

    public bool HasMore => genie.More != null;

    public GenieCellView Cell(GenieCellKind kind, int index)
    {
        int id = genie.Genie.Cells(kind)[index];
        return SkillData.Get(id) is { } skill
            ? new GenieCellView(id, SkillIcons.Get(id), SkillText.Tooltip(skill))
            : new GenieCellView(0, null, "");
    }

    public GeniePotionView Potion(GeniePotionSlot slot)
    {
        int id = geniePotions.PotionInUse(GeniePotionTargetOf(slot));
        return id > 0
            ? new GeniePotionView(id, ItemIcons.Get(id), self.Inventory.CountInBackpack(id), ItemData.DisplayName(id))
            : default;
    }

    public void Apply(GenieSetup setup) => genie.ApplySetup(setup.Clone());

    public bool SetCell(GenieCellKind kind, int index, int skillId)
    {
        if (GenieCellsLocked()) return false;
        int cell = GenieCellNumber(kind, index);
        if (skillId != 0)
        {
            if (SkillData.Get(skillId) is not { } skill) return false;
            if (!genie.SkillAllowed(skill, cell)) { genie.RefuseSkill(skill); return false; }
        }
        genie.SetCell(cell, skillId);
        genie.RefreshSlots();
        genie.SetupEdited();
        return true;
    }

    public void MoveCell(GenieCellKind fromKind, int fromIndex, GenieCellKind toKind, int toIndex)
    {
        if (GenieCellsLocked()) return;
        int from = GenieCellNumber(fromKind, fromIndex), to = GenieCellNumber(toKind, toIndex);
        int id = genie.GenieCell(from);
        if (id == 0 || SkillData.Get(id) is not { } skill) return;
        if (!genie.SkillAllowed(skill, to)) { genie.RefuseSkill(skill); return; }
        genie.MoveSkill(id, from, to);
    }

    public void PickSkill(GenieCellKind kind, int index, Vector2 screenAt)
    {
        if (GenieCellsLocked()) return;
        genie.OpenSkillPicker(GenieCellNumber(kind, index));
        genie.SkillPicker.Position = (Vector2I)screenAt;
    }

    public void PickPotion(GeniePotionSlot slot, Vector2 screenAt)
    {
        geniePotions.OpenPotionPicker(GeniePotionTargetOf(slot));
        geniePotions.PotionPicker.Position = (Vector2I)screenAt;
    }

    public void ToggleRun() => genieSub.ToggleFromSub();

    public void UseSpirit() => genieRequests.System(GenieSystemCommand.UseSpiritPotion);

    public void OpenMore() => genie.OpenMore();

    public void ToggleWindow() => genie.Toggle();

    public void AddTarget()
    {
        genie.AddTarget();
        pluginBridge.NotifyGenie();
    }

    public void RemoveTarget(int row)
    {
        var targets = genie.TargetsInOrder();
        if (row >= 0 && row < targets.Count) genie.RemoveTarget(targets[row]);
        pluginBridge.NotifyGenie();
    }

    public void SetTargetOn(int row, bool on)
    {
        var targets = genie.TargetsInOrder();
        if (row >= 0 && row < targets.Count) genie.SetTargetOn(targets[row], on);
        pluginBridge.NotifyGenie();
    }

    public void Close() => genie.Close();

    event Action? IGameGenie.Changed { add { } remove { } }

    private const int GenieRunningText = 31044;

    internal bool GenieCellsLocked()
    {
        if (!genieSession.GenieRunning) return false;
        log.Notice(ItemData.Text(GenieRunningText, "It cannot be used while Genie is activated."));
        return true;
    }

    internal int GenieCellNumber(GenieCellKind kind, int index) => kind switch
    {
        GenieCellKind.Attack => index,
        GenieCellKind.Recovery => GenieFeature.RecoveryFirstCell + index,
        _ => GenieFeature.SupportFirstCell + index,
    };

    internal static int GeniePotionTargetOf(GeniePotionSlot slot) => slot switch
    {
        GeniePotionSlot.Hp => HealTarget.Hp,
        GeniePotionSlot.Mp => HealTarget.Mp,
        _ => GeniePotionsFeature.PetTarget,
    };
}
