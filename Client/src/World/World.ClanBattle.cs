using Godot;
using LibreKO.Network;

namespace LibreKO;

public partial class World
{
    private CanvasLayer _clanBattleLayer = null!;
    private Label _clanBattleLabel = null!;
    private int _clanBattleToken;

    private static readonly Color ClanBattleGold = UiTheme.GoldBright;

    private void ClanBattleInit()
    {
        BuildClanBattleBanner();
        Net.I.ClanBattleNotifyEvent += OnClanBattleNotify;
        Net.I.ClanBattlePointsEvent += OnClanBattlePoints;
    }

    private void ClanBattleDispose()
    {
        Net.I.ClanBattleNotifyEvent -= OnClanBattleNotify;
        Net.I.ClanBattlePointsEvent -= OnClanBattlePoints;
    }

    private void BuildClanBattleBanner()
    {
        _clanBattleLayer = new CanvasLayer { Layer = 67, Visible = false };
        AddChild(_clanBattleLayer);

        var panel = new PanelContainer
        {
            AnchorLeft = 0.5f, AnchorRight = 0.5f, AnchorTop = 0, AnchorBottom = 0,
            GrowHorizontal = Control.GrowDirection.Both,
            OffsetTop = 204,
        };
        panel.AddThemeStyleboxOverride("panel", UiTheme.Panel(7, true));
        _clanBattleLayer.AddChild(panel);

        var m = new MarginContainer();
        UiTheme.Margins(m, 18, 9, 18, 9);
        panel.AddChild(m);

        _clanBattleLabel = UiTheme.Text("", 16, ClanBattleGold, HorizontalAlignment.Center);
        _clanBattleLabel.AddThemeConstantOverride("outline_size", 5);
        m.AddChild(_clanBattleLabel);
    }

    private void ShowClanBattleBanner(string text, Color colour)
    {
        _clanBattleLabel.Text = text;
        _clanBattleLabel.AddThemeColorOverride("font_color", colour);
        _clanBattleLayer.Visible = true;

        int token = ++_clanBattleToken;
        double secs = Mathf.Clamp(3.5 + text.Length * 0.045, 4.0, 12.0);
        GetTree().CreateTimer(secs).Timeout += () => { if (_clanBattleToken == token) _clanBattleLayer.Visible = false; };
    }

    private void OnClanBattleNotify()
    {
        const string line = "Clan battle status has changed.";
        ShowClanBattleBanner(line, ClanBattleGold);
        Chat.Append($"[color=#46d3c0][lb]clan battle[rb] [/color][color=#ecd9a6]{BbCode.Esc(line)}[/color]");
    }

    private void OnClanBattlePoints(int sub)
    {
        var (line, disband) = ClanBattleMessage(sub);
        ShowClanBattleBanner(line, disband ? new Color("ff8a5c") : ClanBattleGold);
        var colHex = disband ? "ff9a6a" : "ecd9a6";
        Chat.Append($"[color=#46d3c0][lb]clan battle[rb] [/color][color=#{colHex}]{BbCode.Esc(line)}[/color]");
    }

    private static (string Line, bool Disband) ClanBattleMessage(int sub) => sub switch
    {
        0 => ("Your clan has been disbanded.", true),
        1 => ("A clan battle has been declared.", false),
        2 => ("The clan battle has begun!", false),
        3 => ("Clan battle score updated.", false),
        4 => ("The clan battle has ended.", false),
        5 => ("The clan battle is in progress.", false),
        _ => ("Clan battle status has changed.", false),
    };
}
