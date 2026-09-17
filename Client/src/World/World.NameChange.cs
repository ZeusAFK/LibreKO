using Godot;
using LibreKO.Network;

namespace LibreKO;

public partial class World
{
    private CanvasLayer _nameChangeLayer = null!;
    private HudWindow _nameChangePanel = null!;
    private LineEdit _nameChangeEdit = null!;
    private Label _nameChangeStatus = null!;
    private Button _nameChangeBtn = null!;
    private bool _nameChangeShown;
    private string _nameChangePending = "";

    private void NameChangeInit()
    {
        BuildNameChangePanel();
        Net.I.NameChangeSuccessEvent += OnNameChangeSuccess;
        Net.I.NameChangeResultEvent += OnNameChangeResult;
    }

    private void NameChangeDispose()
    {
        Net.I.NameChangeSuccessEvent -= OnNameChangeSuccess;
        Net.I.NameChangeResultEvent -= OnNameChangeResult;
    }

    private void BuildNameChangePanel()
    {
        _nameChangeLayer = new CanvasLayer { Layer = 74 };
        AddChild(_nameChangeLayer);

        _nameChangePanel = new HudWindow("namechange", "Change Name", new Vector2(220, 140)) { Visible = false };
        _nameChangePanel.Closed += CloseNameChange;
        _nameChangeLayer.AddChild(_nameChangePanel);

        var r = _nameChangePanel.Body;
        r.AddThemeConstantOverride("separation", 8);

        r.AddChild(UiTheme.SectionTitle("Rename Character"));

        var hint = HudStyle.Label(12);
        hint.Text = "Requires a Scroll of Identity. 3-20 characters.";
        r.AddChild(hint);

        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 6);
        _nameChangeEdit = new LineEdit
        {
            PlaceholderText = "new name",
            MaxLength = 20,
            CustomMinimumSize = new Vector2(200, 0),
        };
        _nameChangeEdit.TextSubmitted += _ => SubmitNameChange();
        row.AddChild(_nameChangeEdit);
        _nameChangeBtn = new Button { Text = "Rename", FocusMode = Control.FocusModeEnum.None };
        _nameChangeBtn.Pressed += SubmitNameChange;
        row.AddChild(_nameChangeBtn);
        r.AddChild(row);

        _nameChangeStatus = HudStyle.Label(13);
        r.AddChild(_nameChangeStatus);
    }

    private void ToggleNameChange()
    {
        if (_nameChangeShown) { CloseNameChange(); return; }
        OpenNameChange();
        Net.I.SendNameChangeRequest();
    }

    private void OpenNameChange()
    {
        _nameChangePanel.Visible = true;
        _nameChangeShown = true;
        _nameChangeEdit.Text = Net.I.LastEnter.Name ?? "";
        _nameChangeEdit.CaretColumn = _nameChangeEdit.Text.Length;
        SetNameChangeStatus("", false);
        _nameChangeEdit.GrabFocus();
    }

    private void CloseNameChange()
    {
        if (!_nameChangeShown) return;
        _nameChangeShown = false;
        _nameChangePanel.Visible = false;
    }

    private void SubmitNameChange()
    {
        string name = _nameChangeEdit.Text.Trim();
        if (name.Length is < 3 or > 20)
        {
            SetNameChangeStatus("Name must be 3-20 characters.", true);
            return;
        }
        if (name == (Net.I.LastEnter.Name ?? ""))
        {
            SetNameChangeStatus("That's already your name.", true);
            return;
        }
        _nameChangePending = name;
        SetNameChangeStatus("Renaming…", false);
        Net.I.SendNameChangeConfirm(name);
    }

    private void OnNameChangeSuccess(string newName)
    {
        if (string.IsNullOrEmpty(newName)) newName = _nameChangePending;
        Chat.Info($"Your character is now named \"{newName}\".");
        SetNameChangeStatus("Name changed!", false);
        CloseNameChange();
    }

    private void OnNameChangeResult(int code)
    {
        switch (code)
        {
            case Net.NameChangeShowDialog:
                if (_nameChangeShown)
                    SetNameChangeStatus("You need a Scroll of Identity to rename.", true);
                else
                    OpenNameChange();
                break;
            case Net.NameChangeInvalid:
                SetNameChangeStatus("That name is taken or invalid.", true);
                break;
            case Net.NameChangeInClan:
                SetNameChangeStatus("Leave your clan before renaming.", true);
                break;
            default:
                SetNameChangeStatus("Couldn't change the name.", true);
                break;
        }
    }

    private void SetNameChangeStatus(string text, bool warn)
    {
        _nameChangeStatus.Text = text;
        _nameChangeStatus.AddThemeColorOverride("font_color", warn ? new Color("ff6a6a") : Colors.White);
    }
}
