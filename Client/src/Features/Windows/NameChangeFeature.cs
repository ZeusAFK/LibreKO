using Godot;

namespace LibreKO.Features.Windows;

internal sealed class NameChangeFeature : IFeature, IClosableWindow
{
    private readonly IWorldContext context;
    private readonly NameChangeSession nameChange;
    private readonly SelfSession self;
    private readonly IClanRequests clanRequests;
    private readonly INameChangeRequests requests;

    public NameChangeFeature(IWorldContext context, NameChangeSession nameChange, SelfSession self, IClanRequests clanRequests, INameChangeRequests requests)
    {
        this.context = context;
        this.nameChange = nameChange;
        this.self = self;
        this.clanRequests = clanRequests;
        this.requests = requests;
    }

    private const int AuctionOpenText = 43715;
    private const int TextClanRenameHint = 7708;
    private const int TextClanNameTaken = 7709;
    private const int TextClanRenamed = 7710;
    private const int TextClanRenameNotMember = 7712;

    private CanvasLayer layer = null!;
    private HudWindow panel = null!;
    private LineEdit edit = null!;
    private Label status = null!;
    private Button btn = null!;
    private bool shown;

    public bool IsOpen => shown;

    private bool clan;
    private string pending = "";
    private Label title = null!;

    private Label hint = null!;

    public void Build()
    {
        BuildPanel();
        nameChange.NameChangeSuccessEvent += OnSuccess;
        nameChange.NameChangeResultEvent += OnResult;
        nameChange.ClanNameChangeResultEvent += OnClanResult;
        nameChange.ClanRenamedEvent += OnClanRenamed;
    }

    public void Dispose()
    {
        nameChange.NameChangeSuccessEvent -= OnSuccess;
        nameChange.NameChangeResultEvent -= OnResult;
        nameChange.ClanNameChangeResultEvent -= OnClanResult;
        nameChange.ClanRenamedEvent -= OnClanRenamed;
    }

    private void BuildPanel()
    {
        layer = new CanvasLayer { Layer = UiLayers.Windows };
        context.Root.AddChild(layer);

        panel = new HudWindow("namechange", "Change Name") { Visible = false };
        panel.Closed += Close;
        layer.AddChild(panel);

        var r = panel.Body;
        r.AddThemeConstantOverride("separation", 8);

        title = UiTheme.SectionTitle("Rename Character");
        r.AddChild(title);

        hint = HudStyle.Label(12);
        hint.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        hint.CustomMinimumSize = new Vector2(260, 0);
        r.AddChild(hint);

        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 6);
        edit = new LineEdit
        {
            PlaceholderText = "new name",
            MaxLength = 20,
            CustomMinimumSize = new Vector2(200, 0),
        };
        edit.TextSubmitted += _ => Submit();
        row.AddChild(edit);
        btn = new Button { Text = "Rename", FocusMode = Control.FocusModeEnum.None };
        btn.Pressed += Submit;
        row.AddChild(btn);
        r.AddChild(row);

        status = HudStyle.Label(13);
        r.AddChild(status);
    }

    private void Open(bool clan = false)
    {
        this.clan = clan;
        title.Text = clan ? "Rename Clan" : "Rename Character";
        hint.Text = clan
            ? ItemData.Text(TextClanRenameHint, "Click on the clan ID you would like to change.")
            : "Requires a Scroll of Identity. 3-20 characters.";
        panel.Visible = true;
        shown = true;
        edit.Text = clan ? "" : self.LastEnter.Name ?? "";
        edit.CaretColumn = edit.Text.Length;
        SetStatus("", false);
        edit.GrabFocus();
    }

    public void Close()
    {
        if (!shown) return;
        shown = false;
        panel.Visible = false;
    }

    private void Submit()
    {
        string name = edit.Text.Trim();
        if (name.Length is < 3 or > 20)
        {
            SetStatus("Name must be 3-20 characters.", true);
            return;
        }
        pending = name;
        SetStatus("Renaming…", false);
        if (clan)
        {
            requests.ClanNameChangeConfirm(name);
            return;
        }
        if (name == (self.LastEnter.Name ?? ""))
        {
            SetStatus("That's already your name.", true);
            return;
        }
        requests.Confirm(name);
    }

    private void OnSuccess(string newName)
    {
        if (string.IsNullOrEmpty(newName)) newName = pending;
        self.Rename(newName);
        if (context.Self.Plate() is { } plate) plate.Name.Text = newName;
        context.Log.Notice($"Your character is now named \"{newName}\".");
        SetStatus("Name changed!", false);
        Close();
    }

    private void OnResult(NameChangeResult code)
    {
        switch (code)
        {
            case NameChangeResult.ShowDialog:
                if (shown)
                    SetStatus("You need a Scroll of Identity to rename.", true);
                else
                    Open();
                break;
            case NameChangeResult.Invalid:
                SetStatus("That name is taken or invalid.", true);
                break;
            case NameChangeResult.InClan:
                SetStatus("Leave your clan before renaming.", true);
                break;
            case NameChangeResult.AuctionOpen:
                SetStatus(ItemData.Text(AuctionOpenText, "You are already bidding or have items to claim"), true);
                break;
            default:
                SetStatus("Couldn't change the name.", true);
                break;
        }
    }

    private void OnClanResult(ClanNameChangeResult code)
    {
        switch (code)
        {
            case ClanNameChangeResult.ShowDialog:
                Open(clan: true);
                break;
            case ClanNameChangeResult.Invalid:
                if (!shown || !clan) Open(clan: true);
                SetStatus(ItemData.Text(TextClanNameTaken, "The ID you entered is being used by another clan. Please pick another ID."), true);
                break;
            case ClanNameChangeResult.NotInClan:
                context.Log.Notice(ItemData.Text(TextClanRenameNotMember, "You can change your clan ID only if you are a clan member!"));
                Close();
                break;
        }
    }

    private void OnClanRenamed(string newName)
    {
        if (clan) Close();
        context.Log.Notice(ItemData.Text(TextClanRenamed, "Your clan ID has been changed."));
        clanRequests.MembersRequest();
    }

    private void SetStatus(string text, bool warn)
    {
        status.Text = text;
        status.AddThemeColorOverride("font_color", warn ? new Color("ff6a6a") : Colors.White);
    }
}
