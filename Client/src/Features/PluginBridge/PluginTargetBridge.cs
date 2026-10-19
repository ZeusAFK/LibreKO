using LibreKO.Scene;

namespace LibreKO.Features.PluginBridge;

internal sealed class PluginTargetBridge(SelectionFeature selection, EntitySystem entity, EntityPlatesSystem entityPlates, LootFilterFeature lootFilter, TargetQuestDropsFeature targetQuestDrops, PartySession party) : IGameTarget
{
    private int targetId = -1, targetHp, targetMaxHp;

    private string targetName = "";

    private TargetHpLook targetLook;

    event Action? IGameTarget.Changed { add { } remove { } }

    public void RefreshTarget()
    {
        int id = selection.SelectedId >= 0 && entity.Ents.ContainsKey(selection.SelectedId) ? selection.SelectedId : -1;
        int hp = 0, max = 0;
        string name = "";
        if (id >= 0 && entity.Ents.TryGetValue(id, out var e)) { hp = e.Hp; max = e.MaxHp; name = entityPlates.ShownName(e); }
        var look = id >= 0 ? (TargetHpLook)PartyHud.HpLook(party.StatusOf(id), (long)FrameClock.Now()) : TargetHpLook.Normal;
        if (id == targetId && hp == targetHp && max == targetMaxHp && name == targetName && look == targetLook) return;
        targetId = id; targetHp = hp; targetMaxHp = max; targetName = name; targetLook = look;
        PluginHost.Game.RaiseTarget();
    }

    internal string TargetName => targetName;

    public bool Has => targetId >= 0;

    public int Id => targetId;

    string IGameTarget.Name => targetName;

    int IGameTarget.Level => targetId >= 0 && entity.Ents.TryGetValue(targetId, out var e) ? e.Level : 0;

    int IGameTarget.Hp => targetHp;

    int IGameTarget.MaxHp => targetMaxHp;

    public bool Hostile => targetId >= 0 && entity.Ents.TryGetValue(targetId, out var e) && e.Attackable;

    public bool IsPlayer => targetId >= 0 && entity.Ents.TryGetValue(targetId, out var e) && !e.IsNpc;

    public TargetHpLook HpLook => targetLook;

    public bool IsMonster => targetId >= 0 && entity.Ents.TryGetValue(targetId, out var e) && e.IsMonster;

    public int NpcId => targetId >= 0 && entity.Ents.TryGetValue(targetId, out var e) && e.IsNpc ? e.NpcId : 0;

    int IGameTarget.ClanId => targetId >= 0 && entity.Ents.TryGetValue(targetId, out var e) && !e.IsNpc ? e.KnightsId : 0;

    string IGameTarget.ClanName => targetId >= 0 && entity.Ents.TryGetValue(targetId, out var e) && !e.IsNpc ? e.ClanName : "";

    public GameDropTable? Drops
    {
        get
        {
            if (lootFilter.CachedDrops(targetId) is not { Shown: true } list) return null;
            var items = list.Items.Select(d => new GameDrop(d.ItemId, ItemData.DisplayName(d.ItemId), ItemIcons.Get(d.ItemId), (int)d.Band)).ToList();
            return new GameDropTable(list.TargetId, list.CoinMin, list.CoinMax, list.Experience, items);
        }
    }

    public void RequestDrops() => lootFilter.RequestDropsFor(targetId);

    public IReadOnlyList<GameTargetDrop> QuestDrops => targetQuestDrops.ForPlugin();

    public void RaiseDrops(int targetId)
    {
        if (targetId == this.targetId) PluginHost.Game.RaiseTarget();
    }

    public void Clear() => selection.Deselect();
}
