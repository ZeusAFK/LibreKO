using Godot;
using LibreKO.Network;

namespace LibreKO;

public partial class World
{
    private CanvasLayer _genieLayer = null!;
    private HudWindow _geniePanel = null!;
    private Label _genieTip = null!;
    private Label _genieStatus = null!;
    private Button _genieClaimBtn = null!;
    private bool _genieShown;

    private void GenieInit()
    {
        _genieLayer = new CanvasLayer { Layer = 61 };
        AddChild(_genieLayer);
        _geniePanel = new HudWindow("genie", "Genie", new Vector2(220, 150)) { Visible = false };
        _geniePanel.Closed += CloseGenie;
        _genieLayer.AddChild(_geniePanel);

        var root = _geniePanel.Body;
        root.AddThemeConstantOverride("separation", 8);
        root.AddChild(UiTheme.SectionTitle("Your Genie"));

        _genieTip = UiTheme.Text("...", 13, UiTheme.TextHi);
        _genieTip.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _genieTip.CustomMinimumSize = new Vector2(300, 0);
        root.AddChild(_genieTip);

        _genieStatus = UiTheme.Text("", 12, UiTheme.TextLo);
        root.AddChild(_genieStatus);

        _genieClaimBtn = new Button { Text = "Claim Daily Reward", FocusMode = Control.FocusModeEnum.None };
        _genieClaimBtn.Pressed += () => Net.I.SendGenieClaim();
        root.AddChild(_genieClaimBtn);

        Net.I.GenieStatusEvent += OnGenieStatus;
        Net.I.GenieClaimEvent += OnGenieClaim;
    }

    private void GenieDispose()
    {
        Net.I.GenieStatusEvent -= OnGenieStatus;
        Net.I.GenieClaimEvent -= OnGenieClaim;
    }

    private void ToggleGenie()
    {
        if (_genieShown) { CloseGenie(); return; }
        _geniePanel.Visible = true;
        _genieShown = true;
        Net.I.SendGenieStatus();
    }

    private void CloseGenie()
    {
        if (!_genieShown) return;
        _genieShown = false;
        _geniePanel.Visible = false;
    }

    private void OnGenieStatus(string tip, bool rewardAvail)
    {
        _genieTip.Text = tip;
        if (rewardAvail)
        {
            _genieStatus.Text = "A daily reward is waiting for you!";
            _genieStatus.AddThemeColorOverride("font_color", UiTheme.Gold);
            _genieClaimBtn.Disabled = false;
        }
        else
        {
            _genieStatus.Text = "Daily reward already claimed. Come back tomorrow.";
            _genieStatus.AddThemeColorOverride("font_color", UiTheme.TextLo);
            _genieClaimBtn.Disabled = true;
        }
    }

    private void OnGenieClaim(bool ok, int rewardGold)
    {
        if (ok)
        {
            Chat.Info($"Your genie granted you {rewardGold} gold.");
            Net.I.SendGenieStatus();
        }
    }
}
