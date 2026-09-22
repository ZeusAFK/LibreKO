using System;
using Godot;
using LibreKO.Domain;
using LibreKO.Network;

namespace LibreKO;

public partial class World
{
    private const int CollectionRaceBodyWidth = 280;
    private const int CollectionRaceScreenMargin = 20;
    private const int CollectionRaceTop = 140;
    private const byte CollectionRaceCertainRate = 100;

    private CanvasLayer _crLayer = null!;
    private HudWindow _crWindow = null!;
    private Label _crCompletingLabel = null!;
    private Label _crTimerDigits = null!;
    private VBoxContainer _crHuntBlock = null!;
    private VBoxContainer _crHuntBox = null!;
    private VBoxContainer _crCollectBlock = null!;
    private VBoxContainer _crCollectBox = null!;
    private VBoxContainer _crRewardsBlock = null!;
    private VBoxContainer _crRewardsBox = null!;
    private VBoxContainer _crCompleteBanner = null!;
    private Label _crCompleteTitle = null!;
    private Godot.Timer _crTimer = null!;

    private CollectionRaceState? _crState;
    private int _crRemainingSeconds;

    private void CollectionRaceInit()
    {
        BuildCollectionRaceWindow();

        _crTimer = new Godot.Timer { WaitTime = 1.0f, Autostart = true };
        _crTimer.Timeout += OnCrTimerTick;
        AddChild(_crTimer);

        Net.I.CollectionRaceStateEvent += OnCollectionRaceState;
        Net.I.CollectionRaceProgressEvent += OnCollectionRaceProgress;
        Net.I.CollectionRaceCompletedEvent += OnCollectionRaceCompleted;
        Net.I.CollectionRaceCloseEvent += OnCollectionRaceClose;

        Net.I.SendCollectionRaceRequest();
    }

    private void BuildCollectionRaceWindow()
    {
        _crLayer = new CanvasLayer { Layer = 74 };
        AddChild(_crLayer);

        var viewport = GetViewport()?.GetVisibleRect().Size ?? Vector2.Zero;
        var pos = new Vector2(
            Math.Max(CollectionRaceScreenMargin, viewport.X - CollectionRaceBodyWidth - 2 * CollectionRaceScreenMargin),
            CollectionRaceTop);
        _crWindow = new HudWindow("collectionrace", "Collection Race", pos, bodyMinWidth: CollectionRaceBodyWidth, minimizable: true, closable: false)
        {
            Visible = false
        };
        _crWindow.SetBackgroundAlpha(UiTheme.TranslucentWindowAlpha);
        _crLayer.AddChild(_crWindow);

        var body = _crWindow.Body;
        body.AddThemeConstantOverride("separation", 6);

        var info = UiTheme.Section();
        var infoBox = new VBoxContainer();
        infoBox.AddThemeConstantOverride("separation", 8);
        var completing = QuestValueRow("Completing", "0", UiTheme.TextLo);
        _crCompletingLabel = completing.GetChild<Label>(completing.GetChildCount() - 1);
        infoBox.AddChild(completing);
        var time = QuestValueRow("Event time", "00 : 00", UiTheme.GoldBright);
        _crTimerDigits = time.GetChild<Label>(time.GetChildCount() - 1);
        infoBox.AddChild(time);
        info.AddChild(infoBox);
        body.AddChild(info);

        (_crHuntBlock, _crHuntBox) = CollectionRaceSection(body, QuestObjectiveHeading(true, false));
        (_crCollectBlock, _crCollectBox) = CollectionRaceSection(body, QuestObjectiveHeading(false, true));
        (_crRewardsBlock, _crRewardsBox) = CollectionRaceSection(body, "Rewards");

        _crCompleteBanner = new VBoxContainer { Visible = false };
        _crCompleteBanner.AddThemeConstantOverride("separation", 2);
        _crCompleteTitle = UiTheme.Text("", 13, UiTheme.Good, HorizontalAlignment.Center);
        _crCompleteTitle.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _crCompleteBanner.AddChild(_crCompleteTitle);
        body.AddChild(_crCompleteBanner);
    }

    private static (VBoxContainer Block, VBoxContainer Box) CollectionRaceSection(Control parent, string title)
    {
        var block = new VBoxContainer();
        block.AddThemeConstantOverride("separation", 6);
        parent.AddChild(block);
        return (block, QuestSection(block, title));
    }

    private void CollectionRaceDispose()
    {
        Net.I.CollectionRaceStateEvent -= OnCollectionRaceState;
        Net.I.CollectionRaceProgressEvent -= OnCollectionRaceProgress;
        Net.I.CollectionRaceCompletedEvent -= OnCollectionRaceCompleted;
        Net.I.CollectionRaceCloseEvent -= OnCollectionRaceClose;

        if (IsInstanceValid(_crTimer))
            _crTimer.QueueFree();

        if (IsInstanceValid(_crWindow))
            _crWindow.QueueFree();
    }

    private void OnCrTimerTick()
    {
        if (_crRemainingSeconds > 0)
        {
            _crRemainingSeconds--;
            PaintCollectionRaceTimer();
        }
        else if (_crRemainingSeconds == 0 && _crWindow.Visible)
        {
            PaintCollectionRaceTimer();
        }
    }

    private void PaintCollectionRaceTimer() =>
        _crTimerDigits.Text = $"{_crRemainingSeconds / 60:D2} : {_crRemainingSeconds % 60:D2}";

    private void OnCollectionRaceState(CollectionRaceState state) => ShowCollectionRace(state);

    private void ShowCollectionRace(CollectionRaceState state)
    {
        _crState = state;
        _crRemainingSeconds = state.RemainingSeconds;
        PaintCollectionRaceTimer();

        _crWindow.Title = string.IsNullOrWhiteSpace(state.Name) ? "Collection Race" : state.Name;

        if (state.IsCompleted)
            PaintCollectionRaceComplete("Collection Race complete!");
        else
        {
            _crCompletingLabel.Text = "0";
            _crCompleteBanner.Visible = false;
        }

        RenderObjectives();
        RenderRewards();

        _crWindow.Visible = true;
    }

    private void OnCollectionRaceProgress(int raceId, int[] currents)
    {
        if (_crState == null || _crState.RaceId != raceId) return;

        for (var i = 0; i < currents.Length && i < _crState.Objectives.Count; i++)
            _crState.Objectives[i].Current = currents[i];

        RenderObjectives();
    }

    private void OnCollectionRaceCompleted(string message)
    {
        if (_crState != null)
            _crState.IsCompleted = true;

        PaintCollectionRaceComplete(message);
        RenderObjectives();
    }

    private void PaintCollectionRaceComplete(string message)
    {
        _crCompletingLabel.Text = "1";
        _crCompletingLabel.AddThemeColorOverride("font_color", UiTheme.Good);
        _crCompleteTitle.Text = $"★ {message} ★";
        _crCompleteBanner.Visible = true;
    }

    private void OnCollectionRaceClose()
    {
        _crState = null;
        _crWindow.Visible = false;
    }

    private void RenderObjectives()
    {
        ClearChildren(_crHuntBox);
        ClearChildren(_crCollectBox);
        _crHuntBlock.Visible = false;
        _crCollectBlock.Visible = false;

        if (_crState == null) return;

        foreach (var objective in _crState.Objectives)
        {
            if (objective.Count <= 0) continue;

            var value = $"{objective.Current} / {objective.Count}";
            var color = objective.Current >= objective.Count ? UiTheme.Good : UiTheme.GoldBright;
            if (objective.Kind == CollectionRaceObjectiveKind.Item)
            {
                _crCollectBlock.Visible = true;
                _crCollectBox.AddChild(QuestItemRow(objective.TargetId, CollectionRaceItemName(objective), value, color));
            }
            else
            {
                _crHuntBlock.Visible = true;
                _crHuntBox.AddChild(QuestValueRow(CollectionRaceTargetName(objective), value, color));
            }
        }
    }

    private static void ClearChildren(Node parent)
    {
        foreach (var c in parent.GetChildren())
        {
            parent.RemoveChild(c);
            c.QueueFree();
        }
    }

    private static string CollectionRaceTargetName(CollectionRaceObjective objective) =>
        !string.IsNullOrEmpty(objective.Name) ? objective.Name
        : objective.Kind == CollectionRaceObjectiveKind.EnemyPlayer ? "Enemy players"
        : $"Monster {objective.TargetId}";

    private static string CollectionRaceItemName(CollectionRaceObjective objective) =>
        ItemData.Get(objective.TargetId) != null ? ItemData.DisplayName(objective.TargetId)
        : string.IsNullOrEmpty(objective.Name) ? $"Item {objective.TargetId}" : objective.Name;

    private void RenderRewards()
    {
        ClearChildren(_crRewardsBox);

        _crRewardsBlock.Visible = _crState is { Rewards.Count: > 0 };
        if (_crState == null) return;

        foreach (var r in _crState.Rewards)
        {
            var value = r.Rate is > 0 and < CollectionRaceCertainRate ? $"{r.ItemCount:n0}  ({r.Rate}%)" : $"{r.ItemCount:n0}";
            _crRewardsBox.AddChild(QuestItemRow(r.ItemId, CollectionRaceRewardName(r), value, UiTheme.GoldBright));
        }
    }

    private static string CollectionRaceRewardName(CollectionRaceReward reward) =>
        !string.IsNullOrEmpty(reward.Name) && !QuestData.IsVirtualReward(reward.ItemId) && ItemData.Get(reward.ItemId) == null
            ? reward.Name
            : QuestRewardName(reward.ItemId);
}
