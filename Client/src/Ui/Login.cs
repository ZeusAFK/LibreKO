using System.Collections.Generic;
using Godot;

namespace LibreKO;

public partial class Login : Control
{
    public static bool StartAtServers;
    public static string PendingNotice = "";

    private static int CardWidth => Platform.Pick(380, 440);
    private static int ServerPanelWidth => Platform.Pick(380, 430);
    private const int FooterHeight = 66;

    private LineEdit _user = null!;
    private LineEdit _pass = null!;
    private CheckButton _remember = null!;
    private VBoxContainer _card = null!;
    private VBoxContainer _session = null!;
    private Label _sessionStatus = null!;
    private Button _continue = null!;
    private HBoxContainer _footer = null!;
    private PanelContainer _serverPanel = null!;
    private VBoxContainer _serverBox = null!;
    private Label _serverStatus = null!;
    private Notice? _busy;
    private bool _loginPending;
    private bool _resuming;
    private bool _serversReady;

    public override void _Ready()
    {
        Ui.MenuScale(true);
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

        _serverPanel = BuildServerPanel();
        split.AddChild(_serverPanel);

        BuildFooter();

        LoginNet.I.VersionEvent += OnVersion;
        LoginNet.I.LoginResultEvent += OnLoginResult;
        LoginNet.I.AccountInUseEvent += OnLoginServerAccountInUse;
        LoginNet.I.ServerListEvent += OnServerList;
        LoginNet.I.ErrorEvent += OnError;
        Net.I.LoginResultEvent += OnGameLogin;
        Net.I.AccountInUseEvent += OnGameAccountInUse;
        Net.I.KickResultEvent += OnKickResult;
        Net.I.ErrorEvent += OnError;

        if (StartAtServers)
        {
            StartAtServers = false;
            EnterServerMode("Loading servers…");
            LoginNet.I.RequestServerList();
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
        _card = new VBoxContainer { CustomMinimumSize = new Vector2(CardWidth, 0) };
        _card.AddThemeConstantOverride("separation", 8);
        parent.AddChild(_card);

        _card.AddChild(Ui.Legend("Welcome to LibreKO", 30, UiTheme.GoldBright));
        if (Platform.PointerUi)
        {
            _card.AddChild(Ui.Legend("Enter your credentials", 15, UiTheme.TextHi));
            _card.AddChild(Spacer(10));
        }

        _card.AddChild(FieldLabel("Username"));
        _card.AddChild(_user = new LineEdit { PlaceholderText = "username" });
        _card.AddChild(FieldLabel("Password"));
        _card.AddChild(_pass = new LineEdit { PlaceholderText = "password", Secret = true });
        Ui.StyleField(_user);
        Ui.StyleField(_pass);
        _card.AddChild(BuildRememberRow());
        if (Platform.PointerUi) _card.AddChild(Spacer(6));

        AddMenuButton(_card, "Login", OnLoginPressed);
        Ui.ActionGroup(_card, ("Settings", OpenSettings), ("Exit", OnExit));

        Ui.SoftScrim(_card);
        _user.Text = SavedAccount.Name;

        _user.TextSubmitted += _ => _pass.GrabFocus();
        _pass.TextSubmitted += _ => OnLoginPressed();
        if (Platform.PointerUi) _user.CallDeferred(Control.MethodName.GrabFocus);
    }

    private Control BuildRememberRow()
    {
        _remember = new CheckButton
        {
            Text = "Keep me signed in",
            ButtonPressed = SavedAccount.Any,
            FocusMode = FocusModeEnum.None,
            CustomMinimumSize = new Vector2(0, Platform.Pick(30, Ui.TouchButtonHeight)),
            SizeFlagsHorizontal = SizeFlags.ShrinkCenter,
        };
        _remember.AddThemeFontSizeOverride("font_size", 15);
        _remember.AddThemeColorOverride("font_color", UiTheme.TextLo);
        _remember.AddThemeColorOverride("font_pressed_color", UiTheme.GoldBright);
        _remember.AddThemeColorOverride("font_hover_color", UiTheme.TextHi);
        _remember.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0, 0.9f));
        _remember.AddThemeConstantOverride("outline_size", 4);
        _remember.AddThemeConstantOverride("h_separation", 14);
        return _remember;
    }

    private void BuildSession(Node parent)
    {
        _session = new VBoxContainer { CustomMinimumSize = new Vector2(CardWidth, 0), Visible = false };
        _session.AddThemeConstantOverride("separation", 8);
        parent.AddChild(_session);

        _session.AddChild(Ui.Legend("Welcome back", 30, UiTheme.GoldBright));
        _session.AddChild(Ui.Legend(SavedAccount.Name, 20, UiTheme.Gold));
        _sessionStatus = Ui.Legend("", 14, UiTheme.TextLo);
        _sessionStatus.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _sessionStatus.CustomMinimumSize = new Vector2(0, 40);
        _session.AddChild(_sessionStatus);

        _continue = AddMenuButton(_session, "Continue", OnContinue);
        _continue.Disabled = true;
        Ui.ActionGroup(_session, ("Log out", OnForgetAccount), ("Settings", OpenSettings),
                       ("Close game", OnExit));

        Ui.SoftScrim(_session);
    }

    private void ResumeSavedSession()
    {
        _resuming = true;
        _loginPending = true;
        _serversReady = false;
        _card.Visible = false;
        _session.Visible = true;
        _continue.Disabled = true;
        _sessionStatus.Text = "Signing in…";
        LoginNet.I.ConnectToLoginServer();
    }

    private void OnContinue()
    {
        if (!_serversReady)
        {
            ResumeSavedSession();
            return;
        }
        EnterServerMode("");
        OnServerListShown();
    }

    private void OnServerListShown()
    {
        int count = _serverBox.GetChildCount();
        _serverStatus.Text = count == 1 ? "1 server available." : $"{count} servers available.";
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

        _serverStatus = Ui.Legend("", 13, UiTheme.TextLo);
        _serverStatus.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        vb.AddChild(_serverStatus);

        var scroll = new ScrollContainer
        {
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
            SizeFlagsVertical = SizeFlags.ExpandFill,
        };
        vb.AddChild(scroll);

        _serverBox = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _serverBox.AddThemeConstantOverride("separation", 8);
        scroll.AddChild(_serverBox);

        Ui.ActionGroup(vb, ("Settings", OpenSettings), ("Logout", OnLogout), ("Exit game", OnExit));
        return panel;
    }

    private Control BuildServerCard(LoginNet.ServerEntry s)
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
        pick.Pressed += () => Pick(chosen);
        Audio.HookButton(pick);
        row.AddChild(pick);
        return card;
    }

    private void OpenSettings() => SettingsPanel.Open(this);

    private void OnLoginPressed()
    {
        if (_loginPending) return;
        if (_user.Text.StripEdges().Length == 0)
        {
            Notice.Show(this, "Please enter a username.");
            return;
        }

        _loginPending = true;
        _busy = Notice.Busy(this, $"Connecting to {Config.ServerHost}:{Config.ServerPort} …");
        LoginNet.I.ConnectToLoginServer();
    }

    private void OnVersion(int v)
    {
        if (!_loginPending) return;
        if (_resuming)
        {
            _sessionStatus.Text = $"Server online (v{v}). Signing in…";
            LoginNet.I.Login(SavedAccount.Name, SavedAccount.Password);
            return;
        }
        _busy?.SetMessage($"Server online (v{v}). Signing in…");
        LoginNet.I.Login(_user.Text.StripEdges(), _pass.Text);
    }

    private void OnLoginResult(bool ok, int result)
    {
        _loginPending = false;
        CloseBusy();

        if (_resuming)
        {
            if (!ok)
            {
                _sessionStatus.Text = LoginResults.Describe(result)
                                      + " Log out to sign in with different details.";
                _continue.Text = "Try again";
                _continue.Disabled = false;
                return;
            }
            _sessionStatus.Text = "Loading servers…";
            LoginNet.I.RequestServerList();
            return;
        }

        if (!ok)
        {
            Notice.Show(this, LoginResults.Describe(result));
            return;
        }
        if (_remember.ButtonPressed) SavedAccount.Save(_user.Text.StripEdges(), _pass.Text);
        else SavedAccount.Forget();
        EnterServerMode("Loading servers…");
        LoginNet.I.RequestServerList();
    }

    private void OnLoginServerAccountInUse(AccountInUse info)
    {
        _loginPending = false;
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
        _loginPending = true;
        _busy = Notice.Busy(this, $"Closing the session on {info.Where}…");
        var (account, password) = LoginNet.I.Credentials();
        AccountKick.Request(this, Config.GameHostFor(info.Host), info.Port, account, password,
            code => OnLoginServerKickResult(code, info));
    }

    private void OnLoginServerKickResult(AccountKickCode? code, AccountInUse info)
    {
        CloseBusy();

        if (code is AccountKickCode.Done or AccountKickCode.NotOnline)
        {
            _busy = Notice.Busy(this, "Signing in…");
            LoginNet.I.RetryLogin();
            return;
        }

        _loginPending = false;

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
                _loginPending = true;
                _busy = Notice.Busy(this, "Signing in…");
                LoginNet.I.RetryLogin(LoginRequestFlags.IgnoreOnlineClaim);
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
                _busy = Notice.Busy(this, "Closing the other session…");
                Net.I.SendKickOut();
            },
            () => Net.I.Disconnect(expected: true),
            "Account already in use");
    }

    private void OnKickResult(AccountKickCode code)
    {
        CloseBusy();

        if (code is AccountKickCode.Done or AccountKickCode.NotOnline)
        {
            _busy = Notice.Busy(this, "Signing in…");
            Net.I.RetryLogin();
            return;
        }

        Net.I.Disconnect(expected: true);
        Notice.Show(this, "The server refused the takeover request.");
    }

    private void OnServerList(List<LoginNet.ServerEntry> servers)
    {
        foreach (Node c in _serverBox.GetChildren()) c.QueueFree();
        _serversReady = servers.Count > 0;
        if (servers.Count == 0)
        {
            _serverStatus.Text = "No servers online.";
            if (_session.Visible) _sessionStatus.Text = "No servers are online right now.";
            return;
        }
        foreach (var s in servers) _serverBox.AddChild(BuildServerCard(s));
        OnServerListShown();

        if (!_session.Visible) return;
        _sessionStatus.Text = "Continue to server selection.";
        _continue.Text = "Continue";
        _continue.Disabled = false;
    }

    private void Pick(LoginNet.ServerEntry server)
    {
        _busy = Notice.Busy(this, $"Connecting to {server.Name}…");
        var (account, password) = LoginNet.I.Credentials();
        LoginNet.I.Disconnect(expected: true);
        Net.I.BeginGameLogin(Config.GameHostFor(server.Address), server.Port, account, password);
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

    internal void PreviewSignedIn(List<LoginNet.ServerEntry> servers)
    {
        LoginNet.I.Disconnect(expected: true);
        OnServerList(servers);
    }

    internal void PreviewSignInFailed(int code)
    {
        LoginNet.I.Disconnect(expected: true);
        OnLoginResult(false, code);
    }

    internal void PreviewServers(List<LoginNet.ServerEntry> servers)
    {
        EnterServerMode("");
        OnServerList(servers);
    }

    private void EnterServerMode(string status)
    {
        _card.Visible = false;
        _session.Visible = false;
        _resuming = false;
        _footer.OffsetRight = -(ServerPanelWidth + 24);
        _serverStatus.Text = status;
        _serverPanel.Visible = true;
    }

    private void OnLogout() => SignOut(clearUser: false);

    private void SignOut(bool clearUser)
    {
        SavedAccount.Forget();
        LoginNet.I.Disconnect(expected: true);
        _resuming = false;
        _loginPending = false;
        _serversReady = false;
        foreach (Node c in _serverBox.GetChildren()) c.QueueFree();
        _serverPanel.Visible = false;
        _footer.OffsetRight = -12;
        _session.Visible = false;
        _card.Visible = true;
        _remember.ButtonPressed = false;
        if (clearUser) _user.Text = "";
        _pass.Text = "";
        if (Platform.PointerUi) _pass.GrabFocus();
    }

    private void CloseBusy()
    {
        _busy?.Close();
        _busy = null;
    }

    private void OnExit() => _ = Diag.Guard("login-exit", () => Shutdown.Begin(this, 240));

    private const string Disclaimer =
        "This is an unofficial, non-commercial fan project made for study and preservation. It is not "
        + "affiliated with, endorsed by, or sponsored by Mgame Corporation, Noah System, or any Knight "
        + "Online publisher.\nKnight Online and all related names, logos and game content are "
        + "trademarks of their respective owners. No infringement is intended and nothing here is sold.";

    private void BuildFooter()
    {
        var layer = new CanvasLayer { Layer = 1 };
        AddChild(layer);

        var footer = _footer = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
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
        _loginPending = false;
        CloseBusy();
        if (_resuming && _session.Visible)
        {
            _sessionStatus.Text = e;
            _continue.Text = "Try again";
            _continue.Disabled = false;
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
        LoginNet.I.VersionEvent -= OnVersion;
        LoginNet.I.LoginResultEvent -= OnLoginResult;
        LoginNet.I.AccountInUseEvent -= OnLoginServerAccountInUse;
        LoginNet.I.ServerListEvent -= OnServerList;
        LoginNet.I.ErrorEvent -= OnError;
        Net.I.LoginResultEvent -= OnGameLogin;
        Net.I.AccountInUseEvent -= OnGameAccountInUse;
        Net.I.KickResultEvent -= OnKickResult;
        Net.I.ErrorEvent -= OnError;
    }
}
