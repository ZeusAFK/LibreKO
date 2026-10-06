using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using LibreKO.Domain;
using LibreKO.Network;

namespace LibreKO;

public partial class World
{
    private const string FortuneDir = "res://assets/ui/fortune/";
    private const string FortuneDeckFile = FortuneDir + "fortune.json";
    private const string FortuneBackFile = FortuneDir + "back.png";
    private const string FortuneBackHoverFile = FortuneDir + "back_hover.png";
    private const string FortuneStarIcon = "system/star";
    private const string FortuneGreeting = "Let's read your stars for the day.";
    private const string FortunePrompt = "Please pick a Tarot Card.";
    private const string FortuneRevealText = "This card has your Fortune of the Day!";
    private const float FortuneCanvasWidth = 820f;
    private const float FortuneCanvasHeight = 450f;
    private const float FortuneCardWidth = 147f;
    private const float FortuneCardHeight = 196f;
    private const float FortuneCardStep = 30f;
    private const float FortuneCardTop = 34f;
    private const float FortuneCardLift = 16f;
    private const float FortuneFaceWidth = 178f;
    private const float FortuneFaceHeight = 236f;
    private const float FortuneFaceTop = 34f;
    private const float FortuneStarSize = 18f;
    private const double FortuneSpinFps = 24;
    private const double FortuneFinalFps = 30;

    private FortuneDeck _fortuneDeck = FortuneDeck.Empty;
    private CanvasLayer _fortuneLayer = null!;
    private HudWindow _fortunePanel = null!;
    private Control _fortunePick = null!, _fortuneStage = null!;
    private readonly List<TextureButton> _fortuneCards = new();
    private TextureRect _fortuneFace = null!;
    private Flipbook _fortuneSpin = null!, _fortuneFinal = null!;
    private HBoxContainer _fortuneStars = null!;
    private Label _fortuneReveal = null!, _fortuneName = null!;
    private readonly List<Label> _fortuneLines = new();
    private Button _fortuneOk = null!;
    private bool _fortuneShown;
    private FortuneReading? _fortuneReading;

    private void FortuneInit()
    {
        _fortuneDeck = Godot.FileAccess.FileExists(FortuneDeckFile)
            ? Fortune.Parse(Godot.FileAccess.GetFileAsString(FortuneDeckFile))
            : FortuneDeck.Empty;
        BuildFortunePanel();
    }

    private void FortuneDispose()
    {
    }

    private static Texture2D? FortuneTexture(string? file) =>
        string.IsNullOrEmpty(file) ? null : GD.Load<Texture2D>(FortuneDir + file);

    private Texture2D[] FortuneFrames(string set) =>
        _fortuneDeck.FramesOf(set).Select(FortuneTexture).Where(t => t != null).Cast<Texture2D>().ToArray();

    private void BuildFortunePanel()
    {
        _fortuneLayer = new CanvasLayer { Layer = 74 };
        AddChild(_fortuneLayer);
        _fortunePanel = new HudWindow("fortune", "Fortune Teller", bodyMinWidth: (int)FortuneCanvasWidth) { Visible = false };
        _fortunePanel.Closed += CloseFortune;
        _fortuneLayer.AddChild(_fortunePanel);

        var canvas = new Control { CustomMinimumSize = new Vector2(FortuneCanvasWidth, FortuneCanvasHeight), ClipContents = true };
        _fortunePanel.Body.AddChild(canvas);

        _fortunePick = new Control();
        _fortunePick.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        canvas.AddChild(_fortunePick);
        var back = GD.Load<Texture2D>(FortuneBackFile);
        var backHover = GD.Load<Texture2D>(FortuneBackHoverFile);
        float fanLeft = (FortuneCanvasWidth - ((Fortune.CardsShown - 1) * FortuneCardStep + FortuneCardWidth)) / 2f;
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
                Position = new Vector2(fanLeft + i * FortuneCardStep, FortuneCardTop),
                Size = new Vector2(FortuneCardWidth, FortuneCardHeight),
            };
            card.MouseEntered += () => card.Position = card.Position with { Y = FortuneCardTop - FortuneCardLift };
            card.MouseExited += () => card.Position = card.Position with { Y = FortuneCardTop };
            card.Pressed += () => PickFortuneCard(index);
            _fortunePick.AddChild(card);
            _fortuneCards.Add(card);
        }
        _fortunePick.AddChild(FortuneLine(FortuneGreeting, 18, UiTheme.GoldBright, FortuneCardTop + FortuneCardHeight + 40f));
        _fortunePick.AddChild(FortuneLine(FortunePrompt, 15, UiTheme.TextLo, FortuneCardTop + FortuneCardHeight + 70f));

        _fortuneStage = new Control { Visible = false };
        _fortuneStage.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        canvas.AddChild(_fortuneStage);
        var faceRect = new Rect2((FortuneCanvasWidth - FortuneFaceWidth) / 2f, FortuneFaceTop, FortuneFaceWidth, FortuneFaceHeight);
        _fortuneFace = new TextureRect
        {
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.Scale,
            Position = faceRect.Position,
            Size = faceRect.Size,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _fortuneStage.AddChild(_fortuneFace);
        _fortuneSpin = new Flipbook { Position = faceRect.Position, Size = faceRect.Size };
        _fortuneStage.AddChild(_fortuneSpin);
        _fortuneFinal = new Flipbook
        {
            Position = faceRect.Position,
            Size = faceRect.Size,
            Material = new CanvasItemMaterial { BlendMode = CanvasItemMaterial.BlendModeEnum.Add },
        };
        _fortuneStage.AddChild(_fortuneFinal);

        _fortuneStars = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center, MouseFilter = Control.MouseFilterEnum.Ignore };
        _fortuneStars.AddThemeConstantOverride("separation", 3);
        _fortuneStars.Position = new Vector2(0, FortuneFaceTop - FortuneStarSize - 6f);
        _fortuneStars.Size = new Vector2(FortuneCanvasWidth, FortuneStarSize);
        _fortuneStage.AddChild(_fortuneStars);

        float below = FortuneFaceTop + FortuneFaceHeight;
        _fortuneName = FortuneLine("", 16, UiTheme.GoldBright, below + 8f);
        _fortuneStage.AddChild(_fortuneName);
        _fortuneReveal = FortuneLine(FortuneRevealText, 16, UiTheme.TextLo, below + 40f);
        _fortuneStage.AddChild(_fortuneReveal);
        for (int i = 0; i < 3; i++)
        {
            var line = FortuneLine("", 15, UiTheme.TextHi, below + 36f + i * 24f);
            _fortuneLines.Add(line);
            _fortuneStage.AddChild(line);
        }
        _fortuneOk = UiTheme.ActionButton("OK", "Close the reading");
        _fortuneOk.Size = new Vector2(110, 30);
        _fortuneOk.Position = new Vector2((FortuneCanvasWidth - 110f) / 2f, FortuneCanvasHeight - 40f);
        _fortuneOk.Pressed += CloseFortune;
        _fortuneStage.AddChild(_fortuneOk);
    }

    private static Label FortuneLine(string text, int size, Color color, float y)
    {
        var label = UiTheme.Text(text, size, color, HorizontalAlignment.Center);
        label.Position = new Vector2(0, y);
        label.Size = new Vector2(FortuneCanvasWidth, size + 8f);
        label.MouseFilter = Control.MouseFilterEnum.Ignore;
        return label;
    }

    private void OpenFortune()
    {
        _fortuneReading = null;
        _fortunePick.Visible = true;
        _fortuneStage.Visible = false;
        _fortuneSpin.Stop();
        _fortuneFinal.Stop();
        foreach (var card in _fortuneCards) card.Position = card.Position with { Y = FortuneCardTop };
        _fortuneShown = true;
        _fortunePanel.Visible = true;
    }

    private void PickFortuneCard(int index)
    {
        if (Fortune.Draw(_fortuneDeck.Readings, Random.Shared.Next) is not { } reading) return;
        _fortuneReading = reading;
        _fortunePick.Visible = false;
        _fortuneStage.Visible = true;
        _fortuneFace.Texture = GD.Load<Texture2D>(FortuneBackFile);
        _fortuneName.Visible = false;
        _fortuneStars.Visible = false;
        foreach (var line in _fortuneLines) line.Visible = false;
        _fortuneReveal.Visible = true;
        _fortuneOk.Visible = false;
        _fortuneSpin.Play(FortuneFrames(Fortune.SpinFrames), FortuneSpinFps, () => RevealFortune(reading));
    }

    private void RevealFortune(FortuneReading reading)
    {
        _fortuneSpin.Stop();
        _fortuneFace.Texture = FortuneTexture(_fortuneDeck.CardFor(reading.Category));
        _fortuneFinal.Play(FortuneFrames(Fortune.FinalFrames), FortuneFinalFps, () => _fortuneFinal.Stop());

        foreach (var child in _fortuneStars.GetChildren()) child.QueueFree();
        for (int i = 0; i < Math.Clamp(reading.Level, 0, Fortune.MaxStars); i++)
        {
            _fortuneStars.AddChild(new TextureRect
            {
                Texture = UiIcons.Get(FortuneStarIcon),
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                CustomMinimumSize = new Vector2(FortuneStarSize, FortuneStarSize),
                Modulate = UiTheme.GoldVivid,
            });
        }
        _fortuneStars.Visible = true;
        _fortuneName.Text = reading.Title;
        _fortuneName.Visible = true;
        _fortuneReveal.Visible = false;
        for (int i = 0; i < _fortuneLines.Count; i++)
        {
            _fortuneLines[i].Text = i < reading.Lines.Length ? reading.Lines[i] : "";
            _fortuneLines[i].Visible = true;
        }
        _fortuneOk.Visible = true;
    }

    private void CloseFortune()
    {
        if (!_fortuneShown) return;
        _fortuneShown = false;
        _fortunePanel.Visible = false;
        _fortuneSpin.Stop();
        _fortuneFinal.Stop();
        if (_fortuneReading is { } reading && SkillData.Get(Fortune.EffectSkill(reading.Category)) is { TargetFx: { } fx })
            SpawnFxOn(_myId, fx, 0f);
        _fortuneReading = null;
    }
}
