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
    private VBoxContainer _crHuntBox = null!;
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
        _crWindow = new HudWindow("collectionrace", "Collection Race", pos, bodyMinWidth: CollectionRaceBodyWidth, minimizable: true)
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
        var completing = QuestValueRow("Completing", "< 0 >", UiTheme.TextLo);
        _crCompletingLabel = completing.GetChild<Label>(completing.GetChildCount() - 1);
        infoBox.AddChild(completing);
        var time = QuestValueRow("Event time", "00 : 00", UiTheme.GoldBright);
        _crTimerDigits = time.GetChild<Label>(time.GetChildCount() - 1);
        infoBox.AddChild(time);
        info.AddChild(infoBox);
        body.AddChild(info);

        _crHuntBox = QuestSection(body, QuestObjectiveHeading(true, false));

        _crRewardsBlock = new VBoxContainer();
        _crRewardsBlock.AddThemeConstantOverride("separation", 6);
        body.AddChild(_crRewardsBlock);
        _crRewardsBox = QuestSection(_crRewardsBlock, "Rewards");

        _crCompleteBanner = new VBoxContainer { Visible = false };
        _crCompleteBanner.AddThemeConstantOverride("separation", 2);
        _crCompleteTitle = UiTheme.Text("", 13, UiTheme.Good, HorizontalAlignment.Center);
        _crCompleteTitle.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _crCompleteBanner.AddChild(_crCompleteTitle);
        _crCompleteBanner.AddChild(UiTheme.Text("Rewards claimed", 12, UiTheme.TextLo, HorizontalAlignment.Center));
        body.AddChild(_crCompleteBanner);
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

        _crWindow.Title = string.IsNullOrWhiteSpace(state.EventName) ? "Collection Race" : state.EventName;

        if (state.IsCompleted)
            PaintCollectionRaceComplete("Collection Race complete!");
        else
        {
            _crCompletingLabel.Text = "< 0 >";
            _crCompleteBanner.Visible = false;
        }

        RenderTargets();
        RenderRewards();

        _crWindow.Visible = true;
    }

    private void OnCollectionRaceProgress(int t1, int t2, int t3, int enemy)
    {
        if (_crState == null) return;

        _crState.Target1.CurrentCount = t1;
        _crState.Target2.CurrentCount = t2;
        _crState.Target3.CurrentCount = t3;
        _crState.EnemyCurrent = enemy;

        RenderTargets();
    }

    private void OnCollectionRaceCompleted(string message)
    {
        if (_crState != null)
            _crState.IsCompleted = true;

        PaintCollectionRaceComplete(message);
        RenderTargets();
    }

    private void PaintCollectionRaceComplete(string message)
    {
        _crCompletingLabel.Text = "< 1 >";
        _crCompletingLabel.AddThemeColorOverride("font_color", UiTheme.Good);
        _crCompleteTitle.Text = $"★ {message} ★";
        _crCompleteBanner.Visible = true;
    }

    private void OnCollectionRaceClose()
    {
        _crState = null;
        _crWindow.Visible = false;
    }

    private void RenderTargets()
    {
        foreach (var c in _crHuntBox.GetChildren())
        {
            _crHuntBox.RemoveChild(c);
            c.QueueFree();
        }

        if (_crState == null) return;

        void AddTarget(string name, int current, int max)
        {
            if (max <= 0) return;
            _crHuntBox.AddChild(QuestValueRow(name, $"{current} / {max}",
                current >= max ? UiTheme.Good : UiTheme.GoldBright));
        }

        AddTarget(CollectionRaceTargetName(_crState.Target1, 1), _crState.Target1.CurrentCount, _crState.Target1.TargetCount);
        AddTarget(CollectionRaceTargetName(_crState.Target2, 2), _crState.Target2.CurrentCount, _crState.Target2.TargetCount);
        AddTarget(CollectionRaceTargetName(_crState.Target3, 3), _crState.Target3.CurrentCount, _crState.Target3.TargetCount);
        AddTarget("Enemy players", _crState.EnemyCurrent, _crState.EnemyTarget);
    }

    private static string CollectionRaceTargetName(CollectionRaceTarget target, int index) =>
        string.IsNullOrEmpty(target.Name) ? $"Monster {index}" : target.Name;

    private void RenderRewards()
    {
        foreach (var c in _crRewardsBox.GetChildren())
        {
            _crRewardsBox.RemoveChild(c);
            c.QueueFree();
        }

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
