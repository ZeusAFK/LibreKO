using Godot;
using LibreKO.Domain;
using LibreKO.Network;

namespace LibreKO;

public partial class World
{
    private CanvasLayer _upgradeNoticeLayer = null!;
    private Label _upgradeNoticeLabel = null!;
    private int _upgradeNoticeToken;

    private static readonly Color UpgradeNoticeGold = UiTheme.GoldBright;
    private static readonly Color UpgradeNoticeFail = new("ff8a5c");

    private void UpgradeNoticeInit()
    {
        BuildUpgradeNoticeBanner();
        Net.I.UpgradeNoticeWireUp();
        Net.I.UpgradeNoticeEvent += OnUpgradeNotice;
    }

    private void UpgradeNoticeDispose()
    {
        Net.I.UpgradeNoticeEvent -= OnUpgradeNotice;
    }

    private void BuildUpgradeNoticeBanner()
    {
        _upgradeNoticeLayer = new CanvasLayer { Layer = 69, Visible = false };
        AddChild(_upgradeNoticeLayer);

        var panel = new PanelContainer
        {
            AnchorLeft = 0.5f, AnchorRight = 0.5f, AnchorTop = 0, AnchorBottom = 0,
            GrowHorizontal = Control.GrowDirection.Both,
            OffsetTop = 200,
        };
        panel.AddThemeStyleboxOverride("panel", UiTheme.Panel(7, true));
        _upgradeNoticeLayer.AddChild(panel);

        var m = new MarginContainer();
        UiTheme.Margins(m, 18, 9, 18, 9);
        panel.AddChild(m);

        _upgradeNoticeLabel = UiTheme.Text("", 16, UpgradeNoticeGold, HorizontalAlignment.Center);
        _upgradeNoticeLabel.AddThemeConstantOverride("outline_size", 5);
        m.AddChild(_upgradeNoticeLabel);
    }

    private void ShowUpgradeNoticeBanner(string text, Color colour)
    {
        _upgradeNoticeLabel.Text = text;
        _upgradeNoticeLabel.AddThemeColorOverride("font_color", colour);
        _upgradeNoticeLayer.Visible = true;

        int token = ++_upgradeNoticeToken;
        double secs = Mathf.Clamp(3.5 + text.Length * 0.045, 4.0, 12.0);
        GetTree().CreateTimer(secs).Timeout += () =>
        {
            if (_upgradeNoticeToken == token) _upgradeNoticeLayer.Visible = false;
        };
    }

    private void OnUpgradeNotice(bool ok, string name, int itemId)
    {
        string item = ItemData.DisplayName(itemId);
        if (string.IsNullOrEmpty(item)) item = $"item {itemId}";
        string verb = ok ? "successfully upgraded" : "failed to upgrade";
        string line = $"{name} has {verb} {item}!";

        ShowUpgradeNoticeBanner(line, ok ? UpgradeNoticeGold : UpgradeNoticeFail);
        string colHex = ok ? "ffd98a" : "ff9a6a";
        Chat.Append($"[color=#c8a45a][lb]upgrade[rb] [/color][color=#{colHex}]{BbCode.Esc(line)}[/color]");
    }

    public void WatchUpgrade(int itemId) => Net.I.SendWatchUpgrade(itemId);
}
