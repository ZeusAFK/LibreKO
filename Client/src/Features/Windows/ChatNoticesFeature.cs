using Godot;

namespace LibreKO.Features.Windows;

internal sealed class ChatNoticesFeature : IFeature, ITickFeature
{
    private readonly IWorldContext context;
    private readonly ChatSession chat;
    private readonly SelfSession self;

    public ChatNoticesFeature(IWorldContext context, ChatSession chat, SelfSession self)
    {
        this.context = context;
        this.chat = chat;
        this.self = self;
    }

    private const int TextQuestFailed = 7456;
    private const int TextTopSecretCaptured = 7457;
    private const int TextTopSecretLost = 7458;
    private const int TextTopSecretExpired = 7459;
    private const int TextMonumentCaptured = 7460;
    internal const int TextMonumentLost = 7461;
    private const int TextSpyRescueStarted = 7462;
    private const int TextSpyRescueDone = 7463;
    private const int TextEnemySpyRescueStarted = 7464;
    private const int TextEnemySpyRescueDone = 7465;
    private const int TextEnemySuspicious = 7466;
    private const int TextEnemiesInvaded = 7467;
    internal const int TextChaosStoneBroken = 7470;
    private const int TextChaosStoneBrokenByEnemy = 7471;
    private const int TextBoxItem = 17000;
    private const int AlertInvaded = 1;
    private const int AlertSuspicious = 2;
    internal static readonly Color OrderMessageRed = new(1f, 0f, 0f);

    private static readonly Color BoxItemNoticeColour = new("e70df2");

    internal CanvasLayer? OrderMessageLayer { get; private set; }
    private Label? orderMessage;
    private OrderMessage orderLook;
    private double orderShownAt = double.NegativeInfinity;

    public void Tick(double delta, double nowSec) => OrderMessageTick(nowSec);

    public void Build()
    {
        chat.MonumentNoticeEvent += OnMonumentNotice;
        chat.ChaosStoneNoticeEvent += OnChaosStoneNotice;
        chat.BoxItemNoticeEvent += OnBoxItemNotice;
    }

    public void Dispose()
    {
        chat.MonumentNoticeEvent -= OnMonumentNotice;
        chat.ChaosStoneNoticeEvent -= OnChaosStoneNotice;
        chat.BoxItemNoticeEvent -= OnBoxItemNotice;
    }

    private void OnMonumentNotice(MonumentNotice notice)
    {
        bool own = notice.Nation == self.Nation;
        switch (notice.Kind)
        {
            case MonumentNoticeKind.TopSecretCaptured:
                ShowOrderMessage(own ? NoticeText(TextTopSecretCaptured, "<%s>captured enemy nation's Top-Secret successfully", notice.Name)
                    : NoticeText(TextTopSecretLost, "Top-Secret has been captured by enemy Nation"), OrderMessageRed, OrderMessage.Flash);
                break;
            case MonumentNoticeKind.QuestFailed:
                ShowOrderMessage(NoticeText(TextQuestFailed, "%s Quest failed", QuestData.Name(notice.Value, self.LastEnter.Name)),
                    OrderMessageRed, OrderMessage.FlashOnce);
                break;
            case MonumentNoticeKind.TopSecretExpired:
                ShowOrderMessage(NoticeText(TextTopSecretExpired, "Enemy Nation's Top-secret capturing time has been expired"),
                    OrderMessageRed, OrderMessage.Flash);
                break;
            case MonumentNoticeKind.MonumentCaptured:
                ShowOrderMessage(own ? NoticeText(TextMonumentCaptured, "<%s> has captured center Monument", notice.Name)
                    : NoticeText(TextMonumentLost, "Enemy nation has captured center Monument"), UiKit.OrderMessageYellow, OrderMessage.Hold);
                Audio.PlayUi(Sfx.TowerDestroyed);
                break;
            case MonumentNoticeKind.SpyRescue:
                ShowOrderMessage(SpyRescueText(own, notice.Done, notice.Name), OrderMessageRed, OrderMessage.Hold);
                break;
            case MonumentNoticeKind.Alert when notice.Value is AlertInvaded or AlertSuspicious:
                ShowOrderMessage(notice.Value == AlertInvaded
                    ? NoticeText(TextEnemiesInvaded, "Enemies invaded")
                    : NoticeText(TextEnemySuspicious, "Enemy's activity is suspicious"), OrderMessageRed, OrderMessage.Flash);
                break;
        }
    }

    private static string SpyRescueText(bool own, bool done, string name) => (own, done) switch
    {
        (true, false) => NoticeText(TextSpyRescueStarted, "<%s>is carrying out the rescue spy operation", name),
        (true, true) => NoticeText(TextSpyRescueDone, "<%s>rescued %d number of spy successfully", name),
        (false, false) => NoticeText(TextEnemySpyRescueStarted, "<%s>is attempting to rescue the spy", name),
        _ => NoticeText(TextEnemySpyRescueDone, "Enemy nation has rescued the spy succesfully"),
    };

    private void OnChaosStoneNotice(ChaosStoneNotice notice) =>
        ShowOrderMessage(notice.Nation == self.Nation
            ? notice.Breaker + NoticeText(TextChaosStoneBroken, "You have destroyed the [Summoning Stone of Chaos] and opened the Gates of Chaos.")
            : NoticeText(TextChaosStoneBrokenByEnemy, "The enemy has destroyed the [Summoning Stone of Chaos] and opened the Gates of Chaos."),
            OrderMessageRed, OrderMessage.Flash);

    private void OnBoxItemNotice(BoxItemNotice notice) =>
        context.Log.Notice(NoticeText(TextBoxItem, "From %s, %s has acquired the %s item.",
            ItemData.DisplayName(ItemData.BaseId(notice.BoxItem)), notice.Name, ItemData.DisplayName(ItemData.BaseId(notice.Item))),
            BoxItemNoticeColour);

    internal static string NoticeText(int id, string fallback, params object[] args) =>
        TextTemplate.Fill(ItemData.Text(id, fallback), args);

    private void ShowOrderMessage(string text, Color colour, OrderMessage look) =>
        PlaceOrderMessage(text, colour, look, context.Root.GetViewport().GetVisibleRect().Size.X);

    internal void PlaceOrderMessage(string text, Color colour, OrderMessage look, float viewWidth)
    {
        if (orderMessage == null || !GodotObject.IsInstanceValid(orderMessage))
        {
            OrderMessageLayer = new CanvasLayer { Layer = UiLayers.Notices };
            context.Root.AddChild(OrderMessageLayer);
            orderMessage = new Label
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                MouseFilter = Control.MouseFilterEnum.Ignore,
            };
            orderMessage.SetAnchorsPreset(Control.LayoutPreset.TopWide);
            orderMessage.OffsetTop = OrderMessage.Top;
            orderMessage.AddThemeFontSizeOverride("font_size", OrderMessage.FontSize);
            orderMessage.AddThemeColorOverride("font_shadow_color", Colors.Black);
            orderMessage.AddThemeConstantOverride("shadow_offset_x", 1);
            orderMessage.AddThemeConstantOverride("shadow_offset_y", 1);
            OrderMessageLayer.AddChild(orderMessage);
        }

        var label = orderMessage;
        float textWidth = label.GetThemeFont("font").GetStringSize(text, HorizontalAlignment.Left, -1, OrderMessage.FontSize).X;
        label.Text = OrderMessage.Split(text, textWidth > viewWidth);
        label.AddThemeColorOverride("font_color", colour);
        orderLook = look;
        orderShownAt = context.Now;
        label.Visible = true;
    }

    internal void OrderMessageTick(double now)
    {
        if (orderMessage == null || !orderMessage.Visible && now - orderShownAt >= orderLook.TotalSeconds) return;
        orderMessage.Visible = orderLook.VisibleAt((float)(now - orderShownAt));
    }
}
