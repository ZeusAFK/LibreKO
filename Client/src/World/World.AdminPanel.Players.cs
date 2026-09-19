using Godot;

namespace LibreKO;

public partial class World
{
    private LineEdit _admPlayerInput = null!;

    private Control BuildAdminPlayersTab()
    {
        var box = new VBoxContainer { CustomMinimumSize = new Vector2(640, 0) };
        box.AddThemeConstantOverride("separation", 8);

        // Section: Target Selection
        box.AddChild(UiTheme.SectionTitle("Target Player", UiIcons.Get("system/users-three")));

        var targetDesc = UiTheme.Text("Enter player name or fetch the name of your currently targeted character.", 11, UiTheme.TextLo);
        box.AddChild(targetDesc);

        var playerRow = new HBoxContainer();
        playerRow.AddThemeConstantOverride("separation", 6);
        box.AddChild(playerRow);

        _admPlayerInput = new LineEdit
        {
            PlaceholderText = "Character name...",
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        playerRow.AddChild(_admPlayerInput);

        var useTargetBtn = new Button { Text = "Use Selected Target", FocusMode = Control.FocusModeEnum.None };
        useTargetBtn.AddThemeFontSizeOverride("font_size", 12);
        useTargetBtn.Pressed += () =>
        {
            if (_selectedId >= 0 && _ents.TryGetValue(_selectedId, out var ent) && !string.IsNullOrEmpty(ent.Name))
            {
                _admPlayerInput.Text = ent.Name;
                SetAdminStatus($"Target set to {ent.Name}.", false);
            }
            else
            {
                SetAdminStatus("No entity selected. Target a character first or type their name.", true);
            }
        };
        playerRow.AddChild(useTargetBtn);

        var clearBtn = UiTheme.IconButton(UiIcons.Get("system/close"), "Clear name input");
        clearBtn.Pressed += () => { _admPlayerInput.Text = ""; };
        playerRow.AddChild(clearBtn);

        box.AddChild(new HSeparator());

        // Section: Teleportation
        box.AddChild(UiTheme.SectionTitle("Teleportation", UiIcons.Get("system/home")));

        var warpRow = new HBoxContainer();
        warpRow.AddThemeConstantOverride("separation", 8);
        box.AddChild(warpRow);

        var warpToBtn = new Button { Text = "Warp to Player (Arrest)", FocusMode = Control.FocusModeEnum.None };
        warpToBtn.AddThemeFontSizeOverride("font_size", 12);
        warpToBtn.Pressed += () =>
        {
            var name = _admPlayerInput.Text.Trim();
            if (string.IsNullOrEmpty(name))
            {
                SetAdminStatus("Please specify target player name.", true);
                return;
            }
            Net.I.SendOperatorCommand(1, name);
            SetAdminStatus($"Teleporting to player {name}...", false);
        };
        warpRow.AddChild(warpToBtn);

        var summonBtn = new Button { Text = "Summon Player to GM", FocusMode = Control.FocusModeEnum.None };
        summonBtn.AddThemeFontSizeOverride("font_size", 12);
        summonBtn.Pressed += () =>
        {
            var name = _admPlayerInput.Text.Trim();
            if (string.IsNullOrEmpty(name))
            {
                SetAdminStatus("Please specify target player name.", true);
                return;
            }
            Net.I.SendOperatorCommand(7, name);
            SetAdminStatus($"Summoning player {name}...", false);
        };
        warpRow.AddChild(summonBtn);

        box.AddChild(new HSeparator());

        // Section: Moderation & Discipline
        box.AddChild(UiTheme.SectionTitle("Moderation Actions", UiIcons.Get("system/lock")));

        // Chat Discipline
        var muteRow = new HBoxContainer();
        muteRow.AddThemeConstantOverride("separation", 8);
        box.AddChild(muteRow);

        var muteLbl = UiTheme.Text("Chat Moderation:", 12, UiTheme.TextHi);
        muteLbl.CustomMinimumSize = new Vector2(140, 0);
        muteRow.AddChild(muteLbl);

        var muteBtn = new Button { Text = "Mute Player", FocusMode = Control.FocusModeEnum.None };
        muteBtn.AddThemeFontSizeOverride("font_size", 12);
        muteBtn.Pressed += () =>
        {
            var name = _admPlayerInput.Text.Trim();
            if (string.IsNullOrEmpty(name)) { SetAdminStatus("Specify player name.", true); return; }
            Net.I.SendGmCommand($"mute {name}");
            SetAdminStatus($"Muted {name}.", false);
        };
        muteRow.AddChild(muteBtn);

        var unmuteBtn = new Button { Text = "Unmute Player", FocusMode = Control.FocusModeEnum.None };
        unmuteBtn.AddThemeFontSizeOverride("font_size", 12);
        unmuteBtn.Pressed += () =>
        {
            var name = _admPlayerInput.Text.Trim();
            if (string.IsNullOrEmpty(name)) { SetAdminStatus("Specify player name.", true); return; }
            Net.I.SendGmCommand($"unmute {name}");
            SetAdminStatus($"Unmuted {name}.", false);
        };
        muteRow.AddChild(unmuteBtn);

        // Disciplinary Confinement
        var disciplineRow = new HBoxContainer();
        disciplineRow.AddThemeConstantOverride("separation", 8);
        box.AddChild(disciplineRow);

        var dispLbl = UiTheme.Text("Discipline:", 12, UiTheme.TextHi);
        dispLbl.CustomMinimumSize = new Vector2(140, 0);
        disciplineRow.AddChild(dispLbl);

        var prisonBtn = new Button { Text = "Send to Prison (Hapis)", FocusMode = Control.FocusModeEnum.None };
        prisonBtn.AddThemeFontSizeOverride("font_size", 12);
        prisonBtn.Pressed += () =>
        {
            var name = _admPlayerInput.Text.Trim();
            if (string.IsNullOrEmpty(name)) { SetAdminStatus("Specify player name.", true); return; }
            Net.I.SendGmCommand($"hapis {name}");
            SetAdminStatus($"Sent {name} to prison.", false);
        };
        disciplineRow.AddChild(prisonBtn);

        var kickBtn = new Button { Text = "Kick from Server", FocusMode = Control.FocusModeEnum.None };
        kickBtn.AddThemeFontSizeOverride("font_size", 12);
        kickBtn.Pressed += () =>
        {
            var name = _admPlayerInput.Text.Trim();
            if (string.IsNullOrEmpty(name)) { SetAdminStatus("Specify player name.", true); return; }
            Net.I.SendOperatorCommand(5, name);
            SetAdminStatus($"Kicked {name} from server.", false);
        };
        disciplineRow.AddChild(kickBtn);

        var killBtn = new Button { Text = "Kill Character", FocusMode = Control.FocusModeEnum.None };
        killBtn.AddThemeFontSizeOverride("font_size", 12);
        killBtn.Pressed += () =>
        {
            var name = _admPlayerInput.Text.Trim();
            if (string.IsNullOrEmpty(name)) { SetAdminStatus("Specify player name.", true); return; }
            Net.I.SendGmCommand($"kill {name}");
            SetAdminStatus($"Executed kill on {name}.", false);
        };
        disciplineRow.AddChild(killBtn);

        // Account Bans
        var banRow = new HBoxContainer();
        banRow.AddThemeConstantOverride("separation", 8);
        box.AddChild(banRow);

        var banLbl = UiTheme.Text("Account Access:", 12, UiTheme.TextHi);
        banLbl.CustomMinimumSize = new Vector2(140, 0);
        banRow.AddChild(banLbl);

        var banBtn = new Button { Text = "Ban Account", FocusMode = Control.FocusModeEnum.None };
        banBtn.AddThemeFontSizeOverride("font_size", 12);
        banBtn.AddThemeColorOverride("font_color", UiTheme.Bad);
        banBtn.Pressed += () =>
        {
            var name = _admPlayerInput.Text.Trim();
            if (string.IsNullOrEmpty(name)) { SetAdminStatus("Specify player name.", true); return; }
            Net.I.SendGmCommand($"ban {name}");
            SetAdminStatus($"Banned account for {name}.", false);
        };
        banRow.AddChild(banBtn);

        var unbanBtn = new Button { Text = "Unban Account", FocusMode = Control.FocusModeEnum.None };
        unbanBtn.AddThemeFontSizeOverride("font_size", 12);
        unbanBtn.Pressed += () =>
        {
            var name = _admPlayerInput.Text.Trim();
            if (string.IsNullOrEmpty(name)) { SetAdminStatus("Specify player name.", true); return; }
            Net.I.SendGmCommand($"unban {name}");
            SetAdminStatus($"Unbanned account for {name}.", false);
        };
        banRow.AddChild(unbanBtn);

        return box;
    }
}
