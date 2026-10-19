using Godot;

namespace LibreKO.Features.Windows;

internal static class ClanBoardUi
{
    internal const int Width = 560;
    internal const int RowHeight = 38;
    internal const float MarkSize = 20f;
    internal const float NameWidth = 150f;
    internal const int MessageEditHeight = 200;

    internal static Button BoardButton(string text, Action pressed)
    {
        var button = new Button { Text = text, FocusMode = Control.FocusModeEnum.None };
        button.AddThemeFontSizeOverride("font_size", 12);
        button.Pressed += pressed;
        return button;
    }
}
