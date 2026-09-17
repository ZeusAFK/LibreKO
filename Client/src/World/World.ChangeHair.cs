using Godot;
using LibreKO.Domain;
using LibreKO.Network;

namespace LibreKO;

public partial class World
{
    private CanvasLayer _changeHairLayer = null!;
    private HudWindow _changeHairPanel = null!;
    private Label _changeHairFaceLbl = null!;
    private Label _changeHairHairLbl = null!;
    private Label _changeHairStatus = null!;
    private bool _changeHairShown;

    private int _changeHairFace = 1;
    private int _changeHairHair = 1;

    private const int ChangeHairFaceMin = 1;
    private const int ChangeHairFaceMax = 12;
    private const int ChangeHairHairMin = 1;
    private const int ChangeHairHairMax = 12;

    private void ChangeHairInit()
    {
        BuildChangeHairPanel();
        Net.I.ChangeHairResultEvent += OnChangeHairResult;
    }

    private void ChangeHairDispose()
    {
        Net.I.ChangeHairResultEvent -= OnChangeHairResult;
    }

    private void BuildChangeHairPanel()
    {
        _changeHairLayer = new CanvasLayer { Layer = 74 };
        AddChild(_changeHairLayer);

        _changeHairPanel = new HudWindow("changehair", "Beauty Shop", new Vector2(220, 130)) { Visible = false };
        _changeHairPanel.Closed += CloseChangeHair;
        _changeHairLayer.AddChild(_changeHairPanel);

        var r = _changeHairPanel.Body;
        r.AddThemeConstantOverride("separation", 8);

        r.AddChild(UiTheme.SectionTitle("Restyle Appearance"));

        var hint = HudStyle.Label(12);
        hint.Text = "Pick a new hair and face, then Apply.";
        r.AddChild(hint);

        _changeHairHairLbl = AddChangeHairRow(r, "Hair",
            () => StepChangeHairHair(-1), () => StepChangeHairHair(1));
        _changeHairFaceLbl = AddChangeHairRow(r, "Face",
            () => StepChangeHairFace(-1), () => StepChangeHairFace(1));

        var apply = new Button { Text = "Apply", FocusMode = Control.FocusModeEnum.None };
        apply.Pressed += SubmitChangeHair;
        r.AddChild(apply);

        _changeHairStatus = HudStyle.Label(13);
        r.AddChild(_changeHairStatus);
    }

    private static Label AddChangeHairRow(VBoxContainer parent, string caption,
        System.Action onPrev, System.Action onNext)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 6);

        var name = HudStyle.Label(13);
        name.Text = caption;
        name.CustomMinimumSize = new Vector2(48, 0);
        row.AddChild(name);

        var prev = new Button { Text = "<", FocusMode = Control.FocusModeEnum.None };
        prev.Pressed += () => onPrev();
        row.AddChild(prev);

        var value = HudStyle.Label(14, HorizontalAlignment.Center);
        value.CustomMinimumSize = new Vector2(60, 0);
        value.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        row.AddChild(value);

        var next = new Button { Text = ">", FocusMode = Control.FocusModeEnum.None };
        next.Pressed += () => onNext();
        row.AddChild(next);

        parent.AddChild(row);
        return value;
    }

    private void ToggleChangeHair()
    {
        if (_changeHairShown) { CloseChangeHair(); return; }

        int style = HairCode.StyleOf(_selfHair);
        _changeHairFace = Mathf.Clamp(_selfFace == 0 ? ChangeHairFaceMin : _selfFace, ChangeHairFaceMin, ChangeHairFaceMax);
        _changeHairHair = Mathf.Clamp(style == 0 ? ChangeHairHairMin : style, ChangeHairHairMin, ChangeHairHairMax);
        RefreshChangeHairLabels();

        _changeHairPanel.Visible = true;
        _changeHairShown = true;
        SetChangeHairStatus("", false);
    }

    private void CloseChangeHair()
    {
        if (!_changeHairShown) return;
        _changeHairShown = false;
        _changeHairPanel.Visible = false;
    }

    private void StepChangeHairFace(int dir)
    {
        _changeHairFace = WrapRange(_changeHairFace + dir, ChangeHairFaceMin, ChangeHairFaceMax);
        RefreshChangeHairLabels();
    }

    private void StepChangeHairHair(int dir)
    {
        _changeHairHair = WrapRange(_changeHairHair + dir, ChangeHairHairMin, ChangeHairHairMax);
        RefreshChangeHairLabels();
    }

    private static int WrapRange(int v, int min, int max)
    {
        int span = max - min + 1;
        return min + ((v - min) % span + span) % span;
    }

    private void RefreshChangeHairLabels()
    {
        _changeHairFaceLbl.Text = _changeHairFace.ToString();
        _changeHairHairLbl.Text = _changeHairHair.ToString();
    }

    private void SubmitChangeHair()
    {
        SetChangeHairStatus("Applying…", false);
        Net.I.SendChangeHair(HairCode.Pack(_changeHairHair, HairCode.ColourOf(_selfHair)), _changeHairFace);
    }

    private void OnChangeHairResult(bool ok, int face, int hair)
    {
        if (ok)
        {
            _selfHair = hair;
            _selfFace = face;
            RerenderSelfEquipment();
            Chat.Info($"Your new look is ready (hair {HairCode.StyleOf(hair)}, face {face}).");
            SetChangeHairStatus("Looking good!", false);
            CloseChangeHair();
        }
        else
        {
            SetChangeHairStatus("The stylist couldn't apply that.", true);
        }
    }

    private void SetChangeHairStatus(string text, bool warn)
    {
        _changeHairStatus.Text = text;
        _changeHairStatus.AddThemeColorOverride("font_color", warn ? new Color("ff6a6a") : Colors.White);
    }
}
