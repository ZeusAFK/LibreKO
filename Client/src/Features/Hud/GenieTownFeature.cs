using Godot;

namespace LibreKO.Features.Hud;

internal sealed class GenieTownFeature : IFeature
{
    private readonly IWorldContext context;
    private readonly GeniePotionsFeature geniePotions;
    private readonly PartySession party;
    private readonly IGenieRequests requests;
    private readonly SelfSession self;
    private readonly TownRecallFeature townRecall;

    public GenieTownFeature(IWorldContext context, GeniePotionsFeature geniePotions, PartySession party, IGenieRequests requests, SelfSession self, TownRecallFeature townRecall)
    {
        this.context = context;
        this.geniePotions = geniePotions;
        this.party = party;
        this.requests = requests;
        this.self = self;
        this.townRecall = townRecall;
    }

    private IGenieRunner runner = null!;

    internal void AttachRunner(IGenieRunner runner) => this.runner = runner;

    internal void DetachRunner() => runner = null!;

    public void Build()
    {
    }

    public void Dispose()
    {
    }

    private const int OtherTitleText = 31033;
    private const int RogueFamily = 2;
    private const int BasicRangedSkillSuffix = 3;
    private const int ClassSkillScale = 1000;
    private const int SpiritOfItem = 810378000;

    private readonly Dictionary<GenieOther, CheckButton> other = new();
    private SpinBox townHpCount = null!;
    private SpinBox townMpCount = null!;
    private bool sawParty;
    private bool spiritAsked;
    private bool lowHpWarned;

    internal void BuildTownOptions(VBoxContainer misc, GenieControls builder)
    {
        misc.AddChild(UiTheme.SectionTitle(ItemData.Text(OtherTitleText, "Set Others")));
        foreach (var otherRow in GenieTownReturn.OtherRows)
        {
            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", 8);
            misc.AddChild(row);
            var option = otherRow.Option;
            var toggle = builder.Option(row, OtherLabel(otherRow), on => runner.Setup.Set(option, on));
            toggle.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            other[option] = toggle;
            if (option == GenieOther.HpPotions)
                townHpCount = builder.Number(row, GenieTownReturn.MinCount, GenieTownReturn.MaxCount, v => runner.Setup.TownHpCount = v);
            else if (option == GenieOther.MpPotions)
                townMpCount = builder.Number(row, GenieTownReturn.MinCount, GenieTownReturn.MaxCount, v => runner.Setup.TownMpCount = v);
        }
    }

    private static string OtherLabel(GenieTownReturn.OtherRow row)
    {
        string text = row.Text > 0 ? ItemData.Text(row.Text, row.Label) : row.Label;
        return string.Join(' ', text.Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    internal void SyncTownControls()
    {
        foreach (var (option, toggle) in other) toggle.SetPressedNoSignal(runner.Setup.Has(option));
        townHpCount.SetValueNoSignal(runner.Setup.TownHpCount);
        townMpCount.SetValueNoSignal(runner.Setup.TownMpCount);
    }

    internal void ResetTownWatch()
    {
        sawParty = false;
        spiritAsked = false;
        lowHpWarned = false;
    }

    internal bool TownTick()
    {
        var enabled = runner.Setup.Other;
        AutoSpirit(enabled);
        if (party.InParty) sawParty = true;
        int arrowId = SkillData.Get(self.Class * ClassSkillScale + BasicRangedSkillSuffix)?.ConsumedItem ?? 0;
        var state = new GenieTownReturn.State(
            geniePotions.PotionStock(HealTarget.Hp), runner.Setup.TownHpCount,
            geniePotions.PotionStock(HealTarget.Mp), runner.Setup.TownMpCount,
            NeedsArrows: arrowId > 0 && CharacterClassCatalog.Family(self.Class) == RogueFamily && HoldsBow(),
            Arrows: arrowId > 0 ? self.Inventory.CountInBackpack(arrowId) : 0,
            WasInParty: sawParty, InParty: party.InParty, PartyOthersAllDead: PartyOthersAllDead(),
            WeaponBroken: WeaponBroken());
        var triggered = GenieTownReturn.Triggered(enabled, state);
        if (triggered == GenieOther.None) return false;
        if (!GenieTownReturn.CanMoveToTown(self.Vitals.Hp, self.Vitals.MaxHp))
        {
            if (!lowHpWarned) context.Log.Notice(ItemData.Text(GenieTownReturn.LowHpText, GenieTownReturn.LowHpMessage));
            lowHpWarned = true;
            return false;
        }
        foreach (var rule in GenieTownReturn.Rules)
            if (rule.Option == triggered) context.Log.Notice(ItemData.Text(rule.MessageText, rule.Message));
        runner.Stop();
        townRecall.Press();
        return true;
    }

    private void AutoSpirit(GenieOther enabled)
    {
        bool hasSpirit = self.Inventory.CountInBackpack(SpiritOfItem) > 0;
        if (!GenieTownReturn.WantsSpirit(enabled, runner.Points, hasSpirit))
        {
            if (runner.Points != GenieTownReturn.AutoSpiritAtPoints) spiritAsked = false;
            return;
        }
        if (spiritAsked) return;
        spiritAsked = true;
        requests.System(GenieSystemCommand.UseSpiritPotion);
    }

    private bool HoldsBow()
    {
        foreach (int slot in new[] { InventoryConstants.RightHand, InventoryConstants.LeftHand })
            if (!self.Inventory[slot].IsEmpty
                && ItemData.Get(self.Inventory[slot].ItemId)?.Kind is WeaponAnimation.Bow or WeaponAnimation.Crossbow or WeaponAnimation.LongBow)
                return true;
        return false;
    }

    private bool WeaponBroken()
    {
        foreach (int slot in new[] { InventoryConstants.RightHand, InventoryConstants.LeftHand })
            if (!self.Inventory[slot].IsEmpty && self.Inventory[slot].Durability <= 0 && ItemData.Get(self.Inventory[slot].ItemId) is { } def
                && WeaponAnimation.IsWeapon(def.Kind))
                return true;
        return false;
    }

    private bool PartyOthersAllDead()
    {
        if (!party.InParty) return false;
        int others = 0;
        foreach (var member in party.Members)
        {
            if (member.CharId == context.Self.CharId) continue;
            others++;
            if (member.Hp > 0) return false;
        }
        return others > 0;
    }
}
