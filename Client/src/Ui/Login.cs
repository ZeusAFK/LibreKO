using Godot;

namespace LibreKO;

public partial class Login : Control
{
    public static bool StartAtServers;
    public static string PendingNotice = "";

    private static int CardWidth => Platform.Pick(380, 440);
    private static int ServerPanelWidth => Platform.Pick(380, 430);
    private const int FooterHeight = 66;

    private LineEdit user = null!;
    private LineEdit pass = null!;
    private CheckButton remember = null!;
    private VBoxContainer cardBox = null!;
    private VBoxContainer session = null!;
    private Label sessionStatus = null!;
    private Button continueButton = null!;
    private HBoxContainer footer = null!;
    private PanelContainer serverPanel = null!;
    private VBoxContainer serverBox = null!;
    private Label serverStatus = null!;
    private Notice? busy;
    private ServerListEntry? onlyServer;
    private bool loginPending;
    private bool resuming;
    private bool serversReady;

    public override void _Ready()
    {
        Ui.MenuScale(true);
        ItemData.PreloadInBackground();
        Backdrop.RerollLoginArt();
        Ui.Background(this, Backdrop.LoginArt, Backdrop.LoginFit, bottomScrim: Platform.TouchUi);
        Audio.BgmFile(Sfx.BgmIntroFile);

        var split = new HBoxContainer();
        split.SetAnchorsPreset(LayoutPreset.FullRect);
        split.AddThemeConstantOverride("separation", 0);
        AddChild(split);

        var cardArea = new MarginContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        cardArea.AddThemeConstantOverride("margin_bottom", Platform.Pick(0, FooterHeight));
        split.AddChild(cardArea);

        var cardHost = new CenterContainer();
        cardArea.AddChild(cardHost);
        Ui.AutoScale(cardHost);
        BuildCard(cardHost);
        BuildSession(cardHost);

        serverPanel = BuildServerPanel();
        split.AddChild(serverPanel);

        BuildFooter();

        Callable.From(() => GlyphWarmer.Warm(this)).CallDeferred();
        Game.LoginServer.VersionEvent += OnVersion;
        Game.LoginServer.LoginResultEvent += OnLoginResult;
        Game.LoginServer.AccountInUseEvent += OnLoginServerAccountInUse;
        Game.LoginServer.ServerListEvent += OnServerList;
        Game.LoginServer.ErrorEvent += OnError;
        Game.Session.PreGame.LoginResultEvent += OnGameLogin;
        Game.Session.AccountLock.AccountInUseEvent += OnGameAccountInUse;
        Game.Session.AccountLock.KickResultEvent += OnKickResult;
        Game.Connection.ErrorEvent += OnError;

        if (StartAtServers)
        {
            StartAtServers = false;
            EnterServerMode("Loading servers…");
            Game.LoginServer.RequestServerList();
        }
        else if (SavedAccount.Any)
        {
            ResumeSavedSession();
        }

        if (PendingNotice.Length > 0)
        {
            string message = PendingNotice;
            PendingNotice = "";
            Notice.Show(this, message, "Disconnected");
        }
    }

    private void BuildCard(Node parent)
    {
        cardBox = new VBoxContainer { CustomMinimumSize = new Vector2(CardWidth, 0) };
        cardBox.AddThemeConstantOverride("separation", 8);
        parent.AddChild(cardBox);

        cardBox.AddChild(Ui.Legend("Welcome to LibreKO", 30, UiTheme.GoldBright));
        if (Platform.PointerUi)
        {
            cardBox.AddChild(Ui.Legend("Enter your credentials", 15, UiTheme.TextHi));
            cardBox.AddChild(Spacer(10));
        }

        cardBox.AddChild(FieldLabel("Username"));
        cardBox.AddChild(user = new LineEdit { PlaceholderText = "username" });
        cardBox.AddChild(FieldLabel("Password"));
        cardBox.AddChild(pass = new LineEdit { PlaceholderText = "password", Secret = true });
        Ui.StyleField(user);
        Ui.StyleField(pass);
        cardBox.AddChild(BuildRememberRow());
        if (Platform.PointerUi) cardBox.AddChild(Spacer(6));

        AddMenuButton(cardBox, "Login", OnLoginPressed);
        Ui.ActionGroup(cardBox, ("Settings", OpenSettings), ("Exit", OnExit));

        Ui.SoftScrim(cardBox);
        user.Text = SavedAccount.Name;

        user.TextSubmitted += _ => pass.GrabFocus();
        pass.TextSubmitted += _ => OnLoginPressed();
        if (Platform.PointerUi) user.CallDeferred(Control.MethodName.GrabFocus);
    }

    private Control BuildRememberRow()
    {
        remember = new CheckButton
        {
            Text = "Keep me signed in",
            ButtonPressed = SavedAccount.Any,
            FocusMode = FocusModeEnum.None,
            CustomMinimumSize = new Vector2(0, Platform.Pick(30, Ui.TouchButtonHeight)),
            SizeFlagsHorizontal = SizeFlags.ShrinkCenter,
        };
        remember.AddThemeFontSizeOverride("font_size", 15);
        remember.AddThemeColorOverride("font_color", UiTheme.TextLo);
        remember.AddThemeColorOverride("font_pressed_color", UiTheme.GoldBright);
        remember.AddThemeColorOverride("font_hover_color", UiTheme.TextHi);
        remember.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0, 0.9f));
        remember.AddThemeConstantOverride("outline_size", 4);
        remember.AddThemeConstantOverride("h_separation", 14);
        return remember;
    }

    private void BuildSession(Node parent)
    {
        session = new VBoxContainer { CustomMinimumSize = new Vector2(CardWidth, 0), Visible = false };
        session.AddThemeConstantOverride("separation", 8);
        parent.AddChild(session);

        session.AddChild(Ui.Legend("Welcome back", 30, UiTheme.GoldBright));
        session.AddChild(Ui.Legend(SavedAccount.Name, 20, UiTheme.Gold));
        sessionStatus = Ui.Legend("", 14, UiTheme.TextLo);
        sessionStatus.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        sessionStatus.CustomMinimumSize = new Vector2(0, 40);
        session.AddChild(sessionStatus);

        continueButton = AddMenuButton(session, "Continue", OnContinue);
        continueButton.Disabled = true;
        Ui.ActionGroup(session, ("Log out", OnForgetAccount), ("Settings", OpenSettings),
                       ("Close game", OnExit));

        Ui.SoftScrim(session);
    }

    private void ResumeSavedSession()
    {
        resuming = true;
        loginPending = true;
        serversReady = false;
        cardBox.Visible = false;
        session.Visible = true;
        continueButton.Disabled = true;
        sessionStatus.Text = "Signing in…";
        Game.LoginServer.ConnectToLoginServer();
    }

    private void OnContinue()
    {
        if (!serversReady)
        {
            ResumeSavedSession();
            return;
        }
        EnterServerMode("");
        OnServerListShown();
    }

    private void OnServerListShown()
    {
        int count = serverBox.GetChildCount();
        serverStatus.Text = count == 1 ? "1 server available." : $"{count} servers available.";
    }

    private void OnForgetAccount() => SignOut(clearUser: true);

    private static Button AddMenuButton(Node parent, string text, System.Action pressed,
                                        int height = 42, int fontSize = 19)
    {
        var b = Ui.MenuButton(text, height, fontSize);
        b.Pressed += pressed;
        parent.AddChild(b);
        return b;
    }

    private PanelContainer BuildServerPanel()
    {
        var panel = new PanelContainer
        {
            CustomMinimumSize = new Vector2(ServerPanelWidth, 0),
            SizeFlagsVertical = SizeFlags.ExpandFill,
            Visible = false,
        };
        var sb = new StyleBoxFlat
        {
            BgColor = new Color(0.02f, 0.02f, 0.028f, 0.72f),
            BorderColor = new Color(UiTheme.Gold, 0.35f),
        };
        sb.BorderWidthLeft = 1;
        panel.AddThemeStyleboxOverride("panel", sb);

        var margin = new MarginContainer();
        foreach (var s in new[] { "left", "right", "top" })
            margin.AddThemeConstantOverride($"margin_{s}", 16);
        margin.AddThemeConstantOverride("margin_bottom", 22);
        panel.AddChild(margin);

        var vb = new VBoxContainer();
        vb.AddThemeConstantOverride("separation", 10);
        margin.AddChild(vb);

        vb.AddChild(Ui.Legend("Game Servers", 22, UiTheme.GoldBright));

        serverStatus = Ui.Legend("", 13, UiTheme.TextLo);
        serverStatus.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        vb.AddChild(serverStatus);

        var scroll = new ScrollContainer
        {
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
            SizeFlagsVertical = SizeFlags.ExpandFill,
        };
        vb.AddChild(scroll);

        serverBox = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        serverBox.AddThemeConstantOverride("separation", 8);
        scroll.AddChild(serverBox);

        Ui.ActionGroup(vb, ("Settings", OpenSettings), ("Logout", OnLogout), ("Exit game", OnExit));
        return panel;
    }

    private Control BuildServerCard(ServerListEntry s)
    {
        bool full = s.Players < 0;
        int players = full ? s.MaxPlayers : s.Players;
        int max = Mathf.Max(1, (int)s.MaxPlayers);
        float load = Mathf.Clamp(players / (float)max, 0f, 1f);
        var tone = full ? UiTheme.Bad : load > 0.75f ? UiTheme.Neutral : UiTheme.Good;

        var card = new PanelContainer();
        var sb = new StyleBoxFlat
        {
            BgColor = new Color(0.055f, 0.053f, 0.060f, 0.80f),
            BorderColor = UiTheme.EdgeSoft,
        };
        sb.SetBorderWidthAll(1);
        sb.SetCornerRadiusAll(5);
        sb.SetContentMarginAll(10);
        card.AddThemeStyleboxOverride("panel", sb);

        var vb = new VBoxContainer();
        vb.AddThemeConstantOverride("separation", 6);
        card.AddChild(vb);

        var name = new Label { Text = s.Group.Length > 0 ? $"{s.Group}  {s.Name}" : s.Name };
        name.AddThemeFontSizeOverride("font_size", 17);
        name.AddThemeColorOverride("font_color", UiTheme.Gold);
        vb.AddChild(name);

        var meter = new ProgressBar
        {
            MinValue = 0, MaxValue = 1, Value = load,
            ShowPercentage = false,
            CustomMinimumSize = new Vector2(0, 6),
        };
        var track = new StyleBoxFlat { BgColor = UiTheme.SlotBg };
        track.SetCornerRadiusAll(3);
        var fill = new StyleBoxFlat { BgColor = tone };
        fill.SetCornerRadiusAll(3);
        meter.AddThemeStyleboxOverride("background", track);
        meter.AddThemeStyleboxOverride("fill", fill);
        vb.AddChild(meter);

        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 8);
        vb.AddChild(row);

        var pop = new Label
        {
            Text = full ? "FULL" : $"{players} / {max} online",
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            VerticalAlignment = VerticalAlignment.Center,
        };
        pop.AddThemeFontSizeOverride("font_size", 13);
        pop.AddThemeColorOverride("font_color", tone);
        row.AddChild(pop);

        var chosen = s;
        var pick = Ui.MenuButton("Select", height: 30, fontSize: 15);
        pick.CustomMinimumSize = new Vector2(96, 30);
        if (onlyServer is { } only && only.Name == s.Name) pick.TooltipText = "Enter";
        pick.Pressed += () => Pick(chosen);
        Audio.HookButton(pick);
        row.AddChild(pick);
        return card;
    }

    private void OpenSettings() => SettingsPanel.Open(this, Game.Requests.ClientSettings);

    public override void _UnhandledKeyInput(InputEvent e)
    {
        if (e is not InputEventKey { Pressed: true, Echo: false } key || key.Keycode is not (Key.Enter or Key.KpEnter)) return;
        if (session.Visible && !continueButton.Disabled && busy == null)
        {
            GetViewport().SetInputAsHandled();
            OnContinue();
            return;
        }
        if (onlyServer is not { } only || !serverPanel.Visible || busy != null) return;
        GetViewport().SetInputAsHandled();
        Pick(only);
    }

    private void OnLoginPressed()
    {
        if (loginPending) return;
        if (user.Text.StripEdges().Length == 0)
        {
            Notice.Show(this, "Please enter a username.");
            return;
        }

        loginPending = true;
        busy = Notice.Busy(this, $"Connecting to {Config.ServerHost}:{Config.ServerPort} …");
        Game.LoginServer.ConnectToLoginServer();
    }

    private void OnVersion(int v)
    {
        if (!loginPending) return;
        if (resuming)
        {
            sessionStatus.Text = $"Server online (v{v}). Signing in…";
            Game.LoginServer.Login(SavedAccount.Name, SavedAccount.Password);
            return;
        }
        busy?.SetMessage($"Server online (v{v}). Signing in…");
        Game.LoginServer.Login(user.Text.StripEdges(), pass.Text);
    }

    private void OnLoginResult(bool ok, int result)
    {
        loginPending = false;
        CloseBusy();

        if (resuming)
        {
            if (!ok)
            {
                sessionStatus.Text = LoginResults.Describe(result)
                                      + " Log out to sign in with different details.";
                continueButton.Text = "Try again";
                continueButton.Disabled = false;
                return;
            }
            sessionStatus.Text = "Loading servers…";
            Game.LoginServer.RequestServerList();
            return;
        }

        if (!ok)
        {
            Notice.Show(this, LoginResults.Describe(result));
            return;
        }
        if (remember.ButtonPressed) SavedAccount.Save(user.Text.StripEdges(), pass.Text);
        else SavedAccount.Forget();
        EnterServerMode("Loading servers…");
        Game.LoginServer.RequestServerList();
    }

    private void OnLoginServerAccountInUse(AccountInUse info)
    {
        loginPending = false;
        CloseBusy();
        Notice.Confirm(this,
            info.Describe() + "\n\nDisconnect it and continue?",
            "Disconnect it", "Cancel",
            () => KickThenSignIn(info),
            null,
            "Account already in use");
    }

    private void KickThenSignIn(AccountInUse info)
    {
        loginPending = true;
        busy = Notice.Busy(this, $"Closing the session on {info.Where}…");
        var (account, password) = Game.LoginServer.Credentials();
        AccountKick.Request(this, Config.GameHostFor(info.Host), info.Port, account, password,
            code => OnLoginServerKickResult(code, info));
    }

    private void OnLoginServerKickResult(AccountKickCode? code, AccountInUse info)
    {
        CloseBusy();

        if (code is AccountKickCode.Done or AccountKickCode.NotOnline)
        {
            busy = Notice.Busy(this, "Signing in…");
            Game.LoginServer.RetryLogin();
            return;
        }

        loginPending = false;

        if (code == AccountKickCode.Rejected)
        {
            Notice.Show(this, $"{info.Where} refused the request — the account or password did not match.");
            return;
        }

        Notice.Confirm(this,
            $"{info.Where} did not answer, so the other session could not be closed.\n\nSign in anyway?",
            "Sign in anyway", "Cancel",
            () =>
            {
                loginPending = true;
                busy = Notice.Busy(this, "Signing in…");
                Game.LoginServer.RetryLogin(LoginRequestFlags.IgnoreOnlineClaim);
            },
            null,
            "Server unreachable");
    }

    private void OnGameAccountInUse(AccountInUse info)
    {
        CloseBusy();
        Notice.Confirm(this,
            info.Describe() + "\n\nDisconnect it and continue?",
            "Disconnect it", "Cancel",
            () =>
            {
                busy = Notice.Busy(this, "Closing the other session…");
                Game.Requests.PreGame.KickOut();
            },
            () => Game.Connection.Disconnect(expected: true),
            "Account already in use");
    }

    private void OnKickResult(AccountKickCode code)
    {
        CloseBusy();

        if (code is AccountKickCode.Done or AccountKickCode.NotOnline)
        {
            busy = Notice.Busy(this, "Signing in…");
            Game.Requests.PreGame.RetryLogin();
            return;
        }

        Game.Connection.Disconnect(expected: true);
        Notice.Show(this, "The server refused the takeover request.");
    }

    private void OnServerList(List<ServerListEntry> servers)
    {
        foreach (Node c in serverBox.GetChildren()) c.QueueFree();
        serversReady = servers.Count > 0;
        onlyServer = servers.Count == 1 && servers[0].Players >= 0 ? servers[0] : null;
        if (servers.Count == 0)
        {
            serverStatus.Text = "No servers online.";
            if (session.Visible) sessionStatus.Text = "No servers are online right now.";
            return;
        }
        foreach (var s in servers) serverBox.AddChild(BuildServerCard(s));
        OnServerListShown();

        if (!session.Visible) return;
        sessionStatus.Text = "Continue to server selection.";
        continueButton.Text = "Continue";
        continueButton.Disabled = false;
    }

    private void Pick(ServerListEntry server)
    {
        busy = Notice.Busy(this, $"Connecting to {server.Name}…");
        var (account, password) = Game.LoginServer.Credentials();
        Game.LoginServer.Disconnect(expected: true);
        Game.Connection.BeginGameLogin(Config.GameHostFor(server.Address), server.Port, account, password);
    }

    private void OnGameLogin(bool ok, int nation)
    {
        CloseBusy();
        if (!ok)
        {
            Notice.Show(this, "Game-server login failed.");
            return;
        }
        GetTree().ChangeSceneToFile(nation == Nations.NotSelected
            ? "res://scenes/NationSelect.tscn"
            : "res://scenes/CharSelect.tscn");
    }

    internal void PreviewAccountInUse(AccountInUse info) => OnGameAccountInUse(info);

    internal void PreviewSignedIn(List<ServerListEntry> servers)
    {
        Game.LoginServer.Disconnect(expected: true);
        OnServerList(servers);
    }

    internal void PreviewSignInFailed(int code)
    {
        Game.LoginServer.Disconnect(expected: true);
        OnLoginResult(false, code);
    }

    internal void PreviewServers(List<ServerListEntry> servers)
    {
        EnterServerMode("");
        OnServerList(servers);
    }

    private void EnterServerMode(string status)
    {
        cardBox.Visible = false;
        session.Visible = false;
        resuming = false;
        footer.OffsetRight = -(ServerPanelWidth + 24);
        serverStatus.Text = status;
        serverPanel.Visible = true;
    }

    private void OnLogout() => SignOut(clearUser: false);

    private void SignOut(bool clearUser)
    {
        SavedAccount.Forget();
        Game.LoginServer.Disconnect(expected: true);
        resuming = false;
        loginPending = false;
        serversReady = false;
        foreach (Node c in serverBox.GetChildren()) c.QueueFree();
        serverPanel.Visible = false;
        footer.OffsetRight = -12;
        session.Visible = false;
        cardBox.Visible = true;
        remember.ButtonPressed = false;
        if (clearUser) user.Text = "";
        pass.Text = "";
        if (Platform.PointerUi) pass.GrabFocus();
    }

    private void CloseBusy()
    {
        busy?.Close();
        busy = null;
    }

    private void OnExit() => _ = Diag.Guard("login-exit", () => Shutdown.Begin(this, 240));

    private const string Disclaimer =
        "This is an unofficial, non-commercial fan project made for study and preservation. It is not "
        + "affiliated with, endorsed by, or sponsored by Mgame Corporation, Noah System, or any Knight "
        + "Online publisher.\nKnight Online and all related names, logos and game content are "
        + "trademarks of their respective owners. No infringement is intended and nothing here is sold.";

    private void BuildFooter()
    {
        var layer = new CanvasLayer { Layer = UiLayers.LoginScreen };
        AddChild(layer);

        var footer = this.footer = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        footer.SetAnchorsPreset(LayoutPreset.BottomWide);
        footer.GrowVertical = GrowDirection.Begin;
        footer.OffsetLeft = 16;
        footer.OffsetRight = -12;
        footer.OffsetTop = -FooterHeight;
        footer.OffsetBottom = -8;
        footer.AddThemeConstantOverride("separation", 18);
        layer.AddChild(footer);

        var legal = new Label
        {
            Text = Disclaimer,
            MouseFilter = MouseFilterEnum.Ignore,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ShrinkEnd,
            Modulate = new Color(1, 1, 1, Platform.Pick(0.42f, 0.30f)),
        };
        legal.AddThemeFontSizeOverride("font_size", 11);
        legal.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0, 0.85f));
        legal.AddThemeConstantOverride("outline_size", 4);
        footer.AddChild(legal);

        string text = $"v{Build.Version}";
        if (Config.Development) text += "  [dev]";
        if (Packs.Missing.Count > 0)
            text += $"\nINCOMPLETE INSTALL — missing {string.Join(", ", Packs.Missing)}";

        var label = new Label
        {
            Text = text,
            MouseFilter = MouseFilterEnum.Ignore,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom,
            CustomMinimumSize = new Vector2(Packs.Missing.Count > 0 ? 420 : 130, 0),
            SizeFlagsVertical = SizeFlags.ShrinkEnd,
            Modulate = new Color(1, 1, 1, Packs.Missing.Count > 0 ? 1f : 0.5f),
        };
        label.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0, 0.85f));
        label.AddThemeConstantOverride("outline_size", 4);
        if (Packs.Missing.Count > 0)
            label.AddThemeColorOverride("font_color", new Color(1f, 0.42f, 0.42f));
        footer.AddChild(label);
    }

    private void OnError(string e)
    {
        loginPending = false;
        CloseBusy();
        if (resuming && session.Visible)
        {
            sessionStatus.Text = e;
            continueButton.Text = "Try again";
            continueButton.Disabled = false;
            return;
        }
        Notice.Show(this, "Error: " + e);
    }

    private static Control Spacer(int h) => new() { CustomMinimumSize = new Vector2(0, h) };

    private static Label FieldLabel(string text)
    {
        var l = Ui.Legend(text, 13, UiTheme.TextLo);
        l.HorizontalAlignment = HorizontalAlignment.Left;
        return l;
    }

    public override void _ExitTree()
    {
        Game.LoginServer.VersionEvent -= OnVersion;
        Game.LoginServer.LoginResultEvent -= OnLoginResult;
        Game.LoginServer.AccountInUseEvent -= OnLoginServerAccountInUse;
        Game.LoginServer.ServerListEvent -= OnServerList;
        Game.LoginServer.ErrorEvent -= OnError;
        Game.Session.PreGame.LoginResultEvent -= OnGameLogin;
        Game.Session.AccountLock.AccountInUseEvent -= OnGameAccountInUse;
        Game.Session.AccountLock.KickResultEvent -= OnKickResult;
        Game.Connection.ErrorEvent -= OnError;
    }
}
