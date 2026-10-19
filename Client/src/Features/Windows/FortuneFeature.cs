using Godot;

namespace LibreKO.Features.Windows;

internal sealed class FortuneFeature : IFeature, IClosableWindow
{
    private readonly IWorldContext context;

    public FortuneFeature(IWorldContext context)
    {
        this.context = context;
    }

    private const string Dir = "res://assets/ui/fortune/";
    private const string DeckFile = Dir + "fortune.json";
    private const string BackFile = Dir + "back.png";
    private const string BackHoverFile = Dir + "back_hover.png";
    private const string StarIcon = "system/star";
    private const string Greeting = "Let's read your stars for the day.";
    private const string Prompt = "Please pick a Tarot Card.";
    private const string RevealText = "This card has your Fortune of the Day!";
    private const float CanvasWidth = 820f;
    private const float CanvasHeight = 450f;
    private const float CardWidth = 147f;
    private const float CardHeight = 196f;
    private const float CardStep = 30f;
    private const float CardTop = 34f;
    private const float CardLift = 16f;
    private const float FaceWidth = 178f;
    private const float FaceHeight = 236f;
    private const float FaceTop = 34f;
    private const float StarSize = 18f;
    private const double SpinFps = 24;
    private const double FinalFps = 30;

    internal FortuneDeck Deck { get; private set; } = FortuneDeck.Empty;
    private CanvasLayer layer = null!;
    internal HudWindow Panel { get; private set; } = null!;
    internal Control Pick { get; private set; } = null!;
    internal Control Stage { get; private set; } = null!;
    private readonly List<TextureButton> cards = new();
    private TextureRect face = null!;
    private Flipbook spin = null!;
    internal Flipbook Final { get; private set; } = null!;
    private HBoxContainer stars = null!;
    private Label reveal = null!;
    private Label name = null!;

    private readonly List<Label> lines = new();
    private Button okButton = null!;
    private bool shown;

    public bool IsOpen => shown;

    internal FortuneReading? Reading { get; set; }

    public void Build()
    {
        Deck = Godot.FileAccess.FileExists(DeckFile)
            ? Fortune.Parse(Godot.FileAccess.GetFileAsString(DeckFile))
            : FortuneDeck.Empty;
        BuildPanel();
    }

    public void Dispose()
    {
    }

    private static Texture2D? Texture(string? file) =>
        string.IsNullOrEmpty(file) ? null : GD.Load<Texture2D>(Dir + file);

    private Texture2D[] Frames(string set) =>
        Deck.FramesOf(set).Select(Texture).Where(t => t != null).Cast<Texture2D>().ToArray();

    private void BuildPanel()
    {
        layer = new CanvasLayer { Layer = UiLayers.Windows };
        context.Root.AddChild(layer);
        Panel = new HudWindow("fortune", "Fortune Teller", bodyMinWidth: (int)CanvasWidth) { Visible = false };
        Panel.Closed += Close;
        layer.AddChild(Panel);

        var canvas = new Control { CustomMinimumSize = new Vector2(CanvasWidth, CanvasHeight), ClipContents = true };
        Panel.Body.AddChild(canvas);

        Pick = new Control();
        Pick.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        canvas.AddChild(Pick);
        var back = GD.Load<Texture2D>(BackFile);
        var backHover = GD.Load<Texture2D>(BackHoverFile);
        float fanLeft = (CanvasWidth - ((Fortune.CardsShown - 1) * CardStep + CardWidth)) / 2f;
        for (int i = 0; i < Fortune.CardsShown; i++)
        {
            int index = i;
            var card = new TextureButton
            {
                TextureNormal = back,
                TextureHover = backHover,
                TexturePressed = backHover,
                IgnoreTextureSize = true,
                StretchMode = TextureButton.StretchModeEnum.Scale,
                FocusMode = Control.FocusModeEnum.None,
                Position = new Vector2(fanLeft + i * CardStep, CardTop),
                Size = new Vector2(CardWidth, CardHeight),
            };
            card.MouseEntered += () => card.Position = card.Position with { Y = CardTop - CardLift };
            card.MouseExited += () => card.Position = card.Position with { Y = CardTop };
            card.Pressed += () => PickCard(index);
            Pick.AddChild(card);
            cards.Add(card);
        }
        Pick.AddChild(Line(Greeting, 18, UiTheme.GoldBright, CardTop + CardHeight + 40f));
        Pick.AddChild(Line(Prompt, 15, UiTheme.TextLo, CardTop + CardHeight + 70f));

        Stage = new Control { Visible = false };
        Stage.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        canvas.AddChild(Stage);
        var faceRect = new Rect2((CanvasWidth - FaceWidth) / 2f, FaceTop, FaceWidth, FaceHeight);
        face = new TextureRect
        {
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.Scale,
            Position = faceRect.Position,
            Size = faceRect.Size,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        Stage.AddChild(face);
        spin = new Flipbook { Position = faceRect.Position, Size = faceRect.Size };
        Stage.AddChild(spin);
        Final = new Flipbook
        {
            Position = faceRect.Position,
            Size = faceRect.Size,
            Material = new CanvasItemMaterial { BlendMode = CanvasItemMaterial.BlendModeEnum.Add },
        };
        Stage.AddChild(Final);

        stars = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center, MouseFilter = Control.MouseFilterEnum.Ignore };
        stars.AddThemeConstantOverride("separation", 3);
        stars.Position = new Vector2(0, FaceTop - StarSize - 6f);
        stars.Size = new Vector2(CanvasWidth, StarSize);
        Stage.AddChild(stars);

        float below = FaceTop + FaceHeight;
        name = Line("", 16, UiTheme.GoldBright, below + 8f);
        Stage.AddChild(name);
        reveal = Line(RevealText, 16, UiTheme.TextLo, below + 40f);
        Stage.AddChild(reveal);
        for (int i = 0; i < 3; i++)
        {
            var line = Line("", 15, UiTheme.TextHi, below + 36f + i * 24f);
            lines.Add(line);
            Stage.AddChild(line);
        }
        okButton = UiTheme.ActionButton("OK", "Close the reading");
        okButton.Size = new Vector2(110, 30);
        okButton.Position = new Vector2((CanvasWidth - 110f) / 2f, CanvasHeight - 40f);
        okButton.Pressed += Close;
        Stage.AddChild(okButton);
    }

    private static Label Line(string text, int size, Color color, float y)
    {
        var label = UiTheme.Text(text, size, color, HorizontalAlignment.Center);
        label.Position = new Vector2(0, y);
        label.Size = new Vector2(CanvasWidth, size + 8f);
        label.MouseFilter = Control.MouseFilterEnum.Ignore;
        return label;
    }

    internal void Open()
    {
        Reading = null;
        Pick.Visible = true;
        Stage.Visible = false;
        spin.Stop();
        Final.Stop();
        foreach (var card in cards) card.Position = card.Position with { Y = CardTop };
        shown = true;
        Panel.Visible = true;
    }

    private void PickCard(int index)
    {
        if (Fortune.Draw(Deck.Readings, Random.Shared.Next) is not { } reading) return;
        this.Reading = reading;
        Pick.Visible = false;
        Stage.Visible = true;
        face.Texture = GD.Load<Texture2D>(BackFile);
        name.Visible = false;
        stars.Visible = false;
        foreach (var line in lines) line.Visible = false;
        reveal.Visible = true;
        okButton.Visible = false;
        spin.Play(Frames(Fortune.SpinFrames), SpinFps, () => Reveal(reading));
    }

    internal void Reveal(FortuneReading reading)
    {
        spin.Stop();
        face.Texture = Texture(Deck.CardFor(reading.Category));
        Final.Play(Frames(Fortune.FinalFrames), FinalFps, () => Final.Stop());

        foreach (var child in stars.GetChildren()) child.QueueFree();
        for (int i = 0; i < Math.Clamp(reading.Level, 0, Fortune.MaxStars); i++)
        {
            stars.AddChild(new TextureRect
            {
                Texture = UiIcons.Get(StarIcon),
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                CustomMinimumSize = new Vector2(StarSize, StarSize),
                Modulate = UiTheme.GoldVivid,
            });
        }
        stars.Visible = true;
        name.Text = reading.Title;
        name.Visible = true;
        reveal.Visible = false;
        for (int i = 0; i < lines.Count; i++)
        {
            lines[i].Text = i < reading.Lines.Length ? reading.Lines[i] : "";
            lines[i].Visible = true;
        }
        okButton.Visible = true;
    }

    public void Close()
    {
        if (!shown) return;
        shown = false;
        Panel.Visible = false;
        spin.Stop();
        Final.Stop();
        if (this.Reading is { } reading && SkillData.Get(Fortune.EffectSkill(reading.Category)) is { TargetFx: { } fx })
            context.Entities.SpawnFxOn(context.Self.CharId, fx, 0f);
        this.Reading = null;
    }
}
