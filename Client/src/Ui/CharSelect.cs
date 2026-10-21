using Godot;

namespace LibreKO;

public partial class CharSelect : Node3D
{
    private readonly Dictionary<Button, CharacterSummary> buttons = new();
    private readonly List<CharacterSummary> characters = new();

    private Node3D characterAnchor = null!;
    private Node3D? characterModel;
    private readonly Dictionary<string, Node3D> modelCache = new();
    private const double CharListTimeoutSeconds = 20;
    private int charListAttempts;
    private bool charListArrived;
    private VBoxContainer characterList = null!;
    private Label status = null!;
    private Button enterButton = null!;
    private Button backButton = null!;
    private Button createButton = null!;
    private PanelContainer selectPanel = null!;

    private CharacterSummary? selected;
    private bool returningToServers;
    private bool draggingCharacter;

    public override void _Ready()
    {
        Ui.MenuScale(true);
        BuildStage();
        BuildOverlay();
        Audio.BgmFile(Sfx.BgmIntroFile);

        Game.Session.PreGame.CharListEvent += OnCharList;
        Game.Session.PreGame.EnterWorldEvent += OnEnterWorld;
        Game.Session.PreGame.RequiredNameChangeEvent += OnRequiredNameChange;
        Game.Connection.ErrorEvent += OnError;
        Game.LoginServer.LoginResultEvent += OnReturnLoginResult;
        Game.LoginServer.ErrorEvent += OnLoginServerError;

        status.Text = "Loading characters…";
        RequestCharListWithTimeout();
        Callable.From(() => GlyphWarmer.Warm(this)).CallDeferred();
        GetTree().CreateTimer(MemoryCensusDelaySeconds).Timeout += LogMemoryCensus;
    }

    private const double MemoryCensusDelaySeconds = 3;

    private void LogMemoryCensus()
    {
        if (IsInsideTree()) MemoryCensus.Log("character select");
    }

    public override void _Process(double delta) => TickStage(delta);

    private static int PanelWidth => Platform.Pick(380, 430);
    private const float PointerStageMargin = 340f;

    private void BuildOverlay()
    {
        var canvas = new CanvasLayer { Layer = UiLayers.CharSelectScreen };
        AddChild(canvas);

        var overlay = new Control { MouseFilter = Control.MouseFilterEnum.Ignore };
        overlay.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        canvas.AddChild(overlay);

        var panel = selectPanel = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Stop };
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

        status = Ui.Legend("", 13, UiTheme.TextLo);
        status.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        content.AddChild(status);

        var scroll = new ScrollContainer
        {
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
        };
        content.AddChild(scroll);

        characterList = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        characterList.AddThemeConstantOverride("separation", 8);
        scroll.AddChild(characterList);

        enterButton = Ui.MenuButton("Enter World", 46, 20);
        enterButton.Disabled = true;
        enterButton.Pressed += EnterSelectedCharacter;
        content.AddChild(enterButton);

        var group = Ui.ActionGroup(content,
            ("Create Character", OpenCreate), ("Back to servers", ReturnToServerSelection));
        createButton = group[0];
        backButton = group[1];

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
            Text = $"{CharacterLevelText.Of(c.Level, c.Rebirth)}  ·  {ClassLabel(c.Class)}",
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        line.AddThemeFontSizeOverride("font_size", 13);
        line.AddThemeColorOverride("font_color", UiTheme.TextHi);
        vb.AddChild(line);

        buttons[card] = c;
        ApplyCharacterButtonStyle(card, isSelected: false);
        return card;
    }

    private Control BuildEmptySlotCard()
    {
        var card = new Button
        {
            CustomMinimumSize = new Vector2(0, 70),
            FocusMode = Control.FocusModeEnum.None,
            Modulate = EmptySlotTint,
        };
        card.Pressed += OpenCreate;

        var text = new Label
        {
            Text = EmptySlotText,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        text.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        text.AddThemeFontSizeOverride("font_size", 14);
        text.AddThemeColorOverride("font_color", UiTheme.TextLo);
        card.AddChild(text);

        ApplyCharacterButtonStyle(card, isSelected: false);
        return card;
    }

    private const string EmptySlotText = "Empty slot  ·  Create a character";
    private static readonly Color EmptySlotTint = new(1f, 1f, 1f, 0.6f);

    private static int NationOf(int race) => race >= 11 ? Nations.ElMorad : Nations.Karus;

    private const int CharListMaxAttempts = 2;

    private void RequestCharListWithTimeout()
    {
        charListAttempts++;
        Game.Requests.PreGame.CharacterList();
        GetTree().CreateTimer(CharListTimeoutSeconds).Timeout += OnCharListTimeout;
    }

    private void OnCharListTimeout()
    {
        if (charListArrived || !IsInsideTree() || returningToServers) return;
        if (charListAttempts < CharListMaxAttempts)
        {
            status.Text = "The character list is late, asking again…";
            RequestCharListWithTimeout();
            return;
        }
        status.Text = "The character list did not arrive. Go back to the server list and try again.";
    }

    private void DetachCharacterModel()
    {
        if (characterModel == null || !GodotObject.IsInstanceValid(characterModel)) return;
        if (modelCache.ContainsValue(characterModel)) characterModel.GetParent()?.RemoveChild(characterModel);
        else characterModel.QueueFree();
        characterModel = null;
    }

    private void ClearModelCache()
    {
        foreach (var model in modelCache.Values)
            if (GodotObject.IsInstanceValid(model) && model != characterModel) model.QueueFree();
        modelCache.Clear();
    }

    private void OnCharList(List<CharacterSummary> characters)
    {
        charListArrived = true;
        DetachCharacterModel();
        ClearModelCache();
        foreach (Node child in characterList.GetChildren())
            child.QueueFree();
        buttons.Clear();
        this.characters.Clear();
        selected = null;

        foreach (var character in characters)
            if (!character.Empty)
                this.characters.Add(character);

        if (this.characters.Count == 0)
        {
            status.Text = "No characters yet — create one.";
            enterButton.Disabled = true;
            createButton.Disabled = false;
            OpenCreate();
            return;
        }

        foreach (var character in this.characters)
            characterList.AddChild(BuildCharacterCard(character));
        for (int slot = this.characters.Count; slot < MaxSlots; slot++)
            characterList.AddChild(BuildEmptySlotCard());

        createButton.Disabled = this.characters.Count >= MaxSlots;
        SelectCharacter(this.characters[0]);
    }

    private void SelectCharacter(CharacterSummary character)
    {
        if (returningToServers) return;
        selected = character;
        status.Text = characters.Count == 1 ? "1 character on this account."
                                             : $"{characters.Count} characters on this account.";
        enterButton.Disabled = false;

        foreach (var (button, entry) in buttons)
            ApplyCharacterButtonStyle(button, ReferenceEquals(entry, character));

        DetachCharacterModel();
        if (modelCache.TryGetValue(character.Name, out var cached) && GodotObject.IsInstanceValid(cached))
        {
            characterModel = cached;
            characterAnchor.RotationDegrees = new Vector3(0, stageStandYaw, 0);
            characterAnchor.Scale = Vector3.One;
            characterAnchor.AddChild(cached);
            return;
        }

        int hair = (int)character.Hair;
        characterModel = CharacterPreview.Build(character.Race, character.Face, character.Gear,
            HairCode.StyleOf(hair), HairCode.TintOf(hair));

        if (characterModel == null)
        {
            status.Text = $"Character assets are unavailable for race {character.Race}.";
            return;
        }

        modelCache[character.Name] = characterModel;
        characterModel.Position = Vector3.Zero;
        characterModel.Rotation = Vector3.Zero;
        characterAnchor.RotationDegrees = new Vector3(0, stageStandYaw, 0);
        characterAnchor.Scale = Vector3.One;
        characterAnchor.AddChild(characterModel);
        FrameCharacterWhenReady(characterModel);
    }

    private async void FrameCharacterWhenReady(Node3D model)
    {
        for (int i = 0; i < 3; i++)
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

        if (characterModel != model || !GodotObject.IsInstanceValid(model))
            return;

        if (!TryModelBounds(model, out var bounds) || bounds.Size.Y < 0.05f)
            return;

        Vector3 center = bounds.GetCenter();
        model.Position = new Vector3(-center.X, -bounds.Position.Y, -center.Z);

        var tween = CreateTween();
        characterAnchor.Scale = new Vector3(0.96f, 0.96f, 0.96f);
        tween.TweenProperty(characterAnchor, "scale", Vector3.One, 0.24)
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
                if (!mouseButton.Pressed) { draggingCharacter = false; return; }
                if (mouseButton.Position.X >= uiStart || ColourPopupOpen()) return;
                draggingCharacter = true;
                GetViewport().SetInputAsHandled();
            }
            else if (mouseButton.Pressed
                     && mouseButton.ButtonIndex is MouseButton.WheelUp or MouseButton.WheelDown
                     && createPanel.Visible
                     && mouseButton.Position.X < uiStart)
            {
                ZoomCreateCamera(mouseButton.ButtonIndex == MouseButton.WheelUp ? -1 : 1);
                GetViewport().SetInputAsHandled();
            }
        }
        else if (draggingCharacter && inputEvent is InputEventMouseMotion motion)
        {
            characterAnchor.RotateY(motion.Relative.X * 0.01f);
            GetViewport().SetInputAsHandled();
        }
    }

    private LoadingScreen? entering;
    private double enterStartedAt;

    private void EnterSelectedCharacter()
    {
        if (selected == null || returningToServers) return;
        if (selected.NameChangeRequired)
        {
            AskRequiredName(selected);
            return;
        }
        if (!Game.Connection.Connected)
        {
            Game.Connection.LeaveToLogin(Net.ConnectionLostText);
            return;
        }
        status.Text = $"Entering world as {selected.Name}…";
        SetActionsDisabled(true);

        enterStartedAt = FrameClock.Now();
        GD.Print("[enter] requesting character");
        entering = new LoadingScreen();
        AddChild(entering);
        entering.Set("Entering world…", 0.04, selected.Name);

        Game.Requests.PreGame.SelectCharacter(selected.Name);
    }

    private void AskRequiredName(CharacterSummary character)
    {
        Notice.AskText(this, ItemData.Text(RequiredNameChange.PromptText, RequiredNameChange.PromptFallback), name =>
        {
            name = name.Trim();
            if (name.Length == 0) return;
            if (!RequiredNameChange.IsAlphanumeric(name))
            {
                Notice.Show(this, ItemData.Text(RequiredNameChange.NotAlphanumericText, RequiredNameChange.NotAlphanumericFallback),
                    onClose: () => AskRequiredName(character));
                return;
            }
            string confirm = RequiredNameChange.ConfirmLine(ItemData.Text(RequiredNameChange.ConfirmText, RequiredNameChange.ConfirmFallback), name);
            Notice.Confirm(this, confirm, "Yes", "No", () => Game.Requests.PreGame.ChangeRequiredName(character.Slot, character.Name, name));
        }, maxLength: RequiredNameChange.MaxLength);
    }

    private void OnRequiredNameChange(SelectingCharacterNameChangeResult result, int slot, string name)
    {
        if (RequiredNameChange.ResultText(result) is { } text) Notice.Show(this, ItemData.Text(text.Id, text.Fallback));
        if (result == SelectingCharacterNameChangeResult.Success) Game.Requests.PreGame.CharacterList();
    }

    private void CancelEnteringOverlay()
    {
        if (entering == null) return;
        entering.QueueFree();
        entering = null;
        Backdrop.RerollLoadingArt();
    }

    private void ReturnToServerSelection()
    {
        if (returningToServers) return;
        returningToServers = true;
        SetActionsDisabled(true);
        status.Text = "Returning to server selection…";

        Game.Connection.Disconnect(expected: true);

        if (!Game.LoginServer.ReconnectForServerSelection())
        {
            returningToServers = false;
            GetTree().ChangeSceneToFile("res://scenes/Login.tscn");
        }
    }

    private void OnReturnLoginResult(bool ok, int result)
    {
        if (!returningToServers) return;
        if (!ok)
        {
            returningToServers = false;
            SetActionsDisabled(false);
            status.Text = $"Could not return to server list (login code {result}).";
            return;
        }
        Login.StartAtServers = true;
        GetTree().ChangeSceneToFile("res://scenes/Login.tscn");
    }

    private void OnLoginServerError(string error)
    {
        if (!returningToServers) return;
        returningToServers = false;
        SetActionsDisabled(false);
        status.Text = "Login server error: " + error;
    }

    private void SetActionsDisabled(bool disabled)
    {
        enterButton.Disabled = disabled;
        backButton.Disabled = disabled;
        createButton.Disabled = disabled || characters.Count >= MaxSlots;
        foreach (var button in buttons.Keys)
            button.Disabled = disabled;
    }

    private static void ApplyCharacterButtonStyle(Button button, bool isSelected)
    {
        var normal = new StyleBoxFlat
        {
            BgColor = isSelected ? new Color(0.20f, 0.155f, 0.065f, 0.92f)
                               : new Color(0.055f, 0.053f, 0.060f, 0.80f),
            BorderColor = isSelected ? UiTheme.Gold : UiTheme.EdgeSoft,
        };
        normal.SetBorderWidthAll(isSelected ? 2 : 1);
        normal.SetCornerRadiusAll(5);
        button.AddThemeStyleboxOverride("normal", normal);

        var hover = (StyleBoxFlat)normal.Duplicate();
        hover.BgColor = isSelected ? new Color(0.25f, 0.19f, 0.08f, 0.95f)
                                 : new Color(0.10f, 0.095f, 0.085f, 0.88f);
        hover.BorderColor = isSelected ? UiTheme.GoldBright : new Color(UiTheme.Gold, 0.5f);
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
        double now = FrameClock.Now();
        GD.Print($"[enter] server replied (+{now - enterStartedAt:0.0}s), swapping scene");
        entering?.Set("Entering world…", 0.08, info.Name);
        GetTree().ChangeSceneToFile("res://scenes/World.tscn");
    }

    private void OnError(string error)
    {
        CancelEnteringOverlay();
        if (!returningToServers && !Game.Connection.Connected)
        {
            Game.Connection.LeaveToLogin(Net.ConnectionLostText);
            return;
        }
        if (!returningToServers)
        {
            SetActionsDisabled(false);
            status.Text = "Error: " + error;
        }
    }

    public override void _ExitTree()
    {
        if (stageFxLayer != null && IsInstanceValid(stageFxLayer)) stageFxLayer.QueueFree();
        stageFxLayer = null;
        ClearModelCache();
        Game.Session.PreGame.CreateCharResultEvent -= OnCreateResult;
        Game.Session.PreGame.RequiredNameChangeEvent -= OnRequiredNameChange;
        Game.Session.PreGame.CharListEvent -= OnCharList;
        Game.Session.PreGame.EnterWorldEvent -= OnEnterWorld;
        Game.Connection.ErrorEvent -= OnError;
        Game.LoginServer.LoginResultEvent -= OnReturnLoginResult;
        Game.LoginServer.ErrorEvent -= OnLoginServerError;
    }
}
