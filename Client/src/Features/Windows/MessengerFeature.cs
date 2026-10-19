using Godot;

namespace LibreKO.Features.Windows;

internal sealed class MessengerFeature : IFeature, IEscapable
{
    private readonly IWorldContext context;
    private readonly MessengerSession messenger;
    private readonly IMessengerRequests requests;

    public MessengerFeature(IWorldContext context, MessengerSession messenger, IMessengerRequests requests)
    {
        this.context = context;
        this.messenger = messenger;
        this.requests = requests;
    }

    private CanvasLayer msgrLayer = null!;
    private HudWindow msgrPanel = null!;
    private VBoxContainer msgrList = null!;
    private LineEdit msgrToInput = null!;
    private LineEdit msgrTextInput = null!;
    private bool msgrShown;

    public void Build()
    {
        msgrLayer = new CanvasLayer { Layer = UiLayers.Windows };
        context.Root.AddChild(msgrLayer);
        msgrPanel = new HudWindow("messenger", "Messenger") { Visible = false };
        msgrPanel.Closed += Close;
        msgrLayer.AddChild(msgrPanel);
        var root = msgrPanel.Body;
        root.AddThemeConstantOverride("separation", 6);

        root.AddChild(UiTheme.SectionTitle("Online"));
        var scroll = new ScrollContainer { CustomMinimumSize = new Vector2(320, 240), HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        root.AddChild(scroll);
        msgrList = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        msgrList.AddThemeConstantOverride("separation", 3);
        scroll.AddChild(msgrList);

        root.AddChild(UiTheme.SectionTitle("Quick Whisper"));
        msgrToInput = new LineEdit { PlaceholderText = "To (name)", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, MaxLength = 20 };
        root.AddChild(msgrToInput);
        var sendRow = new HBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        sendRow.AddThemeConstantOverride("separation", 6);
        root.AddChild(sendRow);
        msgrTextInput = new LineEdit { PlaceholderText = "Message", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, MaxLength = 128 };
        msgrTextInput.TextSubmitted += _ => DoSend();
        sendRow.AddChild(msgrTextInput);
        var sendBtn = new Button { Text = "Send", FocusMode = Control.FocusModeEnum.None };
        sendBtn.Pressed += DoSend;
        sendRow.AddChild(sendBtn);

        messenger.MessengerListEvent += OnList;
    }

    bool IEscapable.CloseOnEscape()
    {
        if (msgrShown) Close();
        else return false;
        return true;
    }

    public void Dispose()
    {
        messenger.MessengerListEvent -= OnList;
    }

    internal void Toggle()
    {
        if (msgrShown) { Close(); return; }
        msgrPanel.Visible = true;
        msgrShown = true;
        requests.List();
    }

    internal void Close()
    {
        if (!msgrShown) return;
        msgrShown = false;
        msgrPanel.Visible = false;
    }

    private void DoSend()
    {
        string to = msgrToInput.Text.Trim();
        string text = msgrTextInput.Text.Trim();
        if (to.Length == 0 || text.Length == 0) return;
        requests.Message(to, text);
        msgrTextInput.Clear();
    }

    private void OnList(List<MessengerBuddy> list)
    {
        UiKit.ClearChildren(msgrList);
        foreach (var b in list)
        {
            var row = new PanelContainer();
            row.AddThemeStyleboxOverride("panel", UiTheme.Row());
            var hb = new HBoxContainer(); hb.AddThemeConstantOverride("separation", 8);
            row.AddChild(hb);
            var name = UiTheme.Text(b.Name, 13, b.Online ? UiTheme.TextHi : UiTheme.TextLo);
            name.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            hb.AddChild(name);
            hb.AddChild(UiTheme.Text(b.Online ? "Online" : "Offline", 12, b.Online ? UiTheme.Gold : UiTheme.TextLo));
            string targetName = b.Name;
            var whisper = new Button { Text = "Whisper", FocusMode = Control.FocusModeEnum.None };
            whisper.Pressed += () => { msgrToInput.Text = targetName; msgrTextInput.GrabFocus(); };
            hb.AddChild(whisper);
            msgrList.AddChild(row);
        }
        if (msgrList.GetChildCount() == 0)
        {
            var e = HudStyle.Label(13); e.Text = "No one else online.";
            msgrList.AddChild(e);
        }
    }
}
