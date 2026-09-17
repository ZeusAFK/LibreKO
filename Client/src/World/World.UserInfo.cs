using Godot;
using LibreKO.Domain;
using LibreKO.Network;

namespace LibreKO;

public partial class World
{
    private CanvasLayer _userInfoLayer = null!;
    private HudWindow _userInfoPanel = null!;
    private VBoxContainer _userInfoRows = null!;
    private Label _userInfoStatus = null!;
    private bool _userInfoShown;
    private string _userInfoPending = "";

    private void UserInfoInit()
    {
        _userInfoLayer = new CanvasLayer { Layer = 78 };
        AddChild(_userInfoLayer);

        _userInfoPanel = new HudWindow("userinfo", "User Information", new Vector2(320, 150), 260) { Visible = false };
        _userInfoPanel.Closed += CloseUserInfo;
        _userInfoLayer.AddChild(_userInfoPanel);

        var root = _userInfoPanel.Body;
        root.AddThemeConstantOverride("separation", 6);

        _userInfoRows = new VBoxContainer();
        _userInfoRows.AddThemeConstantOverride("separation", 4);
        root.AddChild(_userInfoRows);

        _userInfoStatus = UiTheme.Text("", 12, UiTheme.TextLo);
        root.AddChild(_userInfoStatus);

        Net.I.UserInformationEvent += OnUserInformation;
    }

    private void UserInfoDispose()
    {
        Net.I.UserInformationEvent -= OnUserInformation;
    }

    private void RequestUserInformation(string name)
    {
        _userInfoPending = name;
        _userInfoPanel.Title = $"User Information — {name}";
        ClearUserInfoRows();
        _userInfoStatus.Text = "Requesting…";
        _userInfoPanel.Visible = true;
        _userInfoShown = true;
        Net.I.SendUserInformationRequest(name);
    }

    private void CloseUserInfo()
    {
        _userInfoShown = false;
        _userInfoPanel.Visible = false;
    }

    private void ClearUserInfoRows()
    {
        foreach (var c in _userInfoRows.GetChildren()) c.QueueFree();
    }

    private void OnUserInformation(bool accepted, Net.UserInformation info)
    {
        if (!_userInfoShown) return;

        if (!accepted)
        {
            ClearUserInfoRows();
            _userInfoStatus.Text = $"{_userInfoPending} could not be found.";
            _userInfoStatus.AddThemeColorOverride("font_color", UiTheme.Bad);
            return;
        }

        ClearUserInfoRows();
        _userInfoStatus.Text = "";
        _userInfoPanel.Title = $"User Information — {info.Name}";

        AddUserInfoRow("Name", info.Name, UiTheme.TextHi);
        AddUserInfoRow("Level", info.RebirthLevel > 0
            ? $"{info.Level}  (rebirth {info.RebirthLevel})"
            : info.Level.ToString());
        AddUserInfoRow("Class", CharacterClassCatalog.DisplayName(info.Class));

        _userInfoRows.AddChild(new HSeparator());

        AddUserInfoRow("Clan", info.ClanId != 0 && info.ClanName.Length > 0 ? info.ClanName : "—");
        if (info.ClanId != 0)
        {
            AddUserInfoRow("Clan rank", ClanGradeName(info.ClanGrade));
            if (info.ClanChief.Length > 0)
                AddUserInfoRow("Clan leader", info.ClanChief);
        }

        _userInfoRows.AddChild(new HSeparator());

        AddUserInfoRow("National points", info.Loyalty.ToString("N0"), UiTheme.Gold);
        AddUserInfoRow("Monthly NP", info.MonthlyLoyalty.ToString("N0"));
    }

    private void AddUserInfoRow(string label, string value, Color? valueColor = null)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 10);

        var key = UiTheme.Text(label, 12, UiTheme.TextLo);
        key.CustomMinimumSize = new Vector2(120, 0);
        row.AddChild(key);

        var val = UiTheme.Text(value, 13, valueColor ?? UiTheme.TextHi);
        val.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        row.AddChild(val);

        _userInfoRows.AddChild(row);
    }

    private static string NationName(int nation) => nation switch
    {
        Nations.Karus => "Karus",
        Nations.ElMorad => "El Morad",
        _ => "Neutral",
    };

    private static Color NationColor(int nation) => nation switch
    {
        Nations.Karus => new Color("d08a4a"),
        Nations.ElMorad => new Color("5a9ad0"),
        _ => UiTheme.TextLo,
    };

    private static string ClanGradeName(int grade) => grade switch
    {
        1 => "Chief",
        2 => "Vice chief",
        5 => "Trainee",
        _ => "Member",
    };
}
