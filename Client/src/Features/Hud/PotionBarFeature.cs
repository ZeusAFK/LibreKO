using Godot;

namespace LibreKO.Features.Hud;

internal sealed class PotionBarFeature : IFeature, ILateTickFeature
{
    private readonly IWorldContext context;
    private readonly HotbarFeature hotbar;
    private readonly SelfSession self;
    private readonly SkillTimers skillTimers;

    public PotionBarFeature(IWorldContext context, HotbarFeature hotbar, SelfSession self, SkillTimers skillTimers)
    {
        this.context = context;
        this.hotbar = hotbar;
        this.self = self;
        this.skillTimers = skillTimers;
    }

    public void LateTick(double delta, double nowSec) => PotionBarTick(nowSec);

    public void Build()
    {
    }

    public void Dispose()
    {
    }

    private const double PotionPollInterval = 0.4;

    internal PotionButton? HpPotion { get; private set; }
    internal PotionButton? MpPotion { get; private set; }
    private double potionPollAt;

    internal void BuildPotionBar()
    {
        if (context.Hud.TouchActions == null) return;
        var (hp, mp) = CreatePotionButtons();
        context.Hud.TouchActions.AttachPotion(hp, 0);
        context.Hud.TouchActions.AttachPotion(mp, 1);
    }

    internal (Control Hp, Control Mp) CreatePotionButtons()
    {
        HpPotion = new PotionButton(HealTarget.Hp, UiTheme.Hp);
        MpPotion = new PotionButton(HealTarget.Mp, UiTheme.Mp);
        HpPotion.Pressed += () => UsePotion(HpPotion);
        MpPotion.Pressed += () => UsePotion(MpPotion);
        RefreshPotionBar();
        return (HpPotion, MpPotion);
    }

    internal void UsePotion(PotionButton? button)
    {
        if (button is { ItemId: not 0 } && !hotbar.UseHotItem(button.ItemId))
            context.Log.Notice($"{ItemData.DisplayName(button.ItemId)} has no usable effect.");
    }

    private void PotionBarTick(double now)
    {
        if (HpPotion == null) return;
        ApplyPotionCooldown(HpPotion, now);
        ApplyPotionCooldown(MpPotion, now);
        if (now < potionPollAt) return;
        potionPollAt = now + PotionPollInterval;
        RefreshPotionBar();
    }

    private void ApplyPotionCooldown(PotionButton? button, double now)
    {
        if (button == null) return;
        var def = button.ItemId == 0 ? null : ItemData.Get(button.ItemId);
        var skill = def is { Effect1: not 0 } ? SkillData.Get(def.Effect1) : null;
        button.SetCooldown(skill == null ? 0f : skillTimers.Fraction(skill, now));
    }

    private void RefreshPotionBar()
    {
        HpPotion?.Set(BestPotion(HealTarget.Hp), PotionCount);
        MpPotion?.Set(BestPotion(HealTarget.Mp), PotionCount);
    }

    private int PotionCount(int itemId) => self.Inventory.CountInBackpack(itemId);

    internal void PotionBarDispose()
    {
        HpPotion = null;
        MpPotion = null;
    }

    internal int BestPotion(int healTarget)
    {
        var ids = new int[self.Inventory.Length - Inventory.GridStart];
        for (int abs = Inventory.GridStart; abs < self.Inventory.Length; abs++) ids[abs - Inventory.GridStart] = self.Inventory[abs].ItemId;
        return MaestroPotions.PickStrongest(ids, id => ItemData.PotionHeal(id, healTarget), self.Sheet.Gold);
    }
}
