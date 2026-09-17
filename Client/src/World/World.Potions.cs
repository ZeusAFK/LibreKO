using Godot;
using LibreKO.Domain;

namespace LibreKO;

public partial class World
{
    private const double PotionPollInterval = 0.4;

    private HBoxContainer? _potionRow;
    private PotionButton? _hpPotion;
    private PotionButton? _mpPotion;
    private double _potionPollAt;

    private static void ScaleVitals(Control status)
    {
        status.Scale = Vector2.One * HudPlacement.VitalsScale;
        status.PivotOffset = new Vector2(VitalsSize.X * 0.5f, VitalsSize.Y);
    }

    private void BuildPotionBar()
    {
        if (_touchActions == null) return;
        var (hp, mp) = CreatePotionButtons();
        _touchActions.AttachPotion(hp, 0);
        _touchActions.AttachPotion(mp, 1);
    }

    internal (Control Hp, Control Mp) CreatePotionButtons()
    {
        _hpPotion = new PotionButton(HealTarget.Hp, UiTheme.Hp);
        _mpPotion = new PotionButton(HealTarget.Mp, UiTheme.Mp);
        _hpPotion.Pressed += () => UsePotion(_hpPotion);
        _mpPotion.Pressed += () => UsePotion(_mpPotion);
        RefreshPotionBar();
        return (_hpPotion, _mpPotion);
    }

    private void UsePotion(PotionButton? button)
    {
        if (button is { ItemId: not 0 } && !UseHotItem(button.ItemId))
            CombatNotice($"{ItemData.DisplayName(button.ItemId)} has no usable effect.");
    }

    private void PotionBarTick(double now)
    {
        if (_hpPotion == null) return;
        ApplyPotionCooldown(_hpPotion, now);
        ApplyPotionCooldown(_mpPotion, now);
        if (now < _potionPollAt) return;
        _potionPollAt = now + PotionPollInterval;
        RefreshPotionBar();
    }

    private void ApplyPotionCooldown(PotionButton? button, double now)
    {
        if (button == null) return;
        var def = button.ItemId == 0 ? null : ItemData.Get(button.ItemId);
        var skill = def is { Effect1: not 0 } ? SkillData.Get(def.Effect1) : null;
        button.SetCooldown(skill == null ? 0f : SkillCooldown(skill, now));
    }

    private void RefreshPotionBar()
    {
        _hpPotion?.Set(BestPotion(HealTarget.Hp), this);
        _mpPotion?.Set(BestPotion(HealTarget.Mp), this);
    }

    internal int PotionCount(int itemId) => CountInBackpack(itemId);

    private void PotionBarDispose()
    {
        _hpPotion = null;
        _mpPotion = null;
    }

    private int BestPotion(int healTarget)
    {
        int best = 0, bestHeal = 0;
        for (int abs = GridStart; abs < Inv.Length; abs++)
        {
            int id = Inv[abs].ItemId;
            if (id == 0) continue;
            int heal = ItemData.PotionHeal(id, healTarget);
            if (heal <= bestHeal) continue;
            best = id;
            bestHeal = heal;
        }
        return best;
    }
}

public partial class PotionButton : TouchTapButton
{
    private readonly Color _accent;
    private readonly TextureRect _art;
    private readonly CooldownWipe _cool;
    private readonly Label _count;

    public int HealTarget { get; }
    public int ItemId { get; private set; }

    public void SetCooldown(float remain) => _cool.Remain = remain;

    public PotionButton(int healTarget, Color accent)
    {
        HealTarget = healTarget;
        _accent = accent;
        float size = TouchControls.PotionSize;

        FocusMode = FocusModeEnum.None;
        ActionMode = ActionModeEnum.Press;
        CustomMinimumSize = new Vector2(size, size);
        _assigned = Skin(0f, 0f);
        _empty = Skin(0.42f, 0.45f);
        AddThemeStyleboxOverride("pressed", Skin(0.34f, 0.9f));
        AddThemeStyleboxOverride("disabled", _empty);

        _art = new TextureRect
        {
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        _art.SetAnchorsPreset(LayoutPreset.FullRect);
        float inset = size * 0.03f;
        _art.OffsetLeft = _art.OffsetTop = inset;
        _art.OffsetRight = _art.OffsetBottom = -inset;
        AddChild(_art);

        _cool = TouchControls.AddCooldown(this, size);

        var badge = new PanelContainer { MouseFilter = MouseFilterEnum.Ignore };
        var chip = new StyleBoxFlat { BgColor = new Color(0.02f, 0.022f, 0.028f, 0.90f) };
        chip.SetCornerRadiusAll((int)(size * 0.15f));
        chip.ContentMarginLeft = chip.ContentMarginRight = size * 0.10f;
        chip.ContentMarginTop = chip.ContentMarginBottom = size * 0.015f;
        badge.AddThemeStyleboxOverride("panel", chip);
        badge.SetAnchorsPreset(LayoutPreset.BottomRight);
        badge.GrowHorizontal = GrowDirection.Begin;
        badge.GrowVertical = GrowDirection.Begin;
        badge.OffsetLeft = badge.OffsetRight = -size * 0.06f;
        badge.OffsetTop = badge.OffsetBottom = -size * 0.06f;
        AddChild(badge);

        _count = new Label
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        _count.AddThemeFontSizeOverride("font_size", (int)(size * 0.20f));
        _count.AddThemeColorOverride("font_color", UiTheme.TextHi);
        badge.AddChild(_count);
        _badge = badge;
    }

    private readonly Control _badge;
    private readonly StyleBoxFlat _assigned;
    private readonly StyleBoxFlat _empty;

    internal void Set(int itemId, World world)
    {
        ItemId = itemId;
        Disabled = itemId == 0;
        var skin = itemId == 0 ? _empty : _assigned;
        AddThemeStyleboxOverride("normal", skin);
        AddThemeStyleboxOverride("hover", skin);
        _art.Texture = itemId == 0 ? null : ItemData.Icon(itemId);
        _art.SelfModulate = new Color(1f, 1f, 1f, itemId == 0 ? 0f : 1f);
        int count = itemId == 0 ? 0 : world.PotionCount(itemId);
        _count.Text = count.ToString();
        _badge.Visible = count > 0;
        TooltipText = itemId == 0
            ? HealTarget == Domain.HealTarget.Hp ? "No health potion" : "No mana potion"
            : $"{ItemData.DisplayName(itemId)} × {count}";
    }

    private StyleBoxFlat Skin(float fill, float edge)
    {
        var box = new StyleBoxFlat
        {
            BgColor = new Color(_accent.R * 0.20f, _accent.G * 0.20f, _accent.B * 0.20f, fill),
            BorderColor = new Color(_accent, edge),
            ShadowColor = new Color(0f, 0f, 0f, 0.5f),
            ShadowSize = (int)(TouchControls.PotionSize * 0.05f),
        };
        box.SetCornerRadiusAll((int)(TouchControls.PotionSize * 0.22f));
        box.SetBorderWidthAll(edge > 0f ? 2 : 0);
        return box;
    }
}
