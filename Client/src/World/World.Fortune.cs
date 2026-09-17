using Godot;
using LibreKO.Network;

namespace LibreKO;

public partial class World
{
    private CanvasLayer _fortuneLayer = null!;
    private HudWindow _fortunePanel = null!;
    private Button _fortuneDrawBtn = null!;
    private Label _fortuneResult = null!;
    private bool _fortuneShown;
    private bool _fortuneCanDraw;

    private void FortuneInit()
    {
        _fortuneLayer = new CanvasLayer { Layer = 73 };
        AddChild(_fortuneLayer);
        _fortunePanel = new HudWindow("fortune", "Daily Fortune", new Vector2(360, 240)) { Visible = false };
        _fortunePanel.Closed += CloseFortune;
        _fortuneLayer.AddChild(_fortunePanel);
        var root = _fortunePanel.Body;
        root.AddThemeConstantOverride("separation", 8);
        root.AddChild(UiTheme.SectionTitle("Daily Fortune"));
        root.AddChild(UiTheme.Text("Try your luck — one free draw each day!", 13, UiTheme.TextLo));

        _fortuneResult = UiTheme.Text("", 14, UiTheme.Gold);
        root.AddChild(_fortuneResult);

        _fortuneDrawBtn = new Button { Text = "Draw", FocusMode = Control.FocusModeEnum.None };
        _fortuneDrawBtn.Pressed += OnFortuneDrawPressed;
        root.AddChild(_fortuneDrawBtn);

        Net.I.FortuneStatusEvent += OnFortuneStatus;
        Net.I.FortuneDrawEvent += OnFortuneDraw;
    }

    private void FortuneDispose()
    {
        Net.I.FortuneStatusEvent -= OnFortuneStatus;
        Net.I.FortuneDrawEvent -= OnFortuneDraw;
    }

    private void ToggleFortune()
    {
        if (_fortuneShown) { CloseFortune(); return; }
        _fortunePanel.Visible = true;
        _fortuneShown = true;
        _fortuneResult.Text = "";
        Net.I.SendFortuneStatus();
    }

    private void CloseFortune()
    {
        if (!_fortuneShown) return;
        _fortuneShown = false;
        _fortunePanel.Visible = false;
    }

    private void OnFortuneDrawPressed()
    {
        if (!_fortuneCanDraw) return;
        _fortuneDrawBtn.Disabled = true;
        Net.I.SendFortuneDraw();
    }

    private void OnFortuneStatus(bool canDraw)
    {
        _fortuneCanDraw = canDraw;
        _fortuneDrawBtn.Disabled = !canDraw;
        _fortuneDrawBtn.Text = canDraw ? "Draw" : "Drawn Today";
        if (!canDraw && _fortuneResult.Text == "")
            _fortuneResult.Text = "You've already drawn today. Come back tomorrow!";
    }

    private void OnFortuneDraw(bool drew, int rewardItemId, int rewardGold)
    {
        _fortuneCanDraw = false;
        _fortuneDrawBtn.Disabled = true;
        _fortuneDrawBtn.Text = "Drawn Today";

        if (!drew)
        {
            _fortuneResult.Text = "Already drawn today. Come back tomorrow!";
            return;
        }

        string prize = rewardItemId != 0
            ? (rewardGold > 0 ? $"item #{rewardItemId} + {rewardGold} gold" : $"item #{rewardItemId}")
            : $"{rewardGold} gold";
        _fortuneResult.Text = $"You won: {prize}!";
    }
}
