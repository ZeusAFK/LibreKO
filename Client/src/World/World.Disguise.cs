using System;
using System.Collections.Generic;
using Godot;
using LibreKO.Domain;
using LibreKO.Network;

namespace LibreKO;

public partial class World
{
    private const int DisguiseLevelText = 10351;
    private const int DisguiseConfirmText = 10352;
    private const int DisguiseTransformedText = 10353;
    private const int DisguiseRefusalTextBase = 7626;
    private const int DisguiseRefusalCodes = 3;
    private const string DisguiseTablePath = "res://assets/skills/disguise.json";
    private const float DisguiseWidth = 240f;
    private const float DisguiseGroupHeight = 190f;
    private const float DisguiseFormHeight = 210f;

    private DisguiseForm[] _disguiseTable = Array.Empty<DisguiseForm>();
    private IReadOnlyList<DisguiseGroup> _disguiseGroups = Array.Empty<DisguiseGroup>();
    private CanvasLayer _disguiseLayer = null!;
    private HudWindow _disguisePanel = null!;
    private VBoxContainer _disguiseGroupRows = null!, _disguiseFormRows = null!;
    private Label _disguiseNote = null!;
    private bool _disguiseShown;
    private int _disguiseGroup = -1;
    private DisguiseForm? _disguisePick;

    private void DisguiseInit()
    {
        _disguiseTable = LoadDisguiseTable();
        BuildDisguisePanel();
        Net.I.TransformationListEvent += OpenDisguise;
        Net.I.TransformationRefusedEvent += OnTransformationRefused;
    }

    private void DisguiseDispose()
    {
        Net.I.TransformationListEvent -= OpenDisguise;
        Net.I.TransformationRefusedEvent -= OnTransformationRefused;
    }

    private static DisguiseForm[] LoadDisguiseTable() =>
        Godot.FileAccess.FileExists(DisguiseTablePath)
            ? Disguise.Parse(Godot.FileAccess.GetFileAsString(DisguiseTablePath))
            : Array.Empty<DisguiseForm>();

    private void BuildDisguisePanel()
    {
        _disguiseLayer = new CanvasLayer { Layer = 74 };
        AddChild(_disguiseLayer);
        _disguisePanel = new HudWindow("disguise", "Transformation", bodyMinWidth: (int)DisguiseWidth) { Visible = false };
        _disguisePanel.Closed += CloseDisguise;
        _disguiseLayer.AddChild(_disguisePanel);

        var body = _disguisePanel.Body;
        body.AddThemeConstantOverride("separation", 6);
        body.AddChild(UiTheme.SectionTitle("Level"));
        _disguiseGroupRows = DisguiseList(body, DisguiseGroupHeight);
        body.AddChild(UiTheme.SectionTitle("Form"));
        _disguiseFormRows = DisguiseList(body, DisguiseFormHeight);

        _disguiseNote = UiTheme.Text("", 12, UiTheme.Warning);
        _disguiseNote.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _disguiseNote.CustomMinimumSize = new Vector2(DisguiseWidth, 0);
        body.AddChild(_disguiseNote);

        var footer = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        footer.AddThemeConstantOverride("separation", 8);
        body.AddChild(footer);
        var ok = UiTheme.ActionButton("OK", "Transform into the selected form");
        ok.CustomMinimumSize = new Vector2(100, 28);
        ok.Pressed += ConfirmDisguise;
        footer.AddChild(ok);
        var close = UiTheme.SmallButton("Close", "Close");
        close.CustomMinimumSize = new Vector2(100, 28);
        close.Pressed += CloseDisguise;
        footer.AddChild(close);
    }

    private static VBoxContainer DisguiseList(VBoxContainer parent, float height)
    {
        var frame = new PanelContainer();
        frame.AddThemeStyleboxOverride("panel", UiTheme.Inset());
        parent.AddChild(frame);
        var scroll = new ScrollContainer
        {
            CustomMinimumSize = new Vector2(DisguiseWidth, height),
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        frame.AddChild(scroll);
        var rows = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        rows.AddThemeConstantOverride("separation", 2);
        scroll.AddChild(rows);
        return rows;
    }

    private void OpenDisguise(int listSkill)
    {
        if (SkillData.Get(listSkill) is not { } skill) return;
        int premium = Net.I.HasPremium ? Net.I.PremiumType : 0;
        _disguiseGroups = Disguise.Groups(Disguise.FormsFor(_disguiseTable, skill.UseItem, premium));
        string title = ItemData.DisplayName(skill.UseItem);
        _disguisePanel.Title = string.IsNullOrEmpty(title) ? "Transformation" : title;
        _disguiseGroup = -1;
        PickDisguiseGroup(0);
        _disguiseShown = true;
        _disguisePanel.Visible = true;
    }

    private void CloseDisguise()
    {
        if (!_disguiseShown) return;
        _disguiseShown = false;
        _disguisePanel.Visible = false;
    }

    private void PickDisguiseGroup(int index)
    {
        _disguiseGroup = index;
        foreach (var child in _disguiseGroupRows.GetChildren()) child.QueueFree();
        for (int i = 0; i < _disguiseGroups.Count; i++)
        {
            int at = i;
            _disguiseGroupRows.AddChild(DisguiseRow($"Lv. {_disguiseGroups[i].Level}", i == index, () => PickDisguiseGroup(at), null));
        }
        PickDisguiseForm(index >= 0 && index < _disguiseGroups.Count && _disguiseGroups[index].Forms.Count > 0
            ? _disguiseGroups[index].Forms[0]
            : null);
    }

    private void PickDisguiseForm(DisguiseForm? form)
    {
        _disguisePick = form;
        foreach (var child in _disguiseFormRows.GetChildren()) child.QueueFree();
        if (_disguiseGroup >= 0 && _disguiseGroup < _disguiseGroups.Count)
        {
            foreach (var each in _disguiseGroups[_disguiseGroup].Forms)
            {
                var row = each;
                _disguiseFormRows.AddChild(DisguiseRow(row.Name, row == form, () => PickDisguiseForm(row), ConfirmDisguise));
            }
        }
        _disguiseNote.Text = form?.Note ?? "";
        _disguiseNote.Visible = !string.IsNullOrEmpty(_disguiseNote.Text);
    }

    private static Button DisguiseRow(string text, bool selected, Action pick, Action? activate)
    {
        var button = new Button
        {
            Text = text,
            FocusMode = Control.FocusModeEnum.None,
            Alignment = HorizontalAlignment.Left,
            CustomMinimumSize = new Vector2(0, 26),
        };
        button.AddThemeFontSizeOverride("font_size", 13);
        button.AddThemeColorOverride("font_color", selected ? UiTheme.GoldBright : UiTheme.TextLo);
        button.AddThemeStyleboxOverride("normal", UiTheme.ListRow(selected));
        button.AddThemeStyleboxOverride("hover", UiTheme.ListRow(true));
        button.AddThemeStyleboxOverride("pressed", UiTheme.ListRow(true));
        button.Pressed += pick;
        if (activate != null)
        {
            button.GuiInput += e =>
            {
                if (e is InputEventMouseButton { ButtonIndex: MouseButton.Left, DoubleClick: true }) activate();
            };
        }
        return button;
    }

    private void ConfirmDisguise()
    {
        if (_disguisePick is not { } form) return;
        switch (Disguise.Refusal(form, Sheet.Level, _selfTransformSkill != 0))
        {
            case DisguiseRefusal.Level:
                Notice.Show(this, ItemData.Text(DisguiseLevelText, "You cannot transform at your level."), "Transformation");
                return;
            case DisguiseRefusal.Transformed:
                Notice.Show(this, ItemData.Text(DisguiseTransformedText, "Transforming."), "Transformation");
                return;
        }
        Notice.Confirm(this, ItemData.Text(DisguiseConfirmText, "Would you like to transform?"), "Yes", "No", () =>
        {
            Net.I.SendMagic(MagicSub.Effecting, form.Skill, Net.I.MyCharId);
            CloseDisguise();
        }, title: "Transformation");
    }

    private void OnTransformationRefused(int code)
    {
        if (code < 1 || code > DisguiseRefusalCodes) return;
        Notice.Show(this, ItemData.Text(DisguiseRefusalTextBase + code, "You cannot transform here."), "Transformation");
    }
}
