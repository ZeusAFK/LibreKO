using Godot;
using LibreKO.Network;

namespace LibreKO;

public partial class World
{
    private CanvasLayer _bifrostLayer = null!;

    private PanelContainer _bifrostBanner = null!;
    private Label _bifrostTitleLbl = null!;
    private Label _bifrostTimerLbl = null!;
    private ProgressBar _bifrostBar = null!;
    private StyleBoxFlat _bifrostBarFill = null!;
    private Godot.Timer _bifrostTick = null!;

    private Control _joinModal = null!;
    private Label _joinModalTitle = null!;
    private Label _joinModalTimer = null!;
    private ProgressBar _joinModalBar = null!;
    private StyleBoxFlat _joinModalBarFill = null!;
    private Label _joinModalStatus = null!;
    private Button _joinModalBtn = null!;
    private Button _joinModalCloseBtn = null!;

    private bool _bifrostActive;
    private int _bifrostRemaining;
    private int _bifrostMaxSeen;
    private bool _bifrostPromptShown;
    private bool _isEventRegistered;
    private string _eventTitle = "Juraid Mountain";

    private const int BifrostUrgentSecs = 30;
    private static readonly Color BifrostCalmCol   = new("c8a45a");
    private static readonly Color BifrostUrgentCol = new("e0574a");

    private void BifrostInit()
    {
        BuildBifrostUi();
        Net.I.BifrostTimeEvent    += OnBifrostTime;
        Net.I.BifrostJoinEvent    += OnBifrostJoinResult;
        Net.I.BifrostDisbandEvent += OnBifrostDisband;
        Net.I.NoticeEvent         += OnBifrostNotice;

        if (_worldReady) Net.I.SendBifrostTimeRequest();
    }

    private void BifrostDispose()
    {
        Net.I.BifrostTimeEvent    -= OnBifrostTime;
        Net.I.BifrostJoinEvent    -= OnBifrostJoinResult;
        Net.I.BifrostDisbandEvent -= OnBifrostDisband;
        Net.I.NoticeEvent         -= OnBifrostNotice;
    }

    private void BifrostRequestTime() => Net.I.SendBifrostTimeRequest();

    private void BuildBifrostUi()
    {
        _bifrostLayer = new CanvasLayer { Layer = 110 };
        AddChild(_bifrostLayer);

        BuildBifrostBanner();
        BuildBifrostJoinDialog();

        _bifrostTick = new Godot.Timer { WaitTime = 1.0, Autostart = false, OneShot = false };
        _bifrostTick.Timeout += OnBifrostTick;
        _bifrostLayer.AddChild(_bifrostTick);
    }

    private void BuildBifrostBanner()
    {
        _bifrostBanner = new PanelContainer
        {
            AnchorLeft = 0.5f, AnchorRight = 0.5f, AnchorTop = 0, AnchorBottom = 0,
            GrowHorizontal = Control.GrowDirection.Both,
            OffsetTop = 80,
            Visible = false,
            MouseFilter = Control.MouseFilterEnum.Stop,
            MouseDefaultCursorShape = Control.CursorShape.PointingHand,
            TooltipText = "Click to open registration window",
        };
        _bifrostBanner.AddThemeStyleboxOverride("panel", UiTheme.Panel(5, true));
        _bifrostBanner.GuiInput += ev =>
        {
            if (ev is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left })
                BifrostShowJoinPrompt();
        };
        _bifrostLayer.AddChild(_bifrostBanner);

        var m = new MarginContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        UiTheme.Margins(m, 10, 4, 10, 5);
        _bifrostBanner.AddChild(m);

        var col = new VBoxContainer { CustomMinimumSize = new Vector2(160, 0), MouseFilter = Control.MouseFilterEnum.Ignore };
        col.AddThemeConstantOverride("separation", 2);
        m.AddChild(col);

        var head = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        head.AddThemeConstantOverride("separation", 8);
        col.AddChild(head);

        _bifrostTitleLbl = UiTheme.Text(_eventTitle, 12, UiTheme.GoldBright);
        _bifrostTitleLbl.AddThemeConstantOverride("outline_size", 2);
        _bifrostTitleLbl.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _bifrostTitleLbl.MouseFilter = Control.MouseFilterEnum.Ignore;
        head.AddChild(_bifrostTitleLbl);

        _bifrostTimerLbl = UiTheme.Text("--:--", 12, UiTheme.TextHi, HorizontalAlignment.Right);
        _bifrostTimerLbl.AddThemeConstantOverride("outline_size", 2);
        _bifrostTimerLbl.MouseFilter = Control.MouseFilterEnum.Ignore;
        head.AddChild(_bifrostTimerLbl);

        _bifrostBar = new ProgressBar
        {
            MinValue = 0, MaxValue = 1, Value = 1,
            ShowPercentage = false,
            CustomMinimumSize = new Vector2(0, 3),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        var track = new StyleBoxFlat { BgColor = new Color(0, 0, 0, 0.45f) };
        track.SetCornerRadiusAll(2);
        _bifrostBarFill = new StyleBoxFlat { BgColor = BifrostCalmCol };
        _bifrostBarFill.SetCornerRadiusAll(2);
        _bifrostBar.AddThemeStyleboxOverride("background", track);
        _bifrostBar.AddThemeStyleboxOverride("fill", _bifrostBarFill);
        col.AddChild(_bifrostBar);

        HudLayout.Attach(_bifrostBanner, "hud_bifrost", null, () => _bifrostBanner.Position);
    }

    private void BuildBifrostJoinDialog()
    {
        _joinModal = new Control
        {
            Visible = false,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _joinModal.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _bifrostLayer.AddChild(_joinModal);

        var center = new CenterContainer
        {
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        center.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _joinModal.AddChild(center);

        var panel = new PanelContainer
        {
            CustomMinimumSize = new Vector2(220, 0),
            MouseFilter = Control.MouseFilterEnum.Stop,
        };
        panel.AddThemeStyleboxOverride("panel", UiTheme.Panel(6, true));
        center.AddChild(panel);

        var m = new MarginContainer();
        UiTheme.Margins(m, 10, 6, 10, 8);
        panel.AddChild(m);

        var vb = new VBoxContainer();
        vb.AddThemeConstantOverride("separation", 4);
        m.AddChild(vb);

        // Header Bar (Title & Close Button)
        var headerRow = new HBoxContainer();
        headerRow.AddThemeConstantOverride("separation", 4);
        vb.AddChild(headerRow);

        _joinModalTitle = UiTheme.Text($"#  {_eventTitle.ToUpper()}  #", 11, UiTheme.GoldBright, HorizontalAlignment.Center);
        _joinModalTitle.AddThemeConstantOverride("outline_size", 2);
        _joinModalTitle.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        headerRow.AddChild(_joinModalTitle);

        var xBtn = new Button
        {
            Text = "✕",
            CustomMinimumSize = new Vector2(16, 16),
            FocusMode = Control.FocusModeEnum.None,
        };
        xBtn.AddThemeColorOverride("font_color", UiTheme.TextDim);
        xBtn.AddThemeColorOverride("font_hover_color", UiTheme.GoldBright);
        xBtn.AddThemeStyleboxOverride("normal", new StyleBoxEmpty());
        xBtn.AddThemeStyleboxOverride("hover", new StyleBoxEmpty());
        xBtn.AddThemeStyleboxOverride("pressed", new StyleBoxEmpty());
        xBtn.Pressed += CloseJoinModal;
        headerRow.AddChild(xBtn);

        // Timer & Mini Progress Bar
        var timerBox = new VBoxContainer();
        timerBox.AddThemeConstantOverride("separation", 3);
        vb.AddChild(timerBox);

        _joinModalTimer = UiTheme.Text("--:--", 16, UiTheme.GoldBright, HorizontalAlignment.Center);
        _joinModalTimer.AddThemeConstantOverride("outline_size", 2);
        timerBox.AddChild(_joinModalTimer);

        _joinModalBar = new ProgressBar
        {
            MinValue = 0, MaxValue = 1, Value = 1,
            ShowPercentage = false,
            CustomMinimumSize = new Vector2(0, 3),
        };
        var modalTrack = new StyleBoxFlat { BgColor = new Color(0, 0, 0, 0.45f) };
        modalTrack.SetCornerRadiusAll(2);
        _joinModalBarFill = new StyleBoxFlat { BgColor = BifrostCalmCol };
        _joinModalBarFill.SetCornerRadiusAll(2);
        _joinModalBar.AddThemeStyleboxOverride("background", modalTrack);
        _joinModalBar.AddThemeStyleboxOverride("fill", _joinModalBarFill);
        timerBox.AddChild(_joinModalBar);

        // Status text
        _joinModalStatus = UiTheme.Text("Registration is OPEN! Click [Join] to participate.", 10, UiTheme.TextHi, HorizontalAlignment.Center);
        _joinModalStatus.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        vb.AddChild(_joinModalStatus);

        // Action Buttons KO Theme
        var btnRow = new HBoxContainer();
        btnRow.AddThemeConstantOverride("separation", 6);
        vb.AddChild(btnRow);

        _joinModalBtn = Ui.MenuButton("Join", height: 24, fontSize: 10);
        _joinModalBtn.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _joinModalBtn.Pressed += OnJoinModalToggle;
        btnRow.AddChild(_joinModalBtn);

        _joinModalCloseBtn = Ui.MenuButton("Close", height: 24, fontSize: 10);
        _joinModalCloseBtn.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _joinModalCloseBtn.Pressed += CloseJoinModal;
        btnRow.AddChild(_joinModalCloseBtn);
    }

    private void CloseJoinModal()
    {
        if (_joinModal != null && IsInstanceValid(_joinModal))
            _joinModal.Visible = false;

        if (_bifrostActive)
        {
            Chat.Info($"[{_eventTitle}] Window closed. Click the top timer banner anytime to reopen.");
        }
    }

    private void OnBifrostNotice(string msg)
    {
        if (string.IsNullOrWhiteSpace(msg)) return;
        if (msg.Contains("Juraid", System.StringComparison.OrdinalIgnoreCase) || msg.Contains("JR", System.StringComparison.OrdinalIgnoreCase))
            _eventTitle = "Juraid Mountain";
        else if (msg.Contains("Border Defense War", System.StringComparison.OrdinalIgnoreCase) || msg.Contains("BDW", System.StringComparison.OrdinalIgnoreCase))
            _eventTitle = "Border Defense War";
        else if (msg.Contains("Chaos", System.StringComparison.OrdinalIgnoreCase))
            _eventTitle = "Chaos Dungeon";
        else if (msg.Contains("Bifrost", System.StringComparison.OrdinalIgnoreCase))
            _eventTitle = "Bifrost";

        if (_bifrostTitleLbl != null && IsInstanceValid(_bifrostTitleLbl))
            _bifrostTitleLbl.Text = _eventTitle;
        if (_joinModalTitle != null && IsInstanceValid(_joinModalTitle))
            _joinModalTitle.Text = $"#  {_eventTitle.ToUpper()}  #";
    }

    private void OnBifrostTime(int remaining, byte eventType)
    {
        if (remaining <= 0)
        {
            EndBifrostEvent();
            return;
        }

        if (eventType == 1) _eventTitle = "Chaos Dungeon";
        else if (eventType == 2) _eventTitle = "Border Defense War";
        else if (eventType == 3) _eventTitle = "Juraid Mountain";

        if (_bifrostTitleLbl != null && IsInstanceValid(_bifrostTitleLbl))
            _bifrostTitleLbl.Text = _eventTitle;
        if (_joinModalTitle != null && IsInstanceValid(_joinModalTitle))
            _joinModalTitle.Text = $"#  {_eventTitle.ToUpper()}  #";

        bool wasActive = _bifrostActive;
        _bifrostActive = true;
        _bifrostRemaining = remaining;
        if (remaining > _bifrostMaxSeen) _bifrostMaxSeen = remaining;

        _bifrostBanner.Visible = true;
        UpdateBifrostBanner();
        if (_bifrostTick.IsStopped()) _bifrostTick.Start();

        if (!wasActive)
        {
            Chat.Info($"[{_eventTitle}] Registration is open ({remaining} seconds)!");
            OfferBifrostJoin();
        }
    }

    private void OnJoinModalToggle()
    {
        if (!_isEventRegistered)
        {
            Net.I.SendBifrostJoin();
            _isEventRegistered = true;
            UpdateJoinModalState();
        }
        else
        {
            Net.I.SendBifrostDisband();
            _isEventRegistered = false;
            UpdateJoinModalState();
        }
    }

    private void UpdateJoinModalState()
    {
        if (_joinModalStatus == null || !IsInstanceValid(_joinModalStatus)) return;
        if (_joinModalBtn == null || !IsInstanceValid(_joinModalBtn)) return;

        if (_isEventRegistered)
        {
            _joinModalStatus.Text = "✓ Registered! You will be teleported automatically.";
            _joinModalStatus.AddThemeColorOverride("font_color", UiTheme.Good);
            _joinModalBtn.Text = "Cancel";
            _joinModalBtn.AddThemeColorOverride("font_color", UiTheme.Bad);
        }
        else
        {
            _joinModalStatus.Text = "Registration is OPEN! Click [Join] to participate.";
            _joinModalStatus.AddThemeColorOverride("font_color", UiTheme.TextHi);
            _joinModalBtn.Text = "Join";
            _joinModalBtn.AddThemeColorOverride("font_color", UiTheme.TextHi);
        }
    }

    private void OnBifrostJoinResult(bool joined, int zone)
    {
        _isEventRegistered = joined;
        UpdateJoinModalState();
        if (joined)
        {
            Chat.Info($"[{_eventTitle}] You have registered! Prepare for battle.");
        }
        else
        {
            Chat.Info($"[{_eventTitle}] Unable to register for the event at this time.");
        }
    }

    private void OnBifrostDisband()
    {
        _isEventRegistered = false;
        UpdateJoinModalState();
        Chat.Info($"[{_eventTitle}] You cancelled your event registration.");
    }

    private void EndBifrostEvent()
    {
        if (_bifrostActive) Chat.Info($"[{_eventTitle}] Event registration has ended.");
        _bifrostActive = false;
        _bifrostRemaining = 0;
        _bifrostMaxSeen = 0;
        _bifrostPromptShown = false;
        _bifrostBanner.Visible = false;
        if (_joinModal != null && IsInstanceValid(_joinModal)) _joinModal.Visible = false;
        _isEventRegistered = false;
        UpdateJoinModalState();
        _bifrostTick.Stop();
    }

    private void OnBifrostTick()
    {
        if (!_bifrostActive) { _bifrostTick.Stop(); return; }
        if (_bifrostRemaining > 0) _bifrostRemaining--;
        if (_bifrostRemaining <= 0) { EndBifrostEvent(); return; }
        UpdateBifrostBanner();
    }

    private void UpdateBifrostBanner()
    {
        int s = Mathf.Max(0, _bifrostRemaining);
        string timeStr = $"{s / 60:00}:{s % 60:00}";
        _bifrostTimerLbl.Text = timeStr;
        if (_joinModalTimer != null && IsInstanceValid(_joinModalTimer))
        {
            _joinModalTimer.Text = timeStr;
            if (_bifrostRemaining <= BifrostUrgentSecs)
                _joinModalTimer.AddThemeColorOverride("font_color", BifrostUrgentCol);
            else
                _joinModalTimer.AddThemeColorOverride("font_color", UiTheme.GoldBright);
        }

        float frac = _bifrostMaxSeen > 0 ? Mathf.Clamp((float)_bifrostRemaining / _bifrostMaxSeen, 0f, 1f) : 0f;
        _bifrostBar.Value = frac;

        bool urgent = _bifrostRemaining <= BifrostUrgentSecs;
        var col = urgent ? BifrostUrgentCol : BifrostCalmCol;
        if (urgent)
        {
            float pulse = 0.6f + 0.4f * Mathf.Sin((float)Time.GetTicksMsec() * 0.012f);
            col = new Color(BifrostUrgentCol, pulse);
            _bifrostTimerLbl.AddThemeColorOverride("font_color", new Color(BifrostUrgentCol, 0.6f + 0.4f * pulse));
        }
        else
        {
            _bifrostTimerLbl.AddThemeColorOverride("font_color", UiTheme.TextHi);
        }
        _bifrostBarFill.BgColor = col;
        if (_joinModalBar != null && IsInstanceValid(_joinModalBar))
        {
            _joinModalBar.Value = frac;
            if (_joinModalBarFill != null) _joinModalBarFill.BgColor = col;
        }
    }

    private void OfferBifrostJoin()
    {
        if (_bifrostPromptShown) return;
        _bifrostPromptShown = true;
        BifrostShowJoinPrompt();
    }

    private void BifrostToggleJoin()
    {
        if (!_bifrostActive) return;
        BifrostShowJoinPrompt();
    }

    private void BifrostShowJoinPrompt()
    {
        if (!_bifrostActive) return;
        if (_joinModalTitle != null && IsInstanceValid(_joinModalTitle))
            _joinModalTitle.Text = $"#  {_eventTitle.ToUpper()}  #";
        if (_bifrostTitleLbl != null && IsInstanceValid(_bifrostTitleLbl))
            _bifrostTitleLbl.Text = _eventTitle;
        _joinModal.Visible = true;
    }
}
