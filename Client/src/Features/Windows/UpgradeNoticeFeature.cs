using Godot;

namespace LibreKO.Features.Windows;

internal sealed class UpgradeNoticeFeature : IFeature
{
    private readonly IWorldContext context;
    private readonly UpgradeNoticeSession upgradeNotice;

    public UpgradeNoticeFeature(IWorldContext context, UpgradeNoticeSession upgradeNotice)
    {
        this.context = context;
        this.upgradeNotice = upgradeNotice;
    }

    private CanvasLayer layer = null!;

    private Label label = null!;
    private int token;

    private static readonly Color Gold = UiTheme.GoldBright;
    private static readonly Color Fail = new("ff8a5c");

    public void Build()
    {
        BuildBanner();
        upgradeNotice.UpgradeNoticeEvent += On;
    }

    public void Dispose()
    {
        upgradeNotice.UpgradeNoticeEvent -= On;
    }

    private void BuildBanner()
    {
        layer = new CanvasLayer { Layer = UiLayers.ChatOverlays, Visible = false };
        context.Root.AddChild(layer);

        var panel = new PanelContainer
        {
            AnchorLeft = 0.5f, AnchorRight = 0.5f, AnchorTop = 0, AnchorBottom = 0,
            GrowHorizontal = Control.GrowDirection.Both,
            OffsetTop = 200,
        };
        panel.AddThemeStyleboxOverride("panel", UiTheme.Panel(7, true));
        layer.AddChild(panel);

        var m = new MarginContainer();
        UiTheme.Margins(m, 18, 9, 18, 9);
        panel.AddChild(m);

        label = UiTheme.Text("", 16, Gold, HorizontalAlignment.Center);
        label.AddThemeConstantOverride("outline_size", 5);
        m.AddChild(label);
    }

    internal void ShowBanner(string text, Color colour)
    {
        label.Text = text;
        label.AddThemeColorOverride("font_color", colour);
        layer.Visible = true;

        int token = ++this.token;
        double secs = Mathf.Clamp(3.5 + text.Length * 0.045, 4.0, 12.0);
        context.Root.After(secs, () =>
        {
            if (this.token == token) layer.Visible = false;
        });
    }

    private void On(bool ok, string name, int itemId)
    {
        string item = ItemData.DisplayName(itemId);
        if (string.IsNullOrEmpty(item)) item = $"item {itemId}";
        string verb = ok ? "successfully upgraded" : "failed to upgrade";
        string line = $"{name} has {verb} {item}!";

        ShowBanner(line, ok ? Gold : Fail);
        context.Log.Add(line, CombatLogKind.Status);
    }
}
