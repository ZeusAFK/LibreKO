using Godot;
using LibreKO.Domain;
using LibreKO.Network;

namespace LibreKO;

public partial class World
{
    private CanvasLayer _shoutLayer = null!;
    private Label _shoutLabel = null!;
    private int _shoutToken;

    private static readonly Color ShoutGold = UiTheme.GoldBright;
    private static readonly Color ShoutKarus = new("00bfff");
    private static readonly Color ShoutElmorad = new("f08080");

    private void ShoutInit()
    {
        BuildShoutBanner();
        Net.I.ShoutEvent += OnShout;
        Net.I.ShoutUpgradeEvent += OnShoutUpgrade;
        Net.I.ShoutRareItemEvent += OnShoutRareItem;
        Net.I.ShoutResultEvent += OnShoutResult;
    }

    private void ShoutDispose()
    {
        Net.I.ShoutEvent -= OnShout;
        Net.I.ShoutUpgradeEvent -= OnShoutUpgrade;
        Net.I.ShoutRareItemEvent -= OnShoutRareItem;
        Net.I.ShoutResultEvent -= OnShoutResult;
    }

    private void BuildShoutBanner()
    {
        _shoutLayer = new CanvasLayer { Layer = 68, Visible = false };
        AddChild(_shoutLayer);

        var panel = new PanelContainer
        {
            AnchorLeft = 0.5f, AnchorRight = 0.5f, AnchorTop = 0, AnchorBottom = 0,
            GrowHorizontal = Control.GrowDirection.Both,
            OffsetTop = 150,
        };
        panel.AddThemeStyleboxOverride("panel", UiTheme.Panel(7, true));
        _shoutLayer.AddChild(panel);

        var m = new MarginContainer();
        UiTheme.Margins(m, 18, 9, 18, 9);
        panel.AddChild(m);

        _shoutLabel = UiTheme.Text("", 16, ShoutGold, HorizontalAlignment.Center);
        _shoutLabel.AddThemeConstantOverride("outline_size", 5);
        m.AddChild(_shoutLabel);
    }

    private void ShowShoutBanner(string text, Color colour)
    {
        _shoutLabel.Text = text;
        _shoutLabel.AddThemeColorOverride("font_color", colour);
        _shoutLayer.Visible = true;

        int token = ++_shoutToken;
        double secs = Mathf.Clamp(3.5 + text.Length * 0.045, 4.0, 12.0);
        GetTree().CreateTimer(secs).Timeout += () => { if (_shoutToken == token) _shoutLayer.Visible = false; };
    }

    private void OnShout(string message, byte r, byte g, byte b, int rank)
    {
        if (message.Length == 0) return;
        var colour = new Color(r / 255f, g / 255f, b / 255f);
        float lum = 0.2126f * colour.R + 0.7152f * colour.G + 0.0722f * colour.B;
        if (lum < 0.18f) colour = ShoutGold;

        ShowShoutBanner(message, colour);
        Chat.Append($"[color=#ff9a3c][lb]shout[rb] [/color][color=#ffd98a]{BbCode.Esc(message)}[/color]");
    }

    private void OnShoutUpgrade(bool ok, string name, int itemId, int rank)
    {
        string item = ItemData.DisplayName(itemId);
        if (string.IsNullOrEmpty(item)) item = $"item {itemId}";
        string verb = ok ? "successfully forged" : "failed to forge";
        string line = $"{name} has {verb} {item}!";

        ShowShoutBanner(line, ok ? ShoutGold : new Color("ff8a5c"));
        var colHex = ok ? "ffd98a" : "ff9a6a";
        Chat.Append($"[color=#c8a45a][lb]anvil[rb] [/color][color=#{colHex}]{BbCode.Esc(line)}[/color]");
    }

    private void OnShoutRareItem(string finder, int itemId, byte nation)
    {
        string item = ItemData.DisplayName(itemId);
        if (string.IsNullOrEmpty(item)) item = $"item {itemId}";
        string line = $"{finder} has obtained {item}!";
        var colour = nation == Nations.Karus ? ShoutKarus
            : nation == Nations.ElMorad ? ShoutElmorad : ShoutGold;

        ShowShoutBanner(line, colour);
        Chat.Append($"[color=#c8a45a][lb]rare[rb] [/color][color=#{colour.ToHtml(false)}]{BbCode.Esc(line)}[/color]");
    }

    private void OnShoutResult(int result)
    {
        if (result == Net.ShoutRegisterAccepted) return;

        Chat.Info(result switch
        {
            Net.ShoutRegisterNoItem => "You need a Logos Shout scroll to shout server-wide.",
            Net.ShoutRegisterChatRestricted => "You cannot shout while chat is restricted.",
            Net.ShoutRegisterLevelTooLow => "Shouting is available at level 30 or above.",
            _ => "Could not register your message. Please try again later.",
        });
    }

    public void DoShout(string message)
    {
        message = message?.Trim() ?? "";
        if (message.Length == 0) return;
        Net.I.SendLogoShout(message);
    }
}
