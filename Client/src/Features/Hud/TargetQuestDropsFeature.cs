using Godot;

namespace LibreKO.Features.Hud;

internal sealed class TargetQuestDropsFeature : IFeature
{
    private readonly IWorldContext context;
    private readonly CombatSession combat;

    public TargetQuestDropsFeature(IWorldContext context, CombatSession combat)
    {
        this.context = context;
        this.combat = combat;
    }

    public void Build() => combat.TargetQuestDropsEvent += On;

    private const float DropIconSize = 20f;
    private const int DropFont = 12;

    private VBoxContainer dropRows = null!;
    private int dropsFor = -1;
    internal IReadOnlyList<TargetQuestDrop> Drops { get; set; } = new List<TargetQuestDrop>();

    internal void BuildRow(VBoxContainer box)
    {
        dropRows = new VBoxContainer { Visible = false, CustomMinimumSize = new Vector2(UiKit.TargetHudWidth, 0) };
        dropRows.AddThemeConstantOverride("separation", 2);
        box.AddChild(dropRows);
    }

    public void Dispose() => combat.TargetQuestDropsEvent -= On;

    private void On(int id, IReadOnlyList<TargetQuestDrop> drops)
    {
        if (id != context.Selection.SelectedId) return;
        if (id == dropsFor && SameDrops(drops)) return;
        dropsFor = id;
        this.Drops = drops;
        RebuildDropRows();
        PluginHost.Game.RaiseTarget();
    }

    private bool SameDrops(IReadOnlyList<TargetQuestDrop> drops)
    {
        if (drops.Count != this.Drops.Count) return false;
        for (int i = 0; i < drops.Count; i++)
            if (drops[i] != this.Drops[i]) return false;
        return true;
    }

    internal void Sync(int shown)
    {
        if (shown == dropsFor || dropsFor < 0) return;
        dropsFor = -1;
        Drops = new List<TargetQuestDrop>();
        RebuildDropRows();
        PluginHost.Game.RaiseTarget();
    }

    internal void RebuildDropRows()
    {
        foreach (var child in dropRows.GetChildren()) child.QueueFree();
        foreach (var drop in Drops)
        {
            var row = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
            row.AddThemeConstantOverride("separation", 6);
            row.AddChild(new TextureRect
            {
                Texture = ItemIcons.Get(drop.ItemId),
                CustomMinimumSize = new Vector2(DropIconSize, DropIconSize),
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            });
            var line = HudStyle.Label(DropFont);
            line.Text = drop.Line(ItemData.DisplayName(drop.ItemId));
            row.AddChild(line);
            dropRows.AddChild(row);
        }
        dropRows.Visible = Drops.Count > 0;
    }

    internal IReadOnlyList<GameTargetDrop> ForPlugin()
    {
        var rows = new List<GameTargetDrop>(Drops.Count);
        foreach (var drop in Drops)
            rows.Add(new GameTargetDrop(ItemIcons.Get(drop.ItemId), drop.Line(ItemData.DisplayName(drop.ItemId))));
        return rows;
    }
}
