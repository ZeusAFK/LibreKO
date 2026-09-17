using Godot;
using LibreKO.Network;

namespace LibreKO;

public partial class World
{
    private CanvasLayer _nationTaxLayer = null!;
    private Label _nationTaxSellLabel = null!;
    private Label _nationTaxZoneLabel = null!;
    private Label _nationTaxTreasuryLabel = null!;

    private void NationTaxInit()
    {
        BuildNationTaxChip();
        Net.I.NationTaxStatusEvent += OnNationTaxStatus;

        var t = new Godot.Timer { OneShot = true, WaitTime = 2.0, Autostart = true };
        t.Timeout += () =>
        {
            if (_worldReady) Net.I.SendNationTaxStatus();
            t.QueueFree();
        };
        AddChild(t);
    }

    private void NationTaxDispose()
    {
        Net.I.NationTaxStatusEvent -= OnNationTaxStatus;
    }

    private void BuildNationTaxChip()
    {
        _nationTaxLayer = new CanvasLayer { Layer = 62 };
        AddChild(_nationTaxLayer);

        var panel = new PanelContainer
        {
            AnchorLeft = 0.5f, AnchorRight = 0.5f, AnchorTop = 0, AnchorBottom = 0,
            GrowHorizontal = Control.GrowDirection.Both,
            OffsetTop = 8,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        panel.AddThemeStyleboxOverride("panel", UiTheme.Panel(6));
        _nationTaxLayer.AddChild(panel);

        var m = new MarginContainer();
        UiTheme.Margins(m, 12, 5, 12, 5);
        panel.AddChild(m);

        var hb = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        hb.AddThemeConstantOverride("separation", 12);
        m.AddChild(hb);

        hb.AddChild(UiTheme.SectionTitle("Nation Tax"));
        hb.AddChild(NationTaxSeparator());
        hb.AddChild(UiTheme.Text("Sell", 12, UiTheme.TextLo));
        _nationTaxSellLabel = UiTheme.Text("—", 12, UiTheme.TextHi);
        hb.AddChild(_nationTaxSellLabel);
        hb.AddChild(NationTaxSeparator());
        hb.AddChild(UiTheme.Text("Tariff", 12, UiTheme.TextLo));
        _nationTaxZoneLabel = UiTheme.Text("—", 12, UiTheme.TextHi);
        hb.AddChild(_nationTaxZoneLabel);
        hb.AddChild(NationTaxSeparator());
        hb.AddChild(UiTheme.Text("Treasury", 12, UiTheme.TextLo));
        _nationTaxTreasuryLabel = UiTheme.Text("—", 12, UiTheme.Gold);
        hb.AddChild(_nationTaxTreasuryLabel);

        HudLayout.Attach(
            panel,
            "hud_nation_tax",
            panel,
            () => panel.Position);
    }

    private static Label NationTaxSeparator() => UiTheme.Text("·", 12, UiTheme.GoldDark);

    private void OnNationTaxStatus(NationTaxStatus s)
    {
        _nationTaxSellLabel.Text = $"{s.NationTaxSellPct}%";
        _nationTaxZoneLabel.Text = $"{s.NationTaxZonePct}%";
        _nationTaxTreasuryLabel.Text = NationTaxFormatGold(s.NationTaxTreasury);
    }

    private static string NationTaxFormatGold(int gold) => gold.ToString("N0", System.Globalization.CultureInfo.InvariantCulture);
}
