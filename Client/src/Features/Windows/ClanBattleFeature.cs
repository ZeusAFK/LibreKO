using Godot;

namespace LibreKO.Features.Windows;

internal sealed class ClanBattleFeature : IFeature
{
    private readonly IWorldContext context;
    private readonly ClanBattleSession clanBattle;
    private readonly EventPlatesFeature eventPlates;

    public ClanBattleFeature(IWorldContext context, ClanBattleSession clanBattle, EventPlatesFeature eventPlates)
    {
        this.context = context;
        this.clanBattle = clanBattle;
        this.eventPlates = eventPlates;
    }

    private PanelContainer panel = null!;
    private Label label = null!;
    private int token;

    private static readonly Color Gold = UiTheme.GoldBright;

    public void Build()
    {
        BuildBanner();
        clanBattle.ClanBattleNotifyEvent += OnNotify;
        clanBattle.IdentityCheckResultEvent += OnIdentityCheckResult;
    }

    public void Dispose()
    {
        clanBattle.ClanBattleNotifyEvent -= OnNotify;
        clanBattle.IdentityCheckResultEvent -= OnIdentityCheckResult;
    }

    private void BuildBanner()
    {
        var layer = new CanvasLayer { Layer = UiLayers.HudLauncher };
        context.Root.AddChild(layer);

        var panel = new PanelContainer { Visible = false };
        panel.AddThemeStyleboxOverride("panel", UiTheme.Panel(7, true));
        layer.AddChild(panel);
        this.panel = panel;

        var m = new MarginContainer();
        UiTheme.Margins(m, 18, 9, 18, 9);
        panel.AddChild(m);

        label = UiTheme.Text("", 16, Gold, HorizontalAlignment.Center);
        label.AddThemeConstantOverride("outline_size", 5);
        m.AddChild(label);
        eventPlates.AddPlate(panel);
    }

    private void ShowBanner(string text, Color colour)
    {
        label.Text = text;
        label.AddThemeColorOverride("font_color", colour);
        panel.Visible = true;

        int token = ++this.token;
        double secs = Mathf.Clamp(3.5 + text.Length * 0.045, 4.0, 12.0);
        context.Root.After(secs, () => { if (this.token == token) panel.Visible = false; });
    }

    private void OnNotify()
    {
        const string line = "Clan battle status has changed.";
        ShowBanner(line, Gold);
        context.Log.Add(line, CombatLogKind.Status);
    }

    private const byte IdentityFailed = 0;
    private const byte IdentityAuthorized = 1;
    private const byte IdentityWrongNumber = 2;
    private const byte IdentityAlreadyAuthorized = 3;
    private const byte IdentityTooManyTries = 4;
    private const byte IdentityWrongNumberAgain = 5;

    private void OnIdentityCheckResult(byte result)
    {
        var (id, fallback) = result switch
        {
            IdentityFailed => (25051, "You have failed to authorize your identification."),
            IdentityAuthorized => (25052, "You have authorized your identification."),
            IdentityWrongNumber or IdentityWrongNumberAgain => (1511, "Wrong ID number"),
            IdentityAlreadyAuthorized => (25053, "You have already authorized your identification."),
            IdentityTooManyTries => (25054, "You have tried more than limited time. You need to relog-in the game."),
            _ => (0, ""),
        };
        if (id != 0) context.Log.Notice(ItemData.Text(id, fallback));
    }
}
