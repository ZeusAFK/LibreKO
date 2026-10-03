using Godot;
using LibreKO.Network;

namespace LibreKO;

public partial class World
{
    private const int ClanCreatePanelWidth = 320;
    private const int ClanNameMaxLength = 20;
    private const int ClanNameMinLength = 2;

    private CanvasLayer _clanCreateLayer = null!;
    private HudWindow _clanCreatePanel = null!;
    private Label _clanCreateMessage = null!;
    private LineEdit _clanCreateName = null!;
    private bool _clanCreateShown;

    private void ClanCreateInit()
    {
        _clanCreateLayer = new CanvasLayer { Layer = 75 };
        AddChild(_clanCreateLayer);

        _clanCreatePanel = new HudWindow("creat_clan", "Create a Clan", bodyMinWidth: ClanCreatePanelWidth)
        { Visible = false };
        _clanCreatePanel.Closed += CloseClanCreate;
        _clanCreateLayer.AddChild(_clanCreatePanel);
        var root = _clanCreatePanel.Body;
        root.AddThemeConstantOverride("separation", 8);

        _clanCreateMessage = UiTheme.Text("", 13, UiTheme.TextHi);
        _clanCreateMessage.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _clanCreateMessage.CustomMinimumSize = new Vector2(1, 0);
        root.AddChild(_clanCreateMessage);

        _clanCreateName = new LineEdit
        {
            PlaceholderText = "clan name",
            MaxLength = ClanNameMaxLength,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        _clanCreateName.TextSubmitted += _ => SubmitClanCreate();
        root.AddChild(_clanCreateName);

        var buttons = new HBoxContainer();
        buttons.AddThemeConstantOverride("separation", 8);
        root.AddChild(buttons);
        buttons.AddChild(new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });
        var yes = new Button { Text = "Create", FocusMode = Control.FocusModeEnum.None };
        yes.Pressed += SubmitClanCreate;
        buttons.AddChild(yes);
        var no = new Button { Text = "Cancel", FocusMode = Control.FocusModeEnum.None };
        no.Pressed += CloseClanCreate;
        buttons.AddChild(no);
    }

    private void ClanCreateDispose()
    {
    }

    private void CreateClanFromInn()
    {
        if (Sheet.Level < ClanTypes.CreationLevel)
        {
            CombatNotice("Sorry.  A weakling like you are not fit to become a leader!!");
            return;
        }
        if (Sheet.Gold < ClanTypes.CreationCoins)
        {
            CombatNotice($"Sorry.  You need {ClanTypes.CreationCoins:n0} gold in order to create a clan.");
            return;
        }
        if (MyClan.InClan)
        {
            CombatNotice("You can't create a clan because you're already in another clan.");
            return;
        }

        OpenClanCreate($"Name your clan. Founding it costs {ClanTypes.CreationCoins:n0} gold and makes you its chief.");
    }

    private void OpenClanCreate(string message)
    {
        _clanCreateMessage.Text = message;
        _clanCreateName.Clear();
        _clanCreatePanel.Visible = true;
        _clanCreateShown = true;
        _clanCreateName.GrabFocus();
    }

    private void CloseClanCreate()
    {
        if (!_clanCreateShown) return;
        _clanCreateShown = false;
        _clanCreatePanel.Visible = false;
    }

    private void SubmitClanCreate()
    {
        string name = _clanCreateName.Text.Trim();
        if (name.Length < ClanNameMinLength)
        {
            _clanCreateMessage.Text = $"A clan name needs {ClanNameMinLength} to {ClanNameMaxLength} characters.";
            return;
        }
        Net.I.SendClanCreate(name);
    }
}
