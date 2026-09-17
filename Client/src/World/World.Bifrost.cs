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

    private ConfirmationDialog _bifrostJoinDlg = null!;

    private bool _bifrostActive;
    private int _bifrostRemaining;
    private int _bifrostMaxSeen;
    private bool _bifrostPromptShown;

    private const int BifrostUrgentSecs = 30;
    private static readonly Color BifrostCalmCol   = new("c8a45a");
    private static readonly Color BifrostUrgentCol = new("e0574a");

    private void BifrostInit()
    {
        BuildBifrostUi();
        Net.I.BifrostTimeEvent    += OnBifrostTime;
        Net.I.BifrostJoinEvent    += OnBifrostJoinResult;
        Net.I.BifrostDisbandEvent += OnBifrostDisband;

        if (_worldReady) Net.I.SendBifrostTimeRequest();
    }

    private void BifrostDispose()
    {
        Net.I.BifrostTimeEvent    -= OnBifrostTime;
        Net.I.BifrostJoinEvent    -= OnBifrostJoinResult;
        Net.I.BifrostDisbandEvent -= OnBifrostDisband;
    }

    private void BifrostRequestTime() => Net.I.SendBifrostTimeRequest();

    private void BuildBifrostUi()
    {
        _bifrostLayer = new CanvasLayer { Layer = 68 };
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
            OffsetTop = 140,
            Visible = false,
        };
        _bifrostBanner.AddThemeStyleboxOverride("panel", UiTheme.Panel(7, true));
        _bifrostLayer.AddChild(_bifrostBanner);

        var m = new MarginContainer();
        UiTheme.Margins(m, 22, 9, 22, 10);
        _bifrostBanner.AddChild(m);

        var col = new VBoxContainer { CustomMinimumSize = new Vector2(240, 0) };
        col.AddThemeConstantOverride("separation", 4);
        m.AddChild(col);

        var head = new HBoxContainer();
        head.AddThemeConstantOverride("separation", 12);
        col.AddChild(head);

        _bifrostTitleLbl = UiTheme.Text("Bifrost", 16, UiTheme.GoldBright);
        _bifrostTitleLbl.AddThemeConstantOverride("outline_size", 4);
        _bifrostTitleLbl.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _bifrostTitleLbl.MouseFilter = Control.MouseFilterEnum.Ignore;
        head.AddChild(_bifrostTitleLbl);

        _bifrostTimerLbl = UiTheme.Text("--:--", 18, UiTheme.TextHi, HorizontalAlignment.Right);
        _bifrostTimerLbl.AddThemeConstantOverride("outline_size", 4);
        _bifrostTimerLbl.MouseFilter = Control.MouseFilterEnum.Ignore;
        head.AddChild(_bifrostTimerLbl);

        _bifrostBar = new ProgressBar
        {
            MinValue = 0, MaxValue = 1, Value = 1,
            ShowPercentage = false,
            CustomMinimumSize = new Vector2(0, 6),
        };
        var track = new StyleBoxFlat { BgColor = new Color(0, 0, 0, 0.45f) };
        track.SetCornerRadiusAll(3);
        _bifrostBarFill = new StyleBoxFlat { BgColor = BifrostCalmCol };
        _bifrostBarFill.SetCornerRadiusAll(3);
        _bifrostBar.AddThemeStyleboxOverride("background", track);
        _bifrostBar.AddThemeStyleboxOverride("fill", _bifrostBarFill);
        col.AddChild(_bifrostBar);

        HudLayout.Attach(_bifrostBanner, "hud_bifrost", head, () => _bifrostBanner.Position);
    }

    private void BuildBifrostJoinDialog()
    {
        _bifrostJoinDlg = new ConfirmationDialog
        {
            Title = "Bifrost Event",
            DialogText = "The Bifrost has opened. Join the event?",
            OkButtonText = "Join",
            CancelButtonText = "Not now",
            Exclusive = false,
        };
        _bifrostJoinDlg.Confirmed += OnBifrostJoinConfirmed;
        _bifrostJoinDlg.Canceled += OnBifrostPromptClosed;
        _bifrostJoinDlg.CloseRequested += OnBifrostPromptClosed;
        _bifrostLayer.AddChild(_bifrostJoinDlg);
    }

    private void OnBifrostTime(int remaining)
    {
        if (remaining <= 0)
        {
            EndBifrostEvent();
            return;
        }

        bool wasActive = _bifrostActive;
        _bifrostActive = true;
        _bifrostRemaining = remaining;
        if (remaining > _bifrostMaxSeen) _bifrostMaxSeen = remaining;

        _bifrostBanner.Visible = true;
        UpdateBifrostBanner();
        if (_bifrostTick.IsStopped()) _bifrostTick.Start();

        if (!wasActive)
        {
            Chat.Info("[Bifrost] The chaos event has begun!");
            OfferBifrostJoin();
        }
    }

    private void OnBifrostJoinResult(bool joined, int zone)
    {
        if (joined)
        {
            Chat.Info("[Bifrost] You have joined the event.");
        }
        else
        {
            Chat.Info("[Bifrost] The event is not open to join right now.");
        }
    }

    private void OnBifrostDisband()
    {
        Chat.Info("[Bifrost] You have left the event.");
    }

    private void EndBifrostEvent()
    {
        if (_bifrostActive) Chat.Info("[Bifrost] The event has ended.");
        _bifrostActive = false;
        _bifrostRemaining = 0;
        _bifrostMaxSeen = 0;
        _bifrostPromptShown = false;
        _bifrostBanner.Visible = false;
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
        _bifrostTimerLbl.Text = $"{s / 60:00}:{s % 60:00}";

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
        if (!_bifrostActive || _bifrostJoinDlg.Visible) return;
        _bifrostJoinDlg.PopupCentered();
    }

    private void OnBifrostJoinConfirmed()
    {
        Net.I.SendBifrostJoin();
        OnBifrostPromptClosed();
    }

    private void OnBifrostPromptClosed()
    {
    }
}
