using System;
using System.Collections.Generic;
using Godot;
using LibreKO.Domain;

namespace LibreKO;

public partial class MiniMap
{
    private const string NpcListTitle = "NPC List";
    private const string NpcListGlyph = "N";
    private const float NpcListWidth = 132f;
    private const float NpcListGap = 6f;
    private const float NpcListButtonSize = 24f;
    private const int NpcListTitleFont = 12;
    private const int NpcListRowFont = 11;
    private static readonly Vector2 NpcListButtonSpot = new(4f, 141f);

    private Button npcListButton = null!;
    private PanelContainer npcList = null!;
    private readonly List<Button> npcKindRows = new();

    public event Action<int>? NpcKindPicked;

    private void BuildNpcList()
    {
        npcListButton = UiTheme.IconButton(NpcListGlyph, NpcListTitle);
        npcListButton.CustomMinimumSize = new Vector2(NpcListButtonSize, NpcListButtonSize);
        npcListButton.Position = NpcListButtonSpot + new Vector2(0f, HeaderSize);
        npcListButton.Pressed += () => npcList.Visible = !npcList.Visible;
        AddChild(npcListButton);

        npcList = new PanelContainer
        {
            Visible = false,
            MouseFilter = MouseFilterEnum.Stop,
            CustomMinimumSize = new Vector2(NpcListWidth, 0f),
            Position = new Vector2(-NpcListWidth - NpcListGap, HeaderSize),
        };
        npcList.AddThemeStyleboxOverride("panel", UiTheme.WindowPanel());
        var rows = new VBoxContainer();
        rows.AddThemeConstantOverride("separation", 1);
        var title = UiTheme.Heading(NpcListTitleFont, NpcListTitle);
        title.HorizontalAlignment = HorizontalAlignment.Center;
        rows.AddChild(title);
        for (int i = 0; i < NpcFinder.KindCount; i++)
        {
            int kind = i + 1;
            var row = new Button
            {
                Text = NpcFinder.KindCaptions[i],
                ToggleMode = true,
                FocusMode = FocusModeEnum.None,
                Alignment = HorizontalAlignment.Left,
            };
            row.AddThemeFontSizeOverride("font_size", NpcListRowFont);
            row.AddThemeStyleboxOverride("normal", UiTheme.ListRow(false));
            row.AddThemeStyleboxOverride("hover", UiTheme.ListRow(false));
            row.AddThemeStyleboxOverride("disabled", UiTheme.ListRow(false));
            row.AddThemeStyleboxOverride("pressed", UiTheme.ListRow(true));
            row.AddThemeStyleboxOverride("hover_pressed", UiTheme.ListRow(true));
            row.AddThemeColorOverride("font_color", UiTheme.TextLo);
            row.AddThemeColorOverride("font_hover_color", UiTheme.TextHi);
            row.AddThemeColorOverride("font_pressed_color", UiTheme.GoldBright);
            row.AddThemeColorOverride("font_hover_pressed_color", UiTheme.GoldBright);
            row.AddThemeColorOverride("font_disabled_color", UiTheme.TextDim);
            row.Pressed += () => NpcKindPicked?.Invoke(kind);
            npcKindRows.Add(row);
            rows.AddChild(row);
        }
        npcList.AddChild(rows);
        AddChild(npcList);
    }

    internal void ShowNpcList() => npcList.Visible = true;

    public void SetNpcKinds(IReadOnlyList<bool> present, int selected)
    {
        for (int i = 0; i < npcKindRows.Count; i++)
        {
            var row = npcKindRows[i];
            row.Disabled = i >= present.Count || !present[i];
            row.SetPressedNoSignal(selected == i + 1);
        }
    }
}
