using System;
using System.Collections.Generic;
using System.Globalization;
using Godot;
using LibreKO.Domain;
using LibreKO.Network;

namespace LibreKO;

public partial class World
{
    private CanvasLayer _crLayer = null!;
    private PanelContainer _crRoot = null!;
    private VBoxContainer _crMainVBox = null!;
    private HBoxContainer _crHeader = null!;
    private Label _crTitleLabel = null!;
    private Button _crCollapseBtn = null!;

    private VBoxContainer _crBody = null!;
    private Label _crCompletingLabel = null!;
    private Label _crTimerDigits = null!;
    private VBoxContainer _crTargetsBox = null!;
    private VBoxContainer _crRewardsBox = null!;
    private Label _crCompleteBanner = null!;
    private Godot.Timer _crTimer = null!;

    private CollectionRaceState? _crState;
    private int _crRemainingSeconds;
    private bool _isCollapsed;
    private bool _isDragging;
    private Vector2 _dragOffset;

    // Cache generated icon textures for currencies
    private static ImageTexture? _expIconTex;
    private static ImageTexture? _npIconTex;

    private void CollectionRaceInit()
    {
        _crLayer = new CanvasLayer { Layer = 74 };
        AddChild(_crLayer);

        _crRoot = new PanelContainer
        {
            Visible = false,
            CustomMinimumSize = new Vector2(240, 0),
            Position = new Vector2(Math.Max(10, GetViewport().GetVisibleRect().Size.X - 260), 140)
        };
        _crRoot.AddThemeStyleboxOverride("panel", CreateEmptyStyle());
        _crLayer.AddChild(_crRoot);

        _crMainVBox = new VBoxContainer();
        _crMainVBox.AddThemeConstantOverride("separation", 3);
        _crRoot.AddChild(_crMainVBox);

        // 1. Header Bar: Colony Zone CR [Tekrarli]  [ ^ ]
        _crHeader = new HBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        var headerPanel = new PanelContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        headerPanel.AddThemeStyleboxOverride("panel", CreateHeaderBox());

        _crTitleLabel = new Label
        {
            Text = "Collection Race",
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        _crTitleLabel.AddThemeColorOverride("font_color", new Color(0.95f, 0.92f, 0.85f));
        _crTitleLabel.AddThemeFontSizeOverride("font_size", 12);
        _crHeader.AddChild(_crTitleLabel);

        _crCollapseBtn = new Button
        {
            Text = "▲",
            CustomMinimumSize = new Vector2(22, 22),
            FocusMode = Control.FocusModeEnum.None
        };
        _crCollapseBtn.AddThemeStyleboxOverride("normal", CreateBronzeBox(new Color(0.20f, 0.12f, 0.08f, 0.95f), 1, 2));
        _crCollapseBtn.AddThemeStyleboxOverride("hover", CreateBronzeBox(new Color(0.30f, 0.18f, 0.10f, 0.95f), 1, 2));
        _crCollapseBtn.AddThemeStyleboxOverride("pressed", CreateBronzeBox(new Color(0.15f, 0.08f, 0.05f, 0.95f), 1, 2));
        _crCollapseBtn.AddThemeColorOverride("font_color", new Color(0.95f, 0.80f, 0.50f));
        _crCollapseBtn.AddThemeFontSizeOverride("font_size", 11);
        _crCollapseBtn.Pressed += ToggleCollapse;
        _crHeader.AddChild(_crCollapseBtn);

        headerPanel.AddChild(_crHeader);
        headerPanel.GuiInput += OnHeaderGuiInput;
        _crMainVBox.AddChild(headerPanel);

        // 2. Collapsible Body
        _crBody = new VBoxContainer();
        _crBody.AddThemeConstantOverride("separation", 3);
        _crMainVBox.AddChild(_crBody);

        // Sub-panel: Completing & Event Time
        var infoPanel = new PanelContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        infoPanel.AddThemeStyleboxOverride("panel", CreateBronzeBox(new Color(0.04f, 0.04f, 0.05f, 0.94f), 1, 2));
        var infoVBox = new VBoxContainer();
        infoVBox.AddThemeConstantOverride("separation", 2);

        var compRow = new HBoxContainer();
        var compTitle = new Label { Text = "Completing :", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        compTitle.AddThemeColorOverride("font_color", new Color(0.40f, 0.85f, 0.95f));
        compTitle.AddThemeFontSizeOverride("font_size", 11);
        _crCompletingLabel = new Label { Text = "< 0 >", HorizontalAlignment = HorizontalAlignment.Right };
        _crCompletingLabel.AddThemeColorOverride("font_color", new Color(0.40f, 0.85f, 0.95f));
        _crCompletingLabel.AddThemeFontSizeOverride("font_size", 11);
        compRow.AddChild(compTitle);
        compRow.AddChild(_crCompletingLabel);
        infoVBox.AddChild(compRow);

        var timeRow = new HBoxContainer();
        var timeTitle = new Label { Text = "Event Time :", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        timeTitle.AddThemeColorOverride("font_color", new Color(0.40f, 0.85f, 0.95f));
        timeTitle.AddThemeFontSizeOverride("font_size", 11);
        _crTimerDigits = new Label { Text = "00 : 00", HorizontalAlignment = HorizontalAlignment.Right };
        _crTimerDigits.AddThemeColorOverride("font_color", new Color(0.35f, 0.95f, 0.95f));
        _crTimerDigits.AddThemeFontSizeOverride("font_size", 12);
        timeRow.AddChild(timeTitle);
        timeRow.AddChild(_crTimerDigits);
        infoVBox.AddChild(timeRow);

        infoPanel.AddChild(infoVBox);
        _crBody.AddChild(infoPanel);

        // 3. Targets List
        _crTargetsBox = new VBoxContainer();
        _crTargetsBox.AddThemeConstantOverride("separation", 3);
        _crBody.AddChild(_crTargetsBox);

        // 4. Reward of Winner Header
        var rewardBanner = new PanelContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        rewardBanner.AddThemeStyleboxOverride("panel", CreateRewardBannerBox());
        var bannerLabel = new Label
        {
            Text = "Reward of Winner",
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        bannerLabel.AddThemeColorOverride("font_color", new Color(0.95f, 0.90f, 0.80f));
        bannerLabel.AddThemeFontSizeOverride("font_size", 11);
        rewardBanner.AddChild(bannerLabel);
        _crBody.AddChild(rewardBanner);

        // 5. Rewards List
        _crRewardsBox = new VBoxContainer();
        _crRewardsBox.AddThemeConstantOverride("separation", 2);
        _crBody.AddChild(_crRewardsBox);

        // 6. Complete status banner
        _crCompleteBanner = new Label
        {
            Visible = false,
            Text = "★ Completed! ★",
            HorizontalAlignment = HorizontalAlignment.Center,
            AutowrapMode = TextServer.AutowrapMode.WordSmart
        };
        _crCompleteBanner.AddThemeColorOverride("font_color", new Color(0.40f, 0.95f, 0.40f));
        _crCompleteBanner.AddThemeFontSizeOverride("font_size", 12);
        _crBody.AddChild(_crCompleteBanner);

        // Timer
        _crTimer = new Godot.Timer { WaitTime = 1.0f, Autostart = true };
        _crTimer.Timeout += OnCrTimerTick;
        AddChild(_crTimer);

        // Net Event Listeners
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
    }

    private void ToggleCollapse()
    {
        _isCollapsed = !_isCollapsed;
        _crBody.Visible = !_isCollapsed;
        _crCollapseBtn.Text = _isCollapsed ? "▼" : "▲";
    }

    private void OnHeaderGuiInput(InputEvent @event)
    {
        if (@event is InputEventMouseButton mb)
        {
            if (mb.ButtonIndex == MouseButton.Left)
            {
                if (mb.Pressed)
                {
                    _isDragging = true;
                    _dragOffset = _crRoot.GetGlobalMousePosition() - _crRoot.Position;
                }
                else
                {
                    _isDragging = false;
                }
            }
        }
        else if (@event is InputEventMouseMotion && _isDragging)
        {
            _crRoot.Position = _crRoot.GetGlobalMousePosition() - _dragOffset;
        }
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
        else if (_crRemainingSeconds == 0 && _crRoot.Visible)
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
        _crTitleLabel.Text = $"{baseName} [Tekrarli]";

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

        _crRoot.Visible = true;
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
        _crRoot.Visible = false;
    }

    // Render Target cards (Exact match to Image 2: Square icon on left, Name box + Progress box on right)
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

            // Left: Square Icon (44x44) with bronze border
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

            // Right: VBox with 2 framed boxes: [Target Name] and [Current / Max]
            var rightVBox = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
            rightVBox.AddThemeConstantOverride("separation", 2);

            // Top box: Target Name
            var namePanel = new PanelContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
            namePanel.AddThemeStyleboxOverride("panel", CreateBronzeBox(new Color(0.04f, 0.04f, 0.05f, 0.94f), 1, 2));
            bool isDone = current >= max;
            var nameLabel = new Label
            {
                Text = targetName,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            nameLabel.AddThemeColorOverride("font_color", isDone ? new Color(0.40f, 0.95f, 0.40f) : new Color(0.45f, 0.92f, 0.45f)); // Vivid KO green
            nameLabel.AddThemeFontSizeOverride("font_size", 12);
            namePanel.AddChild(nameLabel);
            rightVBox.AddChild(namePanel);

            // Bottom box: Progress
            var progressPanel = new PanelContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
            progressPanel.AddThemeStyleboxOverride("panel", CreateBronzeBox(new Color(0.04f, 0.04f, 0.05f, 0.94f), 1, 2));
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

    // Render Reward rows (Exact match to Image 2: Icon on left, Name & Amount in middle, Rate on right)
    private void RenderRewards()
    {
        foreach (var c in _crRewardsBox.GetChildren())
            c.QueueFree();

        if (_crState == null || _crState.Rewards.Count == 0) return;

        foreach (var r in _crState.Rewards)
        {
            var rowPanel = new PanelContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
            rowPanel.AddThemeStyleboxOverride("panel", CreateBronzeBox(new Color(0.04f, 0.04f, 0.05f, 0.92f), 1, 2));

            var rowHBox = new HBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
            rowHBox.AddThemeConstantOverride("separation", 6);

            // Left: Icon slot (38x38)
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
            if (r.ItemId == 900000000)
                iconRect.SelfModulate = new Color(1f, 0.88f, 0.35f); // Gold tint

            var slotCenter = new CenterContainer();
            slotCenter.AddChild(iconRect);

            // For EXP, overlay small white "EXP" label at the bottom of the slot just like Image 2
            if (r.ItemId == 900001000)
            {
                var expBadge = new Label
                {
                    Text = "EXP",
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Bottom
                };
                expBadge.AddThemeFontSizeOverride("font_size", 9);
                expBadge.AddThemeColorOverride("font_color", Colors.White);
                expBadge.AddThemeColorOverride("font_shadow_color", Colors.Black);
                expBadge.SetAnchorsPreset(Control.LayoutPreset.FullRect);
                slot.AddChild(expBadge);
            }

            slot.AddChild(slotCenter);
            rowHBox.AddChild(slot);

            // Middle: Name and Count formatted with dot separators (e.g. 25.000.000)
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

            // Use dot notation like Image 2: "25.000.000"
            string countStr = r.ItemCount.ToString("N0", CultureInfo.InvariantCulture).Replace(",", ".");
            var countLabel = new Label { Text = countStr };
            countLabel.AddThemeColorOverride("font_color", new Color(0.92f, 0.92f, 0.95f));
            countLabel.AddThemeFontSizeOverride("font_size", 11);
            middleVBox.AddChild(countLabel);

            rowHBox.AddChild(middleVBox);

            // Right: Rate percentage (%100)
            var rateLabel = new Label
            {
                Text = $"%{r.Rate}",
                HorizontalAlignment = HorizontalAlignment.Right,
                SizeFlagsVertical = Control.SizeFlags.ShrinkCenter
            };
            rateLabel.AddThemeColorOverride("font_color", new Color(0.85f, 0.90f, 0.95f));
            rateLabel.AddThemeFontSizeOverride("font_size", 11);
            rowHBox.AddChild(rateLabel);

            rowPanel.AddChild(rowHBox);
            _crRewardsBox.AddChild(rowPanel);
        }
    }

    private static string GetRewardDisplayName(int itemId, string fallbackName)
    {
        return itemId switch
        {
            900000000 => "Noah",
            900001000 => "EXP",
            900003000 => "National Points",
            _ => string.IsNullOrEmpty(fallbackName) ? ItemData.DisplayName(itemId) : fallbackName
        };
    }

    private static Texture2D GetRewardIcon(int itemId)
    {
        if (itemId == 900000000) // Noah / Gold
        {
            return UiIcons.Get("system/coins") ?? GetOrGenerateGoldIcon();
        }
        if (itemId == 900001000) // EXP: Glowing blue orb
        {
            return GetOrGenerateExpIcon();
        }
        if (itemId == 900003000) // NP: Crest
        {
            return GetOrGenerateNpIcon();
        }
        return ItemData.Icon(itemId);
    }

    private static ImageTexture GetOrGenerateExpIcon()
    {
        if (_expIconTex != null) return _expIconTex;
        int s = 36;
        var img = Image.CreateEmpty(s, s, false, Image.Format.Rgba8);
        var center = new Vector2(s / 2f, s / 2f - 2);
        for (int y = 0; y < s; y++)
        {
            for (int x = 0; x < s; x++)
            {
                float dist = new Vector2(x, y).DistanceTo(center);
                float radius = s * 0.44f;
                float norm = Mathf.Clamp(1f - (dist / radius), 0f, 1f);
                if (norm > 0)
                {
                    float glow = norm * norm;
                    Color c = new Color(
                        Mathf.Clamp(0.1f * norm + 0.9f * glow, 0f, 1f),
                        Mathf.Clamp(0.5f * norm + 0.5f * glow, 0f, 1f),
                        Mathf.Clamp(0.95f * norm + 0.05f * glow, 0f, 1f),
                        1f);
                    img.SetPixel(x, y, c);
                }
                else
                {
                    img.SetPixel(x, y, new Color(0.04f, 0.08f, 0.16f, 1f));
                }
            }
        }
        _expIconTex = ImageTexture.CreateFromImage(img);
        return _expIconTex;
    }

    private static ImageTexture? _goldIconTex;
    private static ImageTexture GetOrGenerateGoldIcon()
    {
        if (_goldIconTex != null) return _goldIconTex;
        int s = 36;
        var img = Image.CreateEmpty(s, s, false, Image.Format.Rgba8);
        var center = new Vector2(s / 2f, s / 2f);
        for (int y = 0; y < s; y++)
        {
            for (int x = 0; x < s; x++)
            {
                float dist = new Vector2(x, y).DistanceTo(center);
                float radius = s * 0.42f;
                float norm = Mathf.Clamp(1f - (dist / radius), 0f, 1f);
                if (norm > 0)
                {
                    Color c = new Color(1f, 0.85f * norm + 0.15f, 0.2f * norm, 1f);
                    img.SetPixel(x, y, c);
                }
                else
                {
                    img.SetPixel(x, y, new Color(0.12f, 0.10f, 0.04f, 1f));
                }
            }
        }
        _goldIconTex = ImageTexture.CreateFromImage(img);
        return _goldIconTex;
    }

    private static ImageTexture GetOrGenerateNpIcon()
    {
        if (_npIconTex != null) return _npIconTex;
        int s = 36;
        var img = Image.CreateEmpty(s, s, false, Image.Format.Rgba8);
        var center = new Vector2(s / 2f, s / 2f);
        for (int y = 0; y < s; y++)
        {
            for (int x = 0; x < s; x++)
            {
                float dist = new Vector2(x, y).DistanceTo(center);
                float radius = s * 0.42f;
                float norm = Mathf.Clamp(1f - (dist / radius), 0f, 1f);
                if (norm > 0)
                {
                    Color c = new Color(0.95f * norm, 0.15f * norm, 0.15f * norm, 1f);
                    img.SetPixel(x, y, c);
                }
                else
                {
                    img.SetPixel(x, y, new Color(0.14f, 0.05f, 0.05f, 1f));
                }
            }
        }
        _npIconTex = ImageTexture.CreateFromImage(img);
        return _npIconTex;
    }

    // Classic KO Bronze Box Style
    private static StyleBoxFlat CreateBronzeBox(Color bg, int borderWidth = 1, int corner = 2)
    {
        return new StyleBoxFlat
        {
            BgColor = bg,
            BorderColor = new Color(0.72f, 0.52f, 0.28f), // Iconic KO bronze/gold border
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

    private static StyleBoxFlat CreateHeaderBox()
    {
        return new StyleBoxFlat
        {
            BgColor = new Color(0.12f, 0.10f, 0.08f, 0.98f),
            BorderColor = new Color(0.78f, 0.58f, 0.30f),
            BorderWidthBottom = 1,
            BorderWidthTop = 1,
            BorderWidthLeft = 1,
            BorderWidthRight = 1,
            CornerRadiusBottomLeft = 2,
            CornerRadiusBottomRight = 2,
            CornerRadiusTopLeft = 2,
            CornerRadiusTopRight = 2,
            ContentMarginBottom = 3,
            ContentMarginTop = 3,
            ContentMarginLeft = 6,
            ContentMarginRight = 6
        };
    }

    private static StyleBoxFlat CreateRewardBannerBox()
    {
        return new StyleBoxFlat
        {
            BgColor = new Color(0.15f, 0.12f, 0.09f, 0.96f),
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

    private static StyleBoxEmpty CreateEmptyStyle() => new();
}
