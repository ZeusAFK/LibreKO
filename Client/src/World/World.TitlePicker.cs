using System.Collections.Generic;
using Godot;
using LibreKO.Domain;
using LibreKO.Network;

namespace LibreKO;

public partial class World
{
    private const int TitlePickerWidth = 380;
    private const int TitlePickerHeight = 320;
    private const int NoTitle = 0;

    private CanvasLayer _titleLayer = null!;
    private HudWindow _titlePanel = null!;
    private VBoxContainer _titleList = null!;
    private Label _titleHint = null!;
    private bool _titleShown;

    private void BuildTitlePicker()
    {
        _titleLayer = new CanvasLayer { Layer = 74 };
        AddChild(_titleLayer);

        _titlePanel = new HudWindow("titles", "Titles", new Vector2(320, 150),
            bodyMinWidth: TitlePickerWidth)
        { Visible = false };
        _titlePanel.Closed += CloseTitlePicker;
        _titleLayer.AddChild(_titlePanel);

        var root = _titlePanel.Body;
        root.AddThemeConstantOverride("separation", 6);

        _titleHint = HudStyle.Label(12);
        root.AddChild(_titleHint);

        var scroll = new ScrollContainer
        {
            CustomMinimumSize = new Vector2(TitlePickerWidth, TitlePickerHeight),
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        root.AddChild(scroll);

        _titleList = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _titleList.AddThemeConstantOverride("separation", 3);
        scroll.AddChild(_titleList);
    }

    private void ToggleTitlePicker()
    {
        if (_titleShown) { CloseTitlePicker(); return; }
        _titlePanel.Visible = true;
        _titleShown = true;
        RebuildTitleList();
    }

    private void CloseTitlePicker()
    {
        if (!_titleShown) return;
        _titleShown = false;
        _titlePanel.Visible = false;
    }

    private void RebuildTitleList()
    {
        foreach (var child in _titleList.GetChildren()) child.QueueFree();

        var unlocked = UnlockedTitles();
        _titleHint.Text = unlocked.Count == 0
            ? "Claim an achievement that carries a title to earn one."
            : $"{unlocked.Count} earned. Every claimed title's bonus already counts — "
              + "this only picks the name shown above your character.";

        _titleList.AddChild(BuildTitleRow(NoTitle, "No title", ""));
        foreach (var (titleId, title) in unlocked)
            _titleList.AddChild(BuildTitleRow(titleId, title.Name, title.Bonus));
    }

    private List<(int Id, AchievementData.Title Title)> UnlockedTitles()
    {
        var found = new List<(int, AchievementData.Title)>();
        var seen = new HashSet<int>();

        foreach (var entry in _achEntries)
        {
            if (!entry.Claimed) continue;
            if (AchievementData.Get(entry.Id) is not { } info || info.TitleId == 0) continue;
            if (!seen.Add(info.TitleId)) continue;
            if (AchievementData.TitleOf(info.TitleId) is not { } title) continue;
            found.Add((info.TitleId, title));
        }

        found.Sort((a, b) => string.CompareOrdinal(a.Item2.Name, b.Item2.Name));
        return found;
    }

    private Control BuildTitleRow(int titleId, string name, string bonus)
    {
        bool worn = Net.I.DisplayTitleId == titleId;

        var row = new PanelContainer();
        row.AddThemeStyleboxOverride("panel", UiTheme.Row(muted: !worn));

        var margin = new MarginContainer();
        UiTheme.Margins(margin, 8, 5, 8, 5);
        row.AddChild(margin);

        var columns = new HBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        columns.AddThemeConstantOverride("separation", 8);
        margin.AddChild(columns);

        var text = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        text.AddThemeConstantOverride("separation", 1);
        columns.AddChild(text);

        var label = UiTheme.Text(name, 13, worn ? UiTheme.GoldBright : UiTheme.TextHi);
        label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        label.CustomMinimumSize = new Vector2(1, 0);
        text.AddChild(label);

        if (bonus.Length > 0)
        {
            var bonusLabel = UiTheme.Text(bonus, 11, UiTheme.TextDim);
            bonusLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            bonusLabel.CustomMinimumSize = new Vector2(1, 0);
            text.AddChild(bonusLabel);
        }

        if (worn)
        {
            columns.AddChild(UiTheme.Text("Worn", 12, UiTheme.Gold));
            return row;
        }

        var wear = new Button { Text = "Wear", FocusMode = Control.FocusModeEnum.None };
        int chosen = titleId;
        wear.Pressed += () => Net.I.SendTitleSelect(chosen);
        columns.AddChild(wear);
        return row;
    }

    private void RefreshTitleButton()
    {
        if (_stTitleBtn == null || !GodotObject.IsInstanceValid(_stTitleBtn)) return;

        int titleId = Net.I.DisplayTitleId;
        var title = titleId != 0 ? AchievementData.TitleOf(titleId) : null;
        _stTitleBtn.Text = title is { } worn ? $"Title: {worn.Name}" : "Title: none";
        _stTitleBtn.AddThemeColorOverride(
            "font_color", title != null ? UiTheme.GoldBright : UiTheme.TextLo);
    }
}
