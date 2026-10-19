using Godot;

namespace LibreKO.Features.Windows;

internal sealed class AdminCommandsTab
{
    private const int CommandsWidth = 720;
    private const int CommandsHeight = 380;
    private const float CommandColumnWidth = 340;

    private LineEdit commandSearch = null!;
    private VBoxContainer commandList = null!;
    private List<GmCommandInfo> commands = new();

    internal bool Built => commandList != null;

    internal Control Build()
    {
        var box = new VBoxContainer { CustomMinimumSize = new Vector2(CommandsWidth, 0) };
        box.AddThemeConstantOverride("separation", 6);
        box.AddChild(UiTheme.SectionTitle("GM commands", UiIcons.Get("system/help")));

        commandSearch = new LineEdit { PlaceholderText = "Search commands", ClearButtonEnabled = true };
        commandSearch.AddThemeFontSizeOverride("font_size", 12);
        commandSearch.TextChanged += _ => RefreshCommands();
        box.AddChild(commandSearch);

        var scroll = new ScrollContainer
        {
            CustomMinimumSize = new Vector2(CommandsWidth, CommandsHeight),
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        box.AddChild(scroll);
        commandList = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        commandList.AddThemeConstantOverride("separation", 8);
        scroll.AddChild(commandList);
        RefreshCommands();
        return box;
    }

    internal void SetCommands(List<GmCommandInfo> commands)
    {
        this.commands = commands;
        if (Built) RefreshCommands();
    }

    private void RefreshCommands()
    {
        foreach (var child in commandList.GetChildren()) { commandList.RemoveChild(child); child.QueueFree(); }
        var groups = GmCommands.Grouped(commands, commandSearch.Text);
        if (groups.Count == 0)
        {
            commandList.AddChild(UiTheme.Text(
                commands.Count == 0 ? "Loading the command list…" : "No command matches your search.",
                12, UiTheme.TextLo));
            return;
        }
        foreach (var (group, grouped) in groups)
        {
            commandList.AddChild(UiTheme.SectionTitle(group));
            var grid = new GridContainer { Columns = Platform.Pick(2, 1) };
            grid.AddThemeConstantOverride("h_separation", 16);
            grid.AddThemeConstantOverride("v_separation", 6);
            commandList.AddChild(grid);
            foreach (var command in grouped) grid.AddChild(BuildCommandRow(command));
        }
    }

    private static Control BuildCommandRow(GmCommandInfo command)
    {
        var row = new VBoxContainer { CustomMinimumSize = new Vector2(CommandColumnWidth, 0) };
        row.AddThemeConstantOverride("separation", 0);
        var syntax = UiTheme.Text(command.Syntax, 12, UiTheme.GoldBright);
        syntax.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        syntax.CustomMinimumSize = new Vector2(CommandColumnWidth, 0);
        row.AddChild(syntax);
        var description = UiTheme.Text(command.Description, 11, UiTheme.TextLo);
        description.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        description.CustomMinimumSize = new Vector2(CommandColumnWidth, 0);
        row.AddChild(description);
        return row;
    }
}
