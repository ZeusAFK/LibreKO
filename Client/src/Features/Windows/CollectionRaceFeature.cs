using Godot;

namespace LibreKO.Features.Windows;

internal sealed class CollectionRaceFeature : IFeature
{
    private readonly IWorldContext context;
    private readonly CollectionRaceSession collectionRace;
    private readonly ICollectionRaceRequests requests;

    public CollectionRaceFeature(IWorldContext context, CollectionRaceSession collectionRace, ICollectionRaceRequests requests)
    {
        this.context = context;
        this.collectionRace = collectionRace;
        this.requests = requests;
    }

    private const int BodyWidth = 280;
    private const int ScreenMargin = 20;
    private const int Top = 140;
    private const byte CertainRate = 100;

    private CanvasLayer crLayer = null!;
    internal HudWindow CrWindow { get; private set; } = null!;
    private Label crCompletingLabel = null!;
    private Control crWinnersRow = null!;
    private Label crWinnersLabel = null!;
    private Label crTimerDigits = null!;
    private VBoxContainer crHuntBlock = null!;
    private VBoxContainer crHuntBox = null!;
    private VBoxContainer crCollectBlock = null!;
    private VBoxContainer crCollectBox = null!;
    private VBoxContainer crRewardsBlock = null!;
    private VBoxContainer crRewardsBox = null!;
    private VBoxContainer crCompleteBanner = null!;
    private Label crCompleteTitle = null!;
    private Godot.Timer crTimer = null!;

    private CollectionRaceState? crState;
    private int crRemainingSeconds;

    public void Build()
    {
        BuildWindow();

        crTimer = new Godot.Timer { WaitTime = 1.0f, Autostart = true };
        crTimer.Timeout += OnCrTimerTick;
        context.Root.AddChild(crTimer);

        collectionRace.CollectionRaceStateEvent += OnState;
        collectionRace.CollectionRaceProgressEvent += OnProgress;
        collectionRace.CollectionRaceCompletedEvent += OnCompleted;
        collectionRace.CollectionRaceCloseEvent += OnClose;
        collectionRace.CollectionRaceWinnersEvent += OnWinners;

        requests.CollectionRaceRequest();
    }

    internal void BuildWindow()
    {
        crLayer = new CanvasLayer { Layer = UiLayers.Windows };
        context.Root.AddChild(crLayer);

        var viewport = context.Root.GetViewport()?.GetVisibleRect().Size ?? Vector2.Zero;
        var pos = new Vector2(
            Math.Max(ScreenMargin, viewport.X - BodyWidth - 2 * ScreenMargin),
            Top);
        CrWindow = new HudWindow("collectionrace", "Collection Race", pos, bodyMinWidth: BodyWidth, minimizable: true, closable: false)
        {
            Visible = false
        };
        CrWindow.SetBackgroundAlpha(UiTheme.TranslucentWindowAlpha);
        crLayer.AddChild(CrWindow);

        var body = CrWindow.Body;
        body.AddThemeConstantOverride("separation", 6);

        var info = UiTheme.Section();
        var infoBox = new VBoxContainer();
        infoBox.AddThemeConstantOverride("separation", 8);
        var completing = UiKit.ValueRow("Completing", "0", UiTheme.TextLo);
        crCompletingLabel = completing.GetChild<Label>(completing.GetChildCount() - 1);
        infoBox.AddChild(completing);
        crWinnersRow = UiKit.ValueRow("Winners", "0 / 0", UiTheme.TextHi);
        crWinnersLabel = crWinnersRow.GetChild<Label>(crWinnersRow.GetChildCount() - 1);
        infoBox.AddChild(crWinnersRow);
        var time = UiKit.ValueRow("Event time", "00 : 00", UiTheme.GoldBright);
        crTimerDigits = time.GetChild<Label>(time.GetChildCount() - 1);
        infoBox.AddChild(time);
        info.AddChild(infoBox);
        body.AddChild(info);

        (crHuntBlock, crHuntBox) = CollectionRaceSection(body, QuestRows.ObjectiveHeading(true, false));
        (crCollectBlock, crCollectBox) = CollectionRaceSection(body, QuestRows.ObjectiveHeading(false, true));
        (crRewardsBlock, crRewardsBox) = CollectionRaceSection(body, "Rewards");

        crCompleteBanner = new VBoxContainer { Visible = false };
        crCompleteBanner.AddThemeConstantOverride("separation", 2);
        crCompleteTitle = UiTheme.Text("", 13, UiTheme.Good, HorizontalAlignment.Center);
        crCompleteTitle.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        crCompleteBanner.AddChild(crCompleteTitle);
        body.AddChild(crCompleteBanner);
    }

    private static (VBoxContainer Block, VBoxContainer Box) CollectionRaceSection(Control parent, string title)
    {
        var block = new VBoxContainer();
        block.AddThemeConstantOverride("separation", 6);
        parent.AddChild(block);
        return (block, QuestRows.Section(block, title));
    }

    public void Dispose()
    {
        collectionRace.CollectionRaceStateEvent -= OnState;
        collectionRace.CollectionRaceProgressEvent -= OnProgress;
        collectionRace.CollectionRaceCompletedEvent -= OnCompleted;
        collectionRace.CollectionRaceCloseEvent -= OnClose;
        collectionRace.CollectionRaceWinnersEvent -= OnWinners;

        if (GodotObject.IsInstanceValid(crTimer))
            crTimer.QueueFree();

        if (GodotObject.IsInstanceValid(CrWindow))
            CrWindow.QueueFree();
    }

    private void OnCrTimerTick()
    {
        if (crRemainingSeconds > 0)
        {
            crRemainingSeconds--;
            PaintTimer();
        }
        else if (crRemainingSeconds == 0 && CrWindow.Visible)
        {
            PaintTimer();
        }
    }

    private void PaintTimer() =>
        crTimerDigits.Text = $"{crRemainingSeconds / 60:D2} : {crRemainingSeconds % 60:D2}";

    private void OnState(CollectionRaceState state) => Show(state);

    internal void Show(CollectionRaceState state)
    {
        crState = state;
        crRemainingSeconds = state.RemainingSeconds;
        PaintTimer();

        CrWindow.Title = string.IsNullOrWhiteSpace(state.Name) ? "Collection Race" : state.Name;

        if (state.IsCompleted)
            PaintComplete("Collection Race complete!");
        else
        {
            crCompletingLabel.Text = "0";
            crCompleteBanner.Visible = false;
        }

        RenderObjectives();
        RenderRewards();
        PaintWinners();

        CrWindow.Visible = true;
    }

    private void OnWinners(int raceId, int winners, int maxWinners)
    {
        if (crState == null || crState.RaceId != raceId) return;
        crState.Winners = winners;
        crState.MaxWinners = maxWinners;
        PaintWinners();
    }

    private void PaintWinners()
    {
        bool limited = crState is { MaxWinners: > 0 };
        crWinnersRow.Visible = limited;
        if (limited)
            crWinnersLabel.Text = $"{crState!.Winners} / {crState.MaxWinners}";
    }

    private void OnProgress(int raceId, int[] currents)
    {
        if (crState == null || crState.RaceId != raceId) return;

        for (var i = 0; i < currents.Length && i < crState.Objectives.Count; i++)
            crState.Objectives[i].Current = currents[i];

        RenderObjectives();
    }

    private void OnCompleted(string message)
    {
        if (crState != null)
            crState.IsCompleted = true;

        PaintComplete(message);
        RenderObjectives();
    }

    private void PaintComplete(string message)
    {
        crCompletingLabel.Text = "1";
        crCompletingLabel.AddThemeColorOverride("font_color", UiTheme.Good);
        crCompleteTitle.Text = $"★ {message} ★";
        crCompleteBanner.Visible = true;
    }

    private void OnClose()
    {
        crState = null;
        CrWindow.Visible = false;
    }

    private void RenderObjectives()
    {
        UiKit.ClearChildren(crHuntBox);
        UiKit.ClearChildren(crCollectBox);
        crHuntBlock.Visible = false;
        crCollectBlock.Visible = false;

        if (crState == null) return;

        foreach (var objective in crState.Objectives)
        {
            if (objective.Count <= 0) continue;

            var value = $"{objective.Current} / {objective.Count}";
            var color = objective.Current >= objective.Count ? UiTheme.Good : UiTheme.GoldBright;
            if (objective.Kind == CollectionRaceObjectiveKind.Item)
            {
                crCollectBlock.Visible = true;
                crCollectBox.AddChild(QuestRows.ItemRow(context.Tooltips, objective.TargetId, ItemName(objective), value, color));
            }
            else
            {
                crHuntBlock.Visible = true;
                crHuntBox.AddChild(UiKit.ValueRow(TargetName(objective), value, color));
            }
        }
    }

    private static string TargetName(CollectionRaceObjective objective) =>
        !string.IsNullOrEmpty(objective.Name) ? objective.Name
        : objective.Kind == CollectionRaceObjectiveKind.EnemyPlayer ? "Enemy players"
        : $"Monster {objective.TargetId}";

    private static string ItemName(CollectionRaceObjective objective) =>
        ItemData.Get(objective.TargetId) != null ? ItemData.DisplayName(objective.TargetId)
    : string.IsNullOrEmpty(objective.Name) ? $"Item {objective.TargetId}" : objective.Name;

    private void RenderRewards()
    {
        UiKit.ClearChildren(crRewardsBox);

        crRewardsBlock.Visible = crState is { Rewards.Count: > 0 };
        if (crState == null) return;

        foreach (var r in crState.Rewards)
        {
            var value = r.Rate is > 0 and < CertainRate ? $"{r.ItemCount:n0}  ({r.Rate}%)" : $"{r.ItemCount:n0}";
            crRewardsBox.AddChild(QuestRows.ItemRow(context.Tooltips, r.ItemId, RewardName(r), value, UiTheme.GoldBright));
        }
    }

    private static string RewardName(CollectionRaceReward reward) =>
        !string.IsNullOrEmpty(reward.Name) && !QuestData.IsVirtualReward(reward.ItemId) && ItemData.Get(reward.ItemId) == null
            ? reward.Name
            : UiKit.QuestRewardName(reward.ItemId);
}
