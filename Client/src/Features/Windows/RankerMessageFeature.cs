using Godot;

namespace LibreKO.Features.Windows;

internal sealed class RankerMessageFeature : IFeature
{
    private readonly IWorldContext context;
    private readonly RankerMessageSession rankerMessage;
    private readonly SelfSession self;
    private readonly IRankerMessageRequests requests;

    public RankerMessageFeature(IWorldContext context, RankerMessageSession rankerMessage, SelfSession self, IRankerMessageRequests requests)
    {
        this.context = context;
        this.rankerMessage = rankerMessage;
        this.self = self;
        this.requests = requests;
    }

    private const string PaperKarus = "res://assets/ui/ranker/paper_karus.png";
    private const string PaperElMorad = "res://assets/ui/ranker/paper_elmorad.png";
    private const int NameFont = 14;
    private const int TextFont = 12;
    private static readonly Vector2 PaperSize = new(466, 499);
    private static readonly Rect2 NameRect = new(69, 118, 304, 21);
    private static readonly Rect2 TextRect = new(72, 166, 294, 228);
    private static readonly Rect2 CountRect = new(296, 396, 70, 22);
    private static readonly Rect2 EditRect = new(80, 421, 91, 28);
    private static readonly Rect2 SendRect = new(174, 421, 91, 28);
    private static readonly Rect2 CloseRect = new(271, 422, 91, 28);
    private static readonly Color RefusalColour = new(1f, 0f, 0f);

    internal byte Type { get; set; }
    internal string Name { get; set; } = "";
    internal string Text { get; set; } = "";
    private string pending = "";
    private double writeReadyAt;
    private CanvasLayer? layer;
    internal Control Paper { get; private set; } = null!;
    private TextureRect art = null!;
    private Label nameLabel = null!;
    private Label textLabel = null!;
    private Label count = null!;
    private TextEdit edit = null!;
    private Button editBtn = null!;
    private Button sendBtn = null!;

    public void Build()
    {
        rankerMessage.RankerPaperEvent += OnPaper;
        rankerMessage.RankerResultEvent += OnResult;
    }

    public void Dispose()
    {
        rankerMessage.RankerPaperEvent -= OnPaper;
        rankerMessage.RankerResultEvent -= OnResult;
        layer = null;
    }

    internal bool TryOpenStatue(EntityInfo e)
    {
        if (!e.Statue) return false;
        if (Coord.FlatDistance(context.Self.Body.Position, e.Position) > RankerMessage.Reach) return true;
        Type = (byte)e.NpcType;
        requests.RankerView(Type);
        return true;
    }

    private bool IsMe => Name.Length > 0 && Name == (self.LastEnter.Name ?? "");

    private void OnPaper(string name, string message)
    {
        this.Name = name;
        Text = message;
        if (IsMe) ShowMenu();
        else ShowPaper();
    }

    private void ShowMenu()
    {
        context.NpcDialog.Begin(ItemData.Text(RankerMessage.TitleText, "[Ranker Statue]"),
            ItemData.Text(RankerMessage.MenuTipText, ""));
        context.NpcDialog.AddMenuButton($"1.   {ItemData.Text(RankerMessage.RedressText, "Ranker Statue Equipment Change (once per day)")}",
            () => { context.NpcDialog.Close(); requests.RankerRedress(Type); });
        context.NpcDialog.AddMenuButton($"2.   {ItemData.Text(RankerMessage.WriteText, "Write Ranker Message")}",
            () => { context.NpcDialog.Close(); ShowPaper(); });
        context.NpcDialog.End(2);
        context.NpcDialog.Show();
    }

    internal void ShowPaper()
    {
        if (layer == null || !GodotObject.IsInstanceValid(layer)) BuildPaper();
        art.Texture = GD.Load<Texture2D>(RankerMessage.Karus(Type) ? PaperKarus : PaperElMorad);
        nameLabel.Text = Name;
        textLabel.Text = Text;
        SetEditing(false);
        if (context.Root.GetViewport() is { } viewport)
            Paper.Position = (viewport.GetVisibleRect().Size - PaperSize) / 2f;
        layer!.Visible = true;
    }

    private void ClosePaper()
    {
        if (layer != null && GodotObject.IsInstanceValid(layer)) layer.Visible = false;
    }

    private void SetEditing(bool editing)
    {
        textLabel.Visible = !editing;
        edit.Visible = editing;
        count.Visible = editing;
        sendBtn.Visible = editing;
        editBtn.Visible = !editing;
        if (!editing) return;
        edit.Text = Text;
        RefreshCount();
        edit.GrabFocus();
    }

    private void OnEditPressed()
    {
        if (!IsMe)
        {
            Notice.Show(context.Root, ItemData.Text(RankerMessage.NotYoursText, ""));
            return;
        }
        SetEditing(true);
    }

    private void OnSendPressed()
    {
        if (context.Now < writeReadyAt)
        {
            Notice.Show(context.Root, ItemData.Text(RankerMessage.CooldownText, ""), ItemData.Text(RankerMessage.SavedTitleText, "Notice"));
            return;
        }
        pending = edit.Text.Trim();
        writeReadyAt = context.Now + RankerMessage.WriteCooldownSeconds;
        requests.RankerWrite(Type, pending);
    }

    private void RefreshCount()
    {
        if (edit.Text.Length > RankerMessage.MessageMaxLength)
        {
            int caret = Mathf.Min(edit.GetCaretColumn(), RankerMessage.MessageMaxLength);
            edit.Text = edit.Text[..RankerMessage.MessageMaxLength];
            edit.SetCaretColumn(caret);
        }
        count.Text = $"({RankerMessage.MessageMaxLength - edit.Text.Length})";
    }

    private void OnResult(RankerMessageSub sub, short result)
    {
        switch (sub)
        {
            case RankerMessageSub.View:
                Refusal(RankerMessage.ViewTextId(result));
                break;
            case RankerMessageSub.Write when result == RankerMessage.Done:
                this.Text = pending;
                if (layer != null && GodotObject.IsInstanceValid(layer) && layer.Visible)
                {
                    textLabel.Text = this.Text;
                    SetEditing(false);
                }
                Notice.Show(context.Root, ItemData.Text(RankerMessage.WriteTextId(result), ""),
                    ItemData.Text(RankerMessage.SavedTitleText, "Notice"));
                break;
            case RankerMessageSub.Write:
                Refusal(RankerMessage.WriteTextId(result));
                break;
            case RankerMessageSub.Redress:
                if (ItemData.Text(RankerMessage.RedressTextId(result), "") is { Length: > 0 } text)
                    Notice.Show(context.Root, text);
                break;
        }
    }

    private void Refusal(int textId)
    {
        if (ItemData.Text(textId, "") is { Length: > 0 } text)
            context.Log.Add(text, CombatLogKind.Status, RefusalColour);
    }

    private void BuildPaper()
    {
        layer = new CanvasLayer { Layer = UiLayers.Windows, Visible = false };
        context.Root.AddChild(layer);
        Paper = new Control { Size = PaperSize, MouseFilter = Control.MouseFilterEnum.Stop };
        layer.AddChild(Paper);

        art = new TextureRect
        {
            Size = PaperSize,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.Scale,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        Paper.AddChild(art);

        nameLabel = Label(NameRect, NameFont, HorizontalAlignment.Center);
        nameLabel.AddThemeColorOverride("font_color", UiTheme.GoldBright);
        textLabel = Label(TextRect, TextFont, HorizontalAlignment.Left);
        textLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        textLabel.VerticalAlignment = VerticalAlignment.Top;
        count = Label(CountRect, TextFont, HorizontalAlignment.Right);

        edit = new TextEdit
        {
            Position = TextRect.Position,
            Size = TextRect.Size,
            WrapMode = TextEdit.LineWrappingMode.Boundary,
            Visible = false,
        };
        edit.AddThemeFontSizeOverride("font_size", TextFont);
        edit.TextChanged += RefreshCount;
        Paper.AddChild(edit);

        editBtn = Button(EditRect, "Edit", OnEditPressed);
        sendBtn = Button(SendRect, "Register", OnSendPressed);
        Button(CloseRect, "Close", ClosePaper);
        HudLayout.Attach(Paper, "ranker_paper", art, null, persist: false);
    }

    private Label Label(Rect2 rect, int fontSize, HorizontalAlignment align)
    {
        var label = new Label
        {
            Position = rect.Position,
            Size = rect.Size,
            HorizontalAlignment = align,
            ClipText = true,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        label.AddThemeFontSizeOverride("font_size", fontSize);
        label.AddThemeColorOverride("font_color", UiTheme.TextHi);
        label.AddThemeColorOverride("font_outline_color", Colors.Black);
        label.AddThemeConstantOverride("outline_size", 3);
        Paper.AddChild(label);
        return label;
    }

    private Button Button(Rect2 rect, string text, System.Action pressed)
    {
        var button = Ui.MenuButton(text, height: (int)rect.Size.Y, fontSize: 11);
        button.Position = rect.Position;
        button.Size = rect.Size;
        button.Pressed += pressed;
        Paper.AddChild(button);
        return button;
    }
}
