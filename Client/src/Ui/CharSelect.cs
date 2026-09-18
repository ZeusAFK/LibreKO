using System.Collections.Generic;
using Godot;
using LibreKO.Domain;

namespace LibreKO;

public partial class CharSelect : Node3D
{
    private readonly Dictionary<Button, CharacterSummary> _buttons = new();
    private readonly List<CharacterSummary> _characters = new();

    private Node3D _characterAnchor = null!;
    private Node3D? _characterModel;
    private VBoxContainer _characterList = null!;
    private Label _status = null!;
    private Button _enterButton = null!;
    private Button _backButton = null!;
    private Button _createButton = null!;
    private PanelContainer _selectPanel = null!;

    private CharacterSummary? _selected;
    private bool _returningToServers;
    private bool _draggingCharacter;

    public override void _Ready()
    {
        Ui.MenuScale(true);
        BuildStage();
        BuildOverlay();
        Audio.BgmFile(Sfx.BgmIntroFile);

        Net.I.CharListEvent += OnCharList;
        Net.I.EnterWorldEvent += OnEnterWorld;
        Net.I.ErrorEvent += OnError;
        LoginNet.I.LoginResultEvent += OnReturnLoginResult;
        LoginNet.I.ErrorEvent += OnLoginServerError;

        _status.Text = "Loading characters…";
        Net.I.RequestCharList();
    }

    public override void _Process(double delta) => TickStage(delta);

    private static int PanelWidth => Platform.Pick(380, 430);
    private const float PointerStageMargin = 340f;

    private void BuildOverlay()
    {
        var canvas = new CanvasLayer { Layer = 10 };
        AddChild(canvas);

        var overlay = new Control { MouseFilter = Control.MouseFilterEnum.Ignore };
        overlay.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        canvas.AddChild(overlay);

        var panel = _selectPanel = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Stop };
        panel.SetAnchorsPreset(Control.LayoutPreset.RightWide);
        panel.OffsetLeft = -PanelWidth;
        var panelStyle = new StyleBoxFlat
        {
            BgColor = new Color(0.02f, 0.02f, 0.028f, 0.78f),
            BorderColor = new Color(UiTheme.Gold, 0.35f),
        };
        panelStyle.BorderWidthLeft = 1;
        panel.AddThemeStyleboxOverride("panel", panelStyle);
        overlay.AddChild(panel);

        var margin = new MarginContainer();
        foreach (var side in new[] { "left", "right", "top" })
            margin.AddThemeConstantOverride($"margin_{side}", 16);
        margin.AddThemeConstantOverride("margin_bottom", 22);
        panel.AddChild(margin);

        var content = new VBoxContainer();
        content.AddThemeConstantOverride("separation", 10);
        margin.AddChild(content);

        content.AddChild(Ui.Legend("Select Character", 22, UiTheme.GoldBright));

        _status = Ui.Legend("", 13, UiTheme.TextLo);
        _status.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        content.AddChild(_status);

        var scroll = new ScrollContainer
        {
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
        };
        content.AddChild(scroll);

        _characterList = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _characterList.AddThemeConstantOverride("separation", 8);
        scroll.AddChild(_characterList);

        _enterButton = Ui.MenuButton("Enter World", 46, 20);
        _enterButton.Disabled = true;
        _enterButton.Pressed += EnterSelectedCharacter;
        content.AddChild(_enterButton);

        var group = Ui.ActionGroup(content,
            ("Create Character", OpenCreate), ("Back to servers", ReturnToServerSelection));
        _createButton = group[0];
        _backButton = group[1];

        BuildCreatePanel(overlay);
    }

    internal void PreviewCharacters(List<CharacterSummary> characters) => OnCharList(characters);

    private Control BuildCharacterCard(CharacterSummary c)
    {
        var card = new Button
        {
            CustomMinimumSize = new Vector2(0, 70),
            FocusMode = Control.FocusModeEnum.None,
        };
        card.Pressed += () => SelectCharacter(c);

        var body = new MarginContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        body.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        foreach (var side in new[] { "left", "right" }) body.AddThemeConstantOverride($"margin_{side}", 12);
        foreach (var side in new[] { "top", "bottom" }) body.AddThemeConstantOverride($"margin_{side}", 11);
        card.AddChild(body);

        var vb = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        vb.AddThemeConstantOverride("separation", 4);
        body.AddChild(vb);

        var name = new Label { Text = c.Name, MouseFilter = Control.MouseFilterEnum.Ignore };
        name.AddThemeFontSizeOverride("font_size", 18);
        name.AddThemeColorOverride("font_color", UiTheme.Gold);
        vb.AddChild(name);

        var line = new Label
        {
            Text = $"Level {c.Level}  ·  {ClassLabel(c.Class)}",
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        line.AddThemeFontSizeOverride("font_size", 13);
        line.AddThemeColorOverride("font_color", UiTheme.TextHi);
        vb.AddChild(line);

        _buttons[card] = c;
        ApplyCharacterButtonStyle(card, selected: false);
        return card;
    }

    private static int NationOf(int race) => race >= 11 ? Nations.ElMorad : Nations.Karus;

    private void OnCharList(List<CharacterSummary> characters)
    {
        foreach (Node child in _characterList.GetChildren())
            child.QueueFree();
        _buttons.Clear();
        _characters.Clear();
        _selected = null;

        foreach (var character in characters)
            if (!character.Empty)
                _characters.Add(character);

        if (_characters.Count == 0)
        {
            _status.Text = "No characters yet — create one.";
            _enterButton.Disabled = true;
            _createButton.Disabled = false;
            OpenCreate();
            return;
        }

        foreach (var character in _characters)
            _characterList.AddChild(BuildCharacterCard(character));

        _createButton.Disabled = _characters.Count >= MaxSlots;
        SelectCharacter(_characters[0]);
    }

    private void SelectCharacter(CharacterSummary character)
    {
        if (_returningToServers) return;
        _selected = character;
        _status.Text = _characters.Count == 1 ? "1 character on this account."
                                             : $"{_characters.Count} characters on this account.";
        _enterButton.Disabled = false;

        foreach (var (button, entry) in _buttons)
            ApplyCharacterButtonStyle(button, ReferenceEquals(entry, character));

        if (_characterModel != null && GodotObject.IsInstanceValid(_characterModel))
            _characterModel.QueueFree();
        int hair = (int)character.Hair;
        _characterModel = CharacterPreview.Build(character.Race, character.Face, character.Gear,
            HairCode.StyleOf(hair), HairCode.TintOf(hair));

        if (_characterModel == null)
        {
            _status.Text = $"Character assets are unavailable for race {character.Race}.";
            return;
        }

        _characterModel.Position = Vector3.Zero;
        _characterModel.Rotation = Vector3.Zero;
        _characterAnchor.RotationDegrees = new Vector3(0, _stageStandYaw, 0);
        _characterAnchor.Scale = Vector3.One;
        _characterAnchor.AddChild(_characterModel);
        FrameCharacterWhenReady(_characterModel);
    }

    private async void FrameCharacterWhenReady(Node3D model)
    {
        for (int i = 0; i < 3; i++)
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

        if (_characterModel != model || !GodotObject.IsInstanceValid(model))
            return;

        if (!TryModelBounds(model, out var bounds) || bounds.Size.Y < 0.05f)
            return;

        Vector3 center = bounds.GetCenter();
        model.Position = new Vector3(-center.X, -bounds.Position.Y, -center.Z);

        var tween = CreateTween();
        _characterAnchor.Scale = new Vector3(0.96f, 0.96f, 0.96f);
        tween.TweenProperty(_characterAnchor, "scale", Vector3.One, 0.24)
            .SetEase(Tween.EaseType.Out)
            .SetTrans(Tween.TransitionType.Cubic);
    }

    private static bool TryModelBounds(Node3D root, out Aabb bounds)
    {
        bounds = new Aabb();
        bool found = false;
        var rootInverse = root.GlobalTransform.AffineInverse();
        foreach (var mesh in FindAll<MeshInstance3D>(root))
        {
            if (mesh.Mesh == null || !mesh.Visible || IsBoneAttached(mesh, root))
                continue;
            var localBounds = (rootInverse * mesh.GlobalTransform) * mesh.GetAabb();
            bounds = found ? bounds.Merge(localBounds) : localBounds;
            found = true;
        }
        return found;
    }

    private static bool IsBoneAttached(Node node, Node root)
    {
        for (Node? current = node; current != null && current != root; current = current.GetParent())
            if (current is BoneAttachment3D)
                return true;
        return false;
    }

    public override void _UnhandledInput(InputEvent inputEvent)
    {
        float uiStart = GetViewport().GetVisibleRect().Size.X
                        - Platform.Pick(PointerStageMargin, PanelWidth);
        if (inputEvent is InputEventMouseButton mouseButton)
        {
            if (mouseButton.ButtonIndex == MouseButton.Left)
            {
                if (!mouseButton.Pressed) { _draggingCharacter = false; return; }
                if (mouseButton.Position.X >= uiStart || ColourPopupOpen()) return;
                _draggingCharacter = true;
                GetViewport().SetInputAsHandled();
            }
            else if (mouseButton.Pressed
                     && mouseButton.ButtonIndex is MouseButton.WheelUp or MouseButton.WheelDown
                     && _createPanel.Visible
                     && mouseButton.Position.X < uiStart)
            {
                ZoomCreateCamera(mouseButton.ButtonIndex == MouseButton.WheelUp ? -1 : 1);
                GetViewport().SetInputAsHandled();
            }
        }
        else if (_draggingCharacter && inputEvent is InputEventMouseMotion motion)
        {
            _characterAnchor.RotateY(motion.Relative.X * 0.01f);
            GetViewport().SetInputAsHandled();
        }
    }

    private LoadingScreen? _entering;
    private double _enterStartedAt;

    private void EnterSelectedCharacter()
    {
        if (_selected == null || _returningToServers) return;
        _status.Text = $"Entering world as {_selected.Name}…";
        SetActionsDisabled(true);

        _enterStartedAt = Time.GetTicksMsec() / 1000.0;
        GD.Print("[enter] requesting character");
        _entering = new LoadingScreen();
        AddChild(_entering);
        _entering.Set("Entering world…", 0.04, _selected.Name);

        Net.I.SelectChar(_selected.Name);
    }

    private void CancelEnteringOverlay()
    {
        if (_entering == null) return;
        _entering.QueueFree();
        _entering = null;
        Backdrop.RerollLoadingArt();
    }

    private void ReturnToServerSelection()
    {
        if (_returningToServers) return;
        _returningToServers = true;
        SetActionsDisabled(true);
        _status.Text = "Returning to server selection…";

        Net.I.Disconnect(expected: true);

        if (!LoginNet.I.ReconnectForServerSelection())
        {
            _returningToServers = false;
            GetTree().ChangeSceneToFile("res://scenes/Login.tscn");
        }
    }

    private void OnReturnLoginResult(bool ok, int result)
    {
        if (!_returningToServers) return;
        if (!ok)
        {
            _returningToServers = false;
            SetActionsDisabled(false);
            _status.Text = $"Could not return to server list (login code {result}).";
            return;
        }
        Login.StartAtServers = true;
        GetTree().ChangeSceneToFile("res://scenes/Login.tscn");
    }

    private void OnLoginServerError(string error)
    {
        if (!_returningToServers) return;
        _returningToServers = false;
        SetActionsDisabled(false);
        _status.Text = "Login server error: " + error;
    }

    private void SetActionsDisabled(bool disabled)
    {
        _enterButton.Disabled = disabled;
        _backButton.Disabled = disabled;
        _createButton.Disabled = disabled || _characters.Count >= MaxSlots;
        foreach (var button in _buttons.Keys)
            button.Disabled = disabled;
    }

    private static void ApplyCharacterButtonStyle(Button button, bool selected)
    {
        var normal = new StyleBoxFlat
        {
            BgColor = selected ? new Color(0.20f, 0.155f, 0.065f, 0.92f)
                               : new Color(0.055f, 0.053f, 0.060f, 0.80f),
            BorderColor = selected ? UiTheme.Gold : UiTheme.EdgeSoft,
        };
        normal.SetBorderWidthAll(selected ? 2 : 1);
        normal.SetCornerRadiusAll(5);
        button.AddThemeStyleboxOverride("normal", normal);

        var hover = (StyleBoxFlat)normal.Duplicate();
        hover.BgColor = selected ? new Color(0.25f, 0.19f, 0.08f, 0.95f)
                                 : new Color(0.10f, 0.095f, 0.085f, 0.88f);
        hover.BorderColor = selected ? UiTheme.GoldBright : new Color(UiTheme.Gold, 0.5f);
        button.AddThemeStyleboxOverride("hover", hover);
        button.AddThemeStyleboxOverride("pressed", hover);
        button.AddThemeStyleboxOverride("disabled", normal);
        button.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
    }

    private static string ClassLabel(int characterClass)
        => CharacterClassCatalog.DisplayName(characterClass);

    private static IEnumerable<T> FindAll<T>(Node node) where T : class
    {
        if (node is T match)
            yield return match;
        foreach (Node child in node.GetChildren())
            foreach (var descendant in FindAll<T>(child))
                yield return descendant;
    }

    private void OnEnterWorld(MyInfo info)
    {
        double now = Time.GetTicksMsec() / 1000.0;
        GD.Print($"[enter] server replied (+{now - _enterStartedAt:0.0}s), swapping scene");
        _entering?.Set("Entering world…", 0.08, info.Name);
        GetTree().ChangeSceneToFile("res://scenes/World.tscn");
    }

    private void OnError(string error)
    {
        CancelEnteringOverlay();
        if (!_returningToServers)
        {
            SetActionsDisabled(false);
            _status.Text = "Error: " + error;
        }
    }

    public override void _ExitTree()
    {
        Net.I.CreateCharResultEvent -= OnCreateResult;
        Net.I.CharListEvent -= OnCharList;
        Net.I.EnterWorldEvent -= OnEnterWorld;
        Net.I.ErrorEvent -= OnError;
        LoginNet.I.LoginResultEvent -= OnReturnLoginResult;
        LoginNet.I.ErrorEvent -= OnLoginServerError;
    }
}
