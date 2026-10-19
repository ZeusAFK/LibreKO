using Godot;

namespace LibreKO.Features.Hud;

internal sealed class ShoutFeature : IFeature
{
    private readonly IWorldContext context;
    private readonly ShoutSession shout;
    private readonly IShoutRequests requests;

    public ShoutFeature(IWorldContext context, ShoutSession shout, IShoutRequests requests)
    {
        this.context = context;
        this.shout = shout;
        this.requests = requests;
    }

    private CanvasLayer layer = null!;

    private Label label = null!;
    private int token;

    private static readonly Color Gold = UiTheme.GoldBright;
    private static readonly Color Karus = new("00bfff");
    private static readonly Color Elmorad = new("f08080");

    public void Build()
    {
        BuildBanner();
        shout.ShoutEvent += On;
        shout.ShoutUpgradeEvent += OnUpgrade;
        shout.ShoutRareItemEvent += OnRareItem;
        shout.ShoutResultEvent += OnResult;
    }

    public void Dispose()
    {
        shout.ShoutEvent -= On;
        shout.ShoutUpgradeEvent -= OnUpgrade;
        shout.ShoutRareItemEvent -= OnRareItem;
        shout.ShoutResultEvent -= OnResult;
    }

    private void BuildBanner()
    {
        layer = new CanvasLayer { Layer = UiLayers.Announcements, Visible = false };
        context.Root.AddChild(layer);

        var panel = new PanelContainer
        {
            AnchorLeft = 0.5f, AnchorRight = 0.5f, AnchorTop = 0, AnchorBottom = 0,
            GrowHorizontal = Control.GrowDirection.Both,
            OffsetTop = 150,
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

    private void ShowBanner(string text, Color colour)
    {
        label.Text = text;
        label.AddThemeColorOverride("font_color", colour);
        layer.Visible = true;

        int token = ++this.token;
        double secs = Mathf.Clamp(3.5 + text.Length * 0.045, 4.0, 12.0);
        context.Root.After(secs, () => { if (this.token == token) layer.Visible = false; });
    }

    private void On(ShoutReply reply)
    {
        if (reply.Message.Length == 0) return;
        var colour = new Color(reply.R / 255f, reply.G / 255f, reply.B / 255f);
        float lum = 0.2126f * colour.R + 0.7152f * colour.G + 0.0722f * colour.B;
        if (lum < 0.18f) colour = Gold;

        ShowBanner(reply.Message, colour);
        context.Chat.AppendShout(reply.Message);
    }

    private const int TextRegistered = 7687;
    private const int TextRetryLater = 7688;
    private const int TextFailed = 7690;
    private const int TextNoItem = 2302;
    private const int TextLevel = 30302;
    private const int TextRestricted = 30303;
    private const int TextRareItem = 30534;
    private const int TextUpgrade = 30535;
    private const int TextUpgradeFail = 30536;
    private static readonly Color ResultColour = new(1f, 1f, 0f);
    private static readonly Color AnnouncementColour = Color.Color8(229, 204, 255);

    private void OnUpgrade(bool ok, string name, int itemId, int rank)
    {
        string template = ok
            ? GameText.System(TextUpgrade, "%s has successfully upgraded to the %s item. Please congratulate them.")
            : GameText.System(TextUpgradeFail, "%s failed to upgrade the %s item. Please send them your support.");
        string line = TextTemplate.Fill(template, name, AnnouncedItem(itemId));
        ShowBanner(line, ok ? Gold : new Color("ff8a5c"));
        context.Log.Add(line, CombatLogKind.Status, AnnouncementColour, AnnouncedMarkup(template, name, itemId));
    }

    private void OnRareItem(string finder, int itemId, byte nation)
    {
        string line = TextTemplate.Fill(
            GameText.System(TextRareItem, "%s has obtained the %s item. Please congratulate them."),
            finder, AnnouncedItem(itemId));
        var colour = nation == Nations.Karus ? Karus
            : nation == Nations.ElMorad ? Elmorad : Gold;
        ShowBanner(line, colour);
        context.Log.Add(line, CombatLogKind.Status, AnnouncementColour,
            AnnouncedMarkup(GameText.System(TextRareItem, "%s has obtained the %s item. Please congratulate them."), finder, itemId));
    }

    private const string LogItemMeta = "i:";

    private static string AnnouncedMarkup(string template, string name, int itemId) =>
        TextTemplate.Fill(BbCode.Esc(template), BbCode.Esc(name),
            $"[url={LogItemMeta}{itemId}][color=#{ItemGrade.Tint(itemId).ToHtml(false)}][lb]{BbCode.Esc(AnnouncedItem(itemId))}[rb][/color][/url]");

    internal void OnLogMetaHover(Variant meta)
    {
        string text = meta.AsString();
        if (text.StartsWith(LogItemMeta, System.StringComparison.Ordinal) && int.TryParse(text[LogItemMeta.Length..], out int itemId))
            context.Tooltips.ShowItem(itemId);
    }

    private static string AnnouncedItem(int itemId) =>
        ItemData.DisplayName(itemId) is { Length: > 0 }

    name ? name : $"item {itemId}";

    private void OnResult(LogosShoutRegisterResult result, int number)
    {
        string text = result switch
        {
            LogosShoutRegisterResult.Accepted => TextTemplate.Fill(
                GameText.System(TextRegistered, "Your message has been registered as number %d."), number),
            LogosShoutRegisterResult.LevelTooLow => GameText.System(TextLevel, "Available at level 30 or above"),
            LogosShoutRegisterResult.ChatRestricted => GameText.System(TextRestricted, "Cannot be used while in chat restriction status."),
            LogosShoutRegisterResult.RetryLater => GameText.System(TextRetryLater, "Cannot register message. Please try again later."),
            LogosShoutRegisterResult.NoItem => GameText.System(TextNoItem, "Not enough items left in the  inventory."),
            _ => GameText.System(TextFailed, "Message registration failed."),
        };
        context.Log.Add(text, CombatLogKind.Status, ResultColour);
    }

    public void Do(string message)
    {
        message = message?.Trim() ?? "";
        if (message.Length == 0) return;
        requests.LogoShout(message);
    }
}
