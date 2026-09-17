using Godot;
using LibreKO.Network;

namespace LibreKO;

public partial class World
{
    private CanvasLayer _seasonalLayer = null!;
    private Label _seasonalLabel = null!;
    private int _seasonalState = -1;

    private void SeasonalInit()
    {
        BuildSeasonalBanner();
        Net.I.SantaEvent += OnSanta;
    }

    private void SeasonalDispose()
    {
        Net.I.SantaEvent -= OnSanta;
    }

    private void BuildSeasonalBanner()
    {
        _seasonalLayer = new CanvasLayer { Layer = 68, Visible = false };
        AddChild(_seasonalLayer);

        var panel = new PanelContainer
        {
            AnchorLeft = 0.5f, AnchorRight = 0.5f, AnchorTop = 0, AnchorBottom = 0,
            GrowHorizontal = Control.GrowDirection.Both,
            OffsetTop = 150,
        };
        panel.AddThemeStyleboxOverride("panel", UiTheme.Panel(7, true));
        _seasonalLayer.AddChild(panel);

        var m = new MarginContainer();
        UiTheme.Margins(m, 18, 7, 18, 7);
        panel.AddChild(m);

        _seasonalLabel = UiTheme.Text("", 15, UiTheme.GoldBright, HorizontalAlignment.Center);
        _seasonalLabel.AddThemeConstantOverride("outline_size", 4);
        m.AddChild(_seasonalLabel);
    }

    private void OnSanta(int state)
    {
        if (state == _seasonalState) return;
        _seasonalState = state;
        ApplySeasonal(announce: true);
    }

    private void ApplySeasonal(bool announce)
    {
        switch (_seasonalState)
        {
            case 1:
                _seasonalLabel.Text = "❄  Santa Claus is flying over the realm!  ❄";
                _seasonalLayer.Visible = true;
                if (announce) Chat.Info("A holiday event has begun — Santa Claus is flying overhead!");
                break;
            case 2:
                _seasonalLabel.Text = "✧  An Angel descends upon the battlefield!  ✧";
                _seasonalLayer.Visible = true;
                if (announce) Chat.Info("A holiday event has begun — an Angel graces the realm!");
                break;
            default:
                _seasonalLayer.Visible = false;
                if (announce && _seasonalState == 0) Chat.Info("The holiday event has ended.");
                break;
        }
    }
}
