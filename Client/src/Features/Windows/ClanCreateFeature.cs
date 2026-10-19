using Godot;

namespace LibreKO.Features.Windows;

internal sealed class ClanCreateFeature : IFeature, IClosableWindow
{
    private readonly IWorldContext context;
    private readonly ClanSession clan;
    private readonly SelfSession self;
    private readonly IClanRequests clanRequests;

    public ClanCreateFeature(IWorldContext context, ClanSession clan, SelfSession self, IClanRequests clanRequests)
    {
        this.context = context;
        this.clan = clan;
        this.self = self;
        this.clanRequests = clanRequests;
    }

    private const int PanelWidth = 320;
    private const int ClanNameMaxLength = 20;
    private const int ClanNameMinLength = 2;
    private const int TextClanCreationFee = 6506;

    private CanvasLayer layer = null!;
    private HudWindow panel = null!;
    private Label messageLabel = null!;
    private LineEdit nameInput = null!;

    private bool shown;

    public bool IsOpen => shown;

    private Notice? notice;

    public void Build()
    {
        layer = new CanvasLayer { Layer = UiLayers.ServiceWindows };
        context.Root.AddChild(layer);

        panel = new HudWindow("creat_clan", "Create a Clan", bodyMinWidth: PanelWidth)
        { Visible = false };
        panel.Closed += Close;
        layer.AddChild(panel);
        var root = panel.Body;
        root.AddThemeConstantOverride("separation", 8);

        messageLabel = UiTheme.Text("", 13, UiTheme.TextHi);
        messageLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        messageLabel.CustomMinimumSize = new Vector2(1, 0);
        root.AddChild(messageLabel);

        nameInput = new LineEdit
        {
            PlaceholderText = "clan name",
            MaxLength = ClanNameMaxLength,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        nameInput.TextSubmitted += _ => Submit();
        root.AddChild(nameInput);

        var buttons = new HBoxContainer();
        buttons.AddThemeConstantOverride("separation", 8);
        root.AddChild(buttons);
        buttons.AddChild(new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });
        var yes = new Button { Text = "Create", FocusMode = Control.FocusModeEnum.None };
        yes.Pressed += Submit;
        buttons.AddChild(yes);
        var no = new Button { Text = "Cancel", FocusMode = Control.FocusModeEnum.None };
        no.Pressed += Close;
        buttons.AddChild(no);
    }

    public void Dispose()
    {
    }

    internal void CreateClanFromInn()
    {
        if (self.Sheet.Level < ClanTypes.CreationLevel)
        {
            context.Log.Notice("Sorry.  A weakling like you are not fit to become a leader!!");
            return;
        }
        if (self.Sheet.Gold < ClanTypes.CreationCoins)
        {
            context.Log.Notice($"Sorry.  You need {ClanTypes.CreationCoins:n0} gold in order to create a clan.");
            return;
        }
        if (clan.MyClan.InClan)
        {
            context.Log.Notice("You can't create a clan because you're already in another clan.");
            return;
        }

        Open($"Name your clan. Founding it costs {ClanTypes.CreationCoins:n0} gold and makes you its chief.");
    }

    internal void Open(string message)
    {
        messageLabel.Text = message;
        nameInput.Clear();
        panel.Visible = true;
        shown = true;
        nameInput.GrabFocus();
    }

    public void Close()
    {
        if (!shown) return;
        shown = false;
        panel.Visible = false;
        DismissNotice();
    }

    private void Submit()
    {
        if (!shown || notice != null) return;
        string name = nameInput.Text.Trim();
        if (name.Length < ClanNameMinLength)
        {
            messageLabel.Text = $"A clan name needs {ClanNameMinLength} to {ClanNameMaxLength} characters.";
            return;
        }
        notice = Notice.Confirm(context.Root,
            ItemData.Text(TextClanCreationFee, "You need %d Coins to create a clan.")
                .Replace("%d", ClanTypes.CreationCoins.ToString()),
            "Yes", "No",
            () =>
            {
                notice = null;
                if (!shown) return;
                clanRequests.Create(name);
                Close();
            },
            () =>
            {
                notice = null;
                if (shown) nameInput.GrabFocus();
            },
            "Create a Clan");
    }

    private void DismissNotice()
    {
        if (notice != null && GodotObject.IsInstanceValid(notice)) notice.Close();
        notice = null;
    }
}
