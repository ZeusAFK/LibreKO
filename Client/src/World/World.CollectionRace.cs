using System;
using System.Collections.Generic;
using Godot;
using LibreKO.Domain;
using LibreKO.Network;

namespace LibreKO;

public partial class World
{
    private static readonly Color CrTextCyan = new(0.40f, 0.85f, 0.95f);
    private static readonly Color CrTextGold = new(0.95f, 0.90f, 0.80f);
    private static readonly Color CrTextMuted = new(0.85f, 0.90f, 0.95f);
    private static readonly Color CrCardBg = new(0.04f, 0.04f, 0.05f, 0.94f);
    private static readonly Color CrBannerBg = new(0.15f, 0.12f, 0.09f, 0.96f);
    private static readonly Color CrGoldTint = new(1f, 0.88f, 0.35f);
    private static readonly Color CrExpTint = new(0.35f, 0.75f, 1f);
    private static readonly Color CrNpTint = new(0.95f, 0.45f, 0.35f);

    private CanvasLayer _crLayer = null!;
    private HudWindow _crWindow = null!;
    private Label _crCompletingLabel = null!;
    private Label _crTimerDigits = null!;
    private VBoxContainer _crTargetsBox = null!;
    private VBoxContainer _crRewardsBox = null!;
    private Label _crCompleteBanner = null!;
    private Godot.Timer _crTimer = null!;

    private CollectionRaceState? _crState;
    private int _crRemainingSeconds;

    private void CollectionRaceInit()
    {
        _crLayer = new CanvasLayer { Layer = 74 };
        AddChild(_crLayer);

        var pos = new Vector2(Math.Max(10, GetViewport().GetVisibleRect().Size.X - 260), 140);
        _crWindow = new HudWindow("collectionrace", "Collection Race", pos, bodyMinWidth: 240, minimizable: true)
        {
            Visible = false
        };
        _crLayer.AddChild(_crWindow);

        var body = _crWindow.Body;
        body.AddThemeConstantOverride("separation", 3);

        var infoPanel = new PanelContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        infoPanel.AddThemeStyleboxOverride("panel", CreateBronzeBox(CrCardBg, 1, 2));
        var infoVBox = new VBoxContainer();
        infoVBox.AddThemeConstantOverride("separation", 2);

        var compRow = new HBoxContainer();
        var compTitle = new Label { Text = "Completing :", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        compTitle.AddThemeColorOverride("font_color", CrTextCyan);
        compTitle.AddThemeFontSizeOverride("font_size", 11);
        _crCompletingLabel = new Label { Text = "< 0 >", HorizontalAlignment = HorizontalAlignment.Right };
        _crCompletingLabel.AddThemeColorOverride("font_color", CrTextCyan);
        _crCompletingLabel.AddThemeFontSizeOverride("font_size", 11);
        compRow.AddChild(compTitle);
        compRow.AddChild(_crCompletingLabel);
        infoVBox.AddChild(compRow);

        var timeRow = new HBoxContainer();
        var timeTitle = new Label { Text = "Event Time :", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        timeTitle.AddThemeColorOverride("font_color", CrTextCyan);
        timeTitle.AddThemeFontSizeOverride("font_size", 11);
        _crTimerDigits = new Label { Text = "00 : 00", HorizontalAlignment = HorizontalAlignment.Right };
        _crTimerDigits.AddThemeColorOverride("font_color", new Color(0.35f, 0.95f, 0.95f));
        _crTimerDigits.AddThemeFontSizeOverride("font_size", 12);
        timeRow.AddChild(timeTitle);
        timeRow.AddChild(_crTimerDigits);
        infoVBox.AddChild(timeRow);

        infoPanel.AddChild(infoVBox);
        body.AddChild(infoPanel);

        _crTargetsBox = new VBoxContainer();
        _crTargetsBox.AddThemeConstantOverride("separation", 3);
        body.AddChild(_crTargetsBox);

        var rewardBanner = new PanelContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        rewardBanner.AddThemeStyleboxOverride("panel", CreateRewardBannerBox());
        var bannerLabel = new Label
        {
            Text = "Reward of Winner",
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        bannerLabel.AddThemeColorOverride("font_color", CrTextGold);
        bannerLabel.AddThemeFontSizeOverride("font_size", 11);
        rewardBanner.AddChild(bannerLabel);
        body.AddChild(rewardBanner);

        _crRewardsBox = new VBoxContainer();
        _crRewardsBox.AddThemeConstantOverride("separation", 2);
        body.AddChild(_crRewardsBox);

        _crCompleteBanner = new Label
        {
            Visible = false,
            Text = "★ Completed! ★",
            HorizontalAlignment = HorizontalAlignment.Center,
            AutowrapMode = TextServer.AutowrapMode.WordSmart
        };
        _crCompleteBanner.AddThemeColorOverride("font_color", new Color(0.40f, 0.95f, 0.40f));
        _crCompleteBanner.AddThemeFontSizeOverride("font_size", 12);
        body.AddChild(_crCompleteBanner);

        _crTimer = new Godot.Timer { WaitTime = 1.0f, Autostart = true };
        _crTimer.Timeout += OnCrTimerTick;
        AddChild(_crTimer);

        Net.I.CollectionRaceStateEvent += OnCollectionRaceState;
        Net.I.CollectionRaceProgressEvent += OnCollectionRaceProgress;
        Net.I.CollectionRaceCompletedEvent += OnCollectionRaceCompleted;
        Net.I.CollectionRaceCloseEvent += OnCollectionRaceClose;

        Net.I.SendCollectionRaceRequest();
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
            int m = _crRemainingSeconds / 60;
            int s = _crRemainingSeconds % 60;
            _crTimerDigits.Text = $"{m:D2} : {s:D2}";
        }
        else if (_crRemainingSeconds == 0 && _crWindow.Visible)
        {
            _crTimerDigits.Text = "00 : 00";
        }
    }

    private void OnCollectionRaceState(CollectionRaceState state)
    {
        _crState = state;
        _crRemainingSeconds = state.RemainingSeconds;
        int m = _crRemainingSeconds / 60;
        int s = _crRemainingSeconds % 60;
        _crTimerDigits.Text = $"{m:D2} : {s:D2}";

        string baseName = string.IsNullOrWhiteSpace(state.EventName) ? "Collection Race" : state.EventName;
        _crWindow.Title = baseName;

        _crCompletingLabel.Text = state.IsCompleted ? "< 1 >" : "< 0 >";
        if (state.IsCompleted)
        {
            _crCompleteBanner.Text = "★ Completed! Rewards Claimed! ★";
            _crCompleteBanner.Visible = true;
        }
        else
        {
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

        _crCompletingLabel.Text = "< 1 >";
        _crCompleteBanner.Text = $"★ {message} ★";
        _crCompleteBanner.Visible = true;
        RenderTargets();
    }

    private void OnCollectionRaceClose()
    {
        _crState = null;
        _crWindow.Visible = false;
    }

    private void RenderTargets()
    {
        foreach (var c in _crTargetsBox.GetChildren())
            c.QueueFree();

        if (_crState == null) return;

        void AddTargetCard(string targetName, int current, int max, bool isPvP = false)
        {
            if (max <= 0) return;

            var card = new HBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
            card.AddThemeConstantOverride("separation", 3);

            var iconFrame = new PanelContainer
            {
                CustomMinimumSize = new Vector2(44, 44),
                SizeFlagsVertical = Control.SizeFlags.ShrinkCenter
            };
            iconFrame.AddThemeStyleboxOverride("panel", CreateSlotBox());

            var iconTex = UiIcons.Get("system/combat-attack");
            var iconRect = new TextureRect
            {
                Texture = iconTex,
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                CustomMinimumSize = new Vector2(36, 36),
                SelfModulate = isPvP ? new Color(1f, 0.28f, 0.25f) : new Color(1f, 0.50f, 0.20f)
            };
            iconRect.SetAnchorsPreset(Control.LayoutPreset.FullRect);
            var center = new CenterContainer();
            center.AddChild(iconRect);
            iconFrame.AddChild(center);
            card.AddChild(iconFrame);

            var rightVBox = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
            rightVBox.AddThemeConstantOverride("separation", 2);

            var namePanel = new PanelContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
            namePanel.AddThemeStyleboxOverride("panel", CreateBronzeBox(CrCardBg, 1, 2));
            bool isDone = current >= max;
            var nameLabel = new Label
            {
                Text = targetName,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            nameLabel.AddThemeColorOverride("font_color", isDone ? new Color(0.40f, 0.95f, 0.40f) : new Color(0.45f, 0.92f, 0.45f));
            nameLabel.AddThemeFontSizeOverride("font_size", 12);
            namePanel.AddChild(nameLabel);
            rightVBox.AddChild(namePanel);

            var progressPanel = new PanelContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
            progressPanel.AddThemeStyleboxOverride("panel", CreateBronzeBox(CrCardBg, 1, 2));
            var progressLabel = new Label
            {
                Text = isDone ? $"[✓] {current} / {max}" : $"{current} / {max}",
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            progressLabel.AddThemeColorOverride("font_color", isDone ? new Color(0.40f, 0.95f, 0.40f) : Colors.White);
            progressLabel.AddThemeFontSizeOverride("font_size", 12);
            progressPanel.AddChild(progressLabel);
            rightVBox.AddChild(progressPanel);

            card.AddChild(rightVBox);
            _crTargetsBox.AddChild(card);
        }

        if (_crState.Target1.TargetCount > 0)
            AddTargetCard(string.IsNullOrEmpty(_crState.Target1.Name) ? "Monster 1" : _crState.Target1.Name, _crState.Target1.CurrentCount, _crState.Target1.TargetCount);

        if (_crState.Target2.TargetCount > 0)
            AddTargetCard(string.IsNullOrEmpty(_crState.Target2.Name) ? "Monster 2" : _crState.Target2.Name, _crState.Target2.CurrentCount, _crState.Target2.TargetCount);

        if (_crState.Target3.TargetCount > 0)
            AddTargetCard(string.IsNullOrEmpty(_crState.Target3.Name) ? "Monster 3" : _crState.Target3.Name, _crState.Target3.CurrentCount, _crState.Target3.TargetCount);

        if (_crState.EnemyTarget > 0)
            AddTargetCard("Enemy Players", _crState.EnemyCurrent, _crState.EnemyTarget, isPvP: true);
    }

    private void RenderRewards()
    {
        foreach (var c in _crRewardsBox.GetChildren())
            c.QueueFree();

        if (_crState == null || _crState.Rewards.Count == 0) return;

        foreach (var r in _crState.Rewards)
        {
            var rowPanel = new PanelContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
            rowPanel.AddThemeStyleboxOverride("panel", CreateBronzeBox(CrCardBg, 1, 2));

            var rowHBox = new HBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
            rowHBox.AddThemeConstantOverride("separation", 6);

            var slot = new PanelContainer
            {
                CustomMinimumSize = new Vector2(38, 38),
                SizeFlagsVertical = Control.SizeFlags.ShrinkCenter
            };
            slot.AddThemeStyleboxOverride("panel", CreateSlotBox());

            var iconTex = GetRewardIcon(r.ItemId);
            var iconRect = new TextureRect
            {
                Texture = iconTex,
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                CustomMinimumSize = new Vector2(32, 32)
            };
            if (r.ItemId == QuestData.CoinItemId)
                iconRect.SelfModulate = CrGoldTint;
            else if (r.ItemId == QuestData.ExpItemId)
                iconRect.SelfModulate = CrExpTint;
            else if (r.ItemId == QuestData.LadderPointItemId)
                iconRect.SelfModulate = CrNpTint;

            var slotCenter = new CenterContainer();
            slotCenter.AddChild(iconRect);
            slot.AddChild(slotCenter);
            rowHBox.AddChild(slot);

            var middleVBox = new VBoxContainer
            {
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                SizeFlagsVertical = Control.SizeFlags.ShrinkCenter
            };
            middleVBox.AddThemeConstantOverride("separation", 1);

            string displayName = GetRewardDisplayName(r.ItemId, r.Name);
            var nameLabel = new Label { Text = displayName };
            nameLabel.AddThemeColorOverride("font_color", Colors.White);
            nameLabel.AddThemeFontSizeOverride("font_size", 11);
            middleVBox.AddChild(nameLabel);

            var countLabel = new Label { Text = $"{r.ItemCount:n0}" };
            countLabel.AddThemeColorOverride("font_color", CrTextMuted);
            countLabel.AddThemeFontSizeOverride("font_size", 11);
            middleVBox.AddChild(countLabel);

            rowHBox.AddChild(middleVBox);

            var rateLabel = new Label
            {
                Text = $"{r.Rate}%",
                HorizontalAlignment = HorizontalAlignment.Right,
                SizeFlagsVertical = Control.SizeFlags.ShrinkCenter
            };
            rateLabel.AddThemeColorOverride("font_color", CrTextMuted);
            rateLabel.AddThemeFontSizeOverride("font_size", 11);
            rowHBox.AddChild(rateLabel);

            rowPanel.AddChild(rowHBox);
            _crRewardsBox.AddChild(rowPanel);
        }
    }

    private static string GetRewardDisplayName(int itemId, string fallbackName)
    {
        if (itemId == QuestData.CoinItemId) return "Noah";
        if (itemId == QuestData.ExpItemId) return "EXP";
        if (itemId == QuestData.LadderPointItemId) return "National Points";
        return string.IsNullOrEmpty(fallbackName) ? ItemData.DisplayName(itemId) : fallbackName;
    }

    private static Texture2D? GetRewardIcon(int itemId)
    {
        if (itemId == QuestData.CoinItemId)
            return UiIcons.Get("system/coins");
        if (itemId == QuestData.ExpItemId)
            return UiIcons.Get("system/sparkle");
        if (itemId == QuestData.LadderPointItemId)
            return UiIcons.Get("system/medal");
        return ItemData.Icon(itemId);
    }

    private static StyleBoxFlat CreateBronzeBox(Color bg, int borderWidth = 1, int corner = 2)
    {
        return new StyleBoxFlat
        {
            BgColor = bg,
            BorderColor = new Color(0.72f, 0.52f, 0.28f),
            BorderWidthBottom = borderWidth,
            BorderWidthTop = borderWidth,
            BorderWidthLeft = borderWidth,
            BorderWidthRight = borderWidth,
            CornerRadiusBottomLeft = corner,
            CornerRadiusBottomRight = corner,
            CornerRadiusTopLeft = corner,
            CornerRadiusTopRight = corner,
            ContentMarginBottom = 3,
            ContentMarginTop = 3,
            ContentMarginLeft = 5,
            ContentMarginRight = 5
        };
    }

    private static StyleBoxFlat CreateRewardBannerBox()
    {
        return new StyleBoxFlat
        {
            BgColor = CrBannerBg,
            BorderColor = new Color(0.72f, 0.52f, 0.28f),
            BorderWidthBottom = 1,
            BorderWidthTop = 1,
            BorderWidthLeft = 1,
            BorderWidthRight = 1,
            CornerRadiusBottomLeft = 2,
            CornerRadiusBottomRight = 2,
            CornerRadiusTopLeft = 2,
            CornerRadiusTopRight = 2,
            ContentMarginBottom = 2,
            ContentMarginTop = 2,
            ContentMarginLeft = 4,
            ContentMarginRight = 4
        };
    }

    private static StyleBoxFlat CreateSlotBox()
    {
        return new StyleBoxFlat
        {
            BgColor = new Color(0.04f, 0.04f, 0.05f, 1f),
            BorderColor = new Color(0.55f, 0.42f, 0.25f),
            BorderWidthBottom = 1,
            BorderWidthTop = 1,
            BorderWidthLeft = 1,
            BorderWidthRight = 1,
            CornerRadiusBottomLeft = 2,
            CornerRadiusBottomRight = 2,
            CornerRadiusTopLeft = 2,
            CornerRadiusTopRight = 2
        };
    }
}
