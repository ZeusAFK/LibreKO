using Godot;
using LibreKO.Network;

namespace LibreKO;

public partial class World
{
    private CanvasLayer _rouletteLayer = null!;
    private HudWindow _roulettePanel = null!;
    private Label _rouletteCoinsLabel = null!;
    private Label _rouletteResultLabel = null!;
    private Button _rouletteSpinBtn = null!;
    private bool _rouletteShown;
    private int _rouletteCoins;

    private void RouletteInit()
    {
        _rouletteLayer = new CanvasLayer { Layer = 78 };
        AddChild(_rouletteLayer);
        _roulettePanel = new HudWindow("roulette", Localization.Loc.Tr("Event Roulette"), new Vector2(220, 150)) { Visible = false };
        _roulettePanel.Closed += CloseRoulette;
        _rouletteLayer.AddChild(_roulettePanel);

        var root = _roulettePanel.Body;
        root.AddThemeConstantOverride("separation", 8);
        root.AddChild(UiTheme.SectionTitle(Localization.Loc.Tr("Event Roulette")));

        _rouletteCoinsLabel = UiTheme.Text($"{Localization.Loc.Tr("Event Coins")}: -", 14, UiTheme.Gold);
        root.AddChild(_rouletteCoinsLabel);

        _rouletteSpinBtn = new Button { Text = Localization.Loc.Tr("Spin (1 coin)"), FocusMode = Control.FocusModeEnum.None };
        _rouletteSpinBtn.Pressed += OnRouletteSpinPressed;
        root.AddChild(_rouletteSpinBtn);

        _rouletteResultLabel = UiTheme.Text(Localization.Loc.Tr("Spin to win a prize!"), 13, UiTheme.TextLo);
        root.AddChild(_rouletteResultLabel);

        Net.I.RouletteStatusEvent += OnRouletteStatus;
        Net.I.RouletteSpinEvent += OnRouletteSpin;
    }

    private void RouletteDispose()
    {
        Net.I.RouletteStatusEvent -= OnRouletteStatus;
        Net.I.RouletteSpinEvent -= OnRouletteSpin;
    }

    private void ToggleRoulette()
    {
        if (_rouletteShown) { CloseRoulette(); return; }
        _roulettePanel.Visible = true;
        _rouletteShown = true;
        Net.I.SendRouletteStatus();
    }

    private void CloseRoulette()
    {
        if (!_rouletteShown) return;
        _rouletteShown = false;
        _roulettePanel.Visible = false;
    }

    private void OnRouletteSpinPressed()
    {
        if (_rouletteCoins < 1)
        {
            _rouletteResultLabel.Text = Localization.Loc.Tr("Not enough event coins.");
            return;
        }
        Net.I.SendRouletteSpin();
    }

    private void OnRouletteStatus(int coins)
    {
        _rouletteCoins = coins;
        _rouletteCoinsLabel.Text = $"{Localization.Loc.Tr("Event Coins")}: {coins}";
        _rouletteSpinBtn.Disabled = coins < 1;
    }

    private void OnRouletteSpin(bool ok, int prizeItemId, int prizeGold)
    {
        if (!ok)
        {
            _rouletteResultLabel.Text = Localization.Loc.Tr("Not enough event coins.");
            Net.I.SendRouletteStatus();
            return;
        }

        string prize = prizeGold > 0
            ? $"{prizeGold:N0} {Localization.Loc.Tr("gold")}"
            : (prizeItemId > 0 ? $"item #{prizeItemId}" : Localization.Loc.Tr("nothing"));
        _rouletteResultLabel.Text = $"{Localization.Loc.Tr("You won")} {prize}!";

        Net.I.SendRouletteStatus();
    }
}
