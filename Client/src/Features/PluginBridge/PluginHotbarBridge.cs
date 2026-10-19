using Godot;
using LibreKO.Scene;

namespace LibreKO.Features.PluginBridge;

internal sealed class PluginHotbarBridge(HotbarFeature hotbar, ItemTooltipFeature itemTooltip, SkillTimers skillTimers, SelfSession self) : IGameHotbar
{
    event Action? IGameHotbar.Changed { add { } remove { } }

    public void RaiseHotbar() => Callable.From(PluginHost.Game.RaiseHotbar).CallDeferred();

    public int Pages => HotbarLayout.Pages;

    public int SlotsPerPage => HotbarLayout.SlotsPerPage;

    public int Page => hotbar.HotPage;

    public bool Locked => Config.HotbarLocked;

    public HotSlotInfo Slot(int abs)
    {
        if (abs < 0 || abs >= HotbarLayout.Total) return HotSlotInfo.Empty(abs);
        int id = hotbar.Slots[abs];
        if (id == 0) return HotSlotInfo.Empty(abs);
        var s = SkillData.Get(id);
        if (s == null && ItemData.Get(id) is { Effect1: not 0 } def) s = SkillData.Get(def.Effect1);
        float cooldown = s == null ? 0f : skillTimers.Fraction(s, FrameClock.Now());
        bool isSkill = SkillData.IsSkill(id);
        int needId = isSkill ? s?.ConsumedItem ?? 0 : id;
        int count = -1;
        bool enough = true;
        if (needId != 0 && ItemData.Get(needId) != null)
        {
            int need = s is { IsRanged: true } ? Mathf.Max(1, s.NeedArrow) : 1;
            count = self.Inventory.CountInBackpack(needId);
            enough = count >= need;
        }
        return new HotSlotInfo(abs, id, isSkill,
            isSkill ? SkillIcons.Get(id) : ItemIcons.Get(id), cooldown,
            isSkill ? s?.Name ?? "" : ItemData.DisplayName(id),
            isSkill && s != null ? SkillText.Tooltip(s) : "", count, enough,
            isSkill && s != null && !self.Vitals.HasStamina(s.Sp));
    }

    public void Activate(int slotInPage) => hotbar.ActivateHotSlot(slotInPage);

    public void ActivateAbs(int abs) => hotbar.FireHotSlot(abs);

    public int Selected => hotbar.HotSelected;

    public void Select(int abs) => hotbar.SelectHotAbs(abs);

    public void SetPage(int page) => hotbar.SetHotPage(page);

    public void ChangePage(int delta) => hotbar.ChangeHotPage(delta);

    void IGameHotbar.Drop(int abs, int id, int fromAbs) => hotbar.DropOntoHotAbs(abs, id, fromAbs);

    void IGameHotbar.Clear(int abs) => hotbar.ClearHotAbs(abs);

    public void SetLocked(bool locked) => hotbar.ApplyLayout(locked, Config.HotbarVertical, Config.HotbarExtraBars);

    void IGameHotbar.ShowTooltip(int abs) => hotbar.HotAbsHover(abs, true);

    void IGameHotbar.HideTooltip() => itemTooltip.Hide();
}
