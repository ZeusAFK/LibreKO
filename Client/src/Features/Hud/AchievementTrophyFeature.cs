using Godot;

namespace LibreKO.Features.Hud;

internal sealed class AchievementTrophyFeature : IFeature
{
    private readonly IWorldContext context;
    private readonly AchievementSession achievement;
    private readonly TopIconsFeature topIcons;

    private CanvasLayer trophyLayer = null!;
    private TopIconBadge badge = null!;

    public AchievementTrophyFeature(IWorldContext context, AchievementSession achievement, TopIconsFeature topIcons)
    {
        this.context = context;
        this.achievement = achievement;
        this.topIcons = topIcons;
    }

    internal event Action? Pressed;

    internal Button Trophy { get; private set; } = null!;

    public void Build()
    {
        BuildTrophy();
        achievement.AchievementListEvent += OnList;
        RefreshTrophy(Claimable());
    }

    public void Dispose()
    {
        achievement.AchievementListEvent -= OnList;
        badge?.StopBlink();
    }

    internal void BuildTrophy()
    {
        trophyLayer = new CanvasLayer { Layer = UiLayers.HudButtons };
        context.Root.AddChild(trophyLayer);

        Trophy = topIcons.Add(TopIconSlot.Trophy, "system/trophy", "Achievements",
            () => Pressed?.Invoke(), out var trophyIcon, trophyLayer);
        badge = new TopIconBadge(Trophy, trophyIcon, blinks: true);

        Trophy.Resized += PlaceTrophy;
        Callable.From(PlaceTrophy).CallDeferred();
    }

    private void PlaceTrophy() => topIcons.Place(TopIconSlot.Trophy, HudPlacement.AchievementTrophy);

    private void OnList(List<AchievementEntry> list) => RefreshTrophy(Claimable());

    private int Claimable()
    {
        int claimable = 0;
        foreach (var entry in achievement.Entries)
            if (entry.Claimable && AchievementData.Get(entry.Id) != null) claimable++;
        return claimable;
    }

    internal void RefreshTrophy(int claimable)
    {
        if (Trophy == null || !GodotObject.IsInstanceValid(Trophy)) return;

        badge.Show(claimable);
        Trophy.TooltipText = claimable > 0
            ? $"Achievements — {claimable} reward{(claimable == 1 ? "" : "s")} to claim"
            : "Achievements";
    }
}
