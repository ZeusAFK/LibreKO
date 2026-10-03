using Godot;
using LibreKO.Network;

namespace LibreKO;

public partial class World
{
    private const int AdminSpawnCoordinateMax = 8191;
    private const int AdminSpawnDirectionMax = 359;
    private const int AdminSpawnCountMax = 50;
    private const int AdminSpawnRespawnMax = 86_400;
    private const int AdminSpawnRangeMax = 1_000;
    private const float AdminSpawnFieldWidth = 120;
    private const float AdminSpawnLabelWidth = 118;

    private HudWindow _admSpawnPanel = null!;
    private Label _admSpawnTitle = null!, _admSpawnInfo = null!;
    private SpinBox _admSpawnX = null!, _admSpawnZ = null!, _admSpawnDirection = null!;
    private SpinBox _admSpawnCount = null!, _admSpawnRespawn = null!, _admSpawnRange = null!;
    private Button _admSpawnPersistBtn = null!;
    private AdminSpawnRow? _admSpawnRow;
    private bool _admSpawnShown;

    private void BuildAdminSpawnPanel()
    {
        _admSpawnPanel = new HudWindow("spawn-row", Localization.Loc.Tr("Spawn row"), new Vector2(560, 160), 400) { Visible = false };
        _admSpawnPanel.Closed += CloseAdminSpawn;
        _admLayer.AddChild(_admSpawnPanel);

        var root = _admSpawnPanel.Body;
        root.AddThemeConstantOverride("separation", 8);

        _admSpawnTitle = UiTheme.Text("", 15, UiTheme.GoldBright);
        root.AddChild(_admSpawnTitle);
        _admSpawnInfo = UiTheme.Text("", 12, UiTheme.TextLo);
        _admSpawnInfo.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        root.AddChild(_admSpawnInfo);
        root.AddChild(UiTheme.Rule());

        var grid = new GridContainer { Columns = 4 };
        grid.AddThemeConstantOverride("h_separation", 10);
        grid.AddThemeConstantOverride("v_separation", 8);
        root.AddChild(grid);

        _admSpawnX = AdminSpawnField(grid, "X", 0, AdminSpawnCoordinateMax, Localization.Loc.Tr("Spawn centre, east-west"));
        _admSpawnZ = AdminSpawnField(grid, "Z", 0, AdminSpawnCoordinateMax, Localization.Loc.Tr("Spawn centre, north-south"));
        _admSpawnDirection = AdminSpawnField(grid, Localization.Loc.Tr("Facing"), 0, AdminSpawnDirectionMax, Localization.Loc.Tr("Degrees; 0 lets the client pick"));
        _admSpawnCount = AdminSpawnField(grid, Localization.Loc.Tr("Quantity"), 1, AdminSpawnCountMax, Localization.Loc.Tr("How many stand here"));
        _admSpawnRespawn = AdminSpawnField(grid, Localization.Loc.Tr("Respawn s"), 0, AdminSpawnRespawnMax, Localization.Loc.Tr("Seconds between a death and the return"));
        _admSpawnRange = AdminSpawnField(grid, Localization.Loc.Tr("Range"), 0, AdminSpawnRangeMax, Localization.Loc.Tr("Radius each one may spawn within; 0 is the exact spot"));

        root.AddChild(UiTheme.Rule());
        var actions = new HBoxContainer();
        actions.AddThemeConstantOverride("separation", 8);
        root.AddChild(actions);

        var set = AdminButton(Localization.Loc.Tr("Set"), 90);
        set.TooltipText = Localization.Loc.Tr("Apply to the running server only");
        set.Pressed += () => SendAdminSpawnEdit(persist: false);
        actions.AddChild(set);

        _admSpawnPersistBtn = AdminButton(Localization.Loc.Tr("Persist"), 90);
        _admSpawnPersistBtn.TooltipText = Localization.Loc.Tr("Apply and write the spawn row to the seed file of this zone");
        _admSpawnPersistBtn.Pressed += () => SendAdminSpawnEdit(persist: true);
        actions.AddChild(_admSpawnPersistBtn);

        var spacer = new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        actions.AddChild(spacer);

        var close = AdminButton(Localization.Loc.Tr("Close"), 90);
        close.Pressed += CloseAdminSpawn;
        actions.AddChild(close);
    }

    private static SpinBox AdminSpawnField(GridContainer grid, string label, int min, int max, string tooltip)
    {
        var caption = AdminFieldLabel(label, AdminSpawnLabelWidth);
        grid.AddChild(caption);
        var spin = AdminSpin(min, max, AdminSpawnFieldWidth);
        spin.TooltipText = tooltip;
        grid.AddChild(spin);
        return spin;
    }

    private void OnAdminSpawnRow(AdminSpawnRow row)
    {
        if (!AdminFullPanel) return;
        _admSpawnRow = row;
        _admSpawnTitle.Text = $"{row.Name}  ({row.NpcId})";
        _admSpawnInfo.Text = $"{(row.Monster ? Localization.Loc.Tr("Monster") : Localization.Loc.Tr("NPC"))} {Localization.Loc.Tr("spawn row")} {row.Index} {Localization.Loc.Tr("in")} {AdminZoneName(row.Zone)} ({row.Zone})"
            + $"  ·  {Localization.Loc.Tr("ground height")} {row.Y:0.0}  ·  {row.Alive} {Localization.Loc.Tr("of")} {row.Count} {Localization.Loc.Tr("alive")}";
        _admSpawnX.Value = row.X;
        _admSpawnZ.Value = row.Z;
        _admSpawnDirection.Value = row.Direction;
        _admSpawnCount.Value = row.Count;
        _admSpawnRespawn.Value = row.RespawnSeconds;
        _admSpawnRange.Value = row.SpawnRange;
        _admSpawnPersistBtn.Visible = row.CanPersist;
        _admSpawnPanel.Visible = true;
        _admSpawnShown = true;
        Callable.From(_admSpawnPanel.ResetSize).CallDeferred();
    }

    private void CloseAdminSpawn()
    {
        if (!_admSpawnShown) return;
        _admSpawnShown = false;
        _admSpawnPanel.Visible = false;
    }

    private void SendAdminSpawnEdit(bool persist)
    {
        if (_admSpawnRow is not { } row) return;
        SetAdminStatus(persist ? $"{Localization.Loc.Tr("Persisting spawn row")} {row.Index}…" : $"{Localization.Loc.Tr("Setting spawn row")} {row.Index}…", false);
        Net.I.SendAdminSpawnEdit(persist, row.Index, (int)_admSpawnX.Value, (int)_admSpawnZ.Value,
            (int)_admSpawnDirection.Value, (int)_admSpawnCount.Value, (int)_admSpawnRespawn.Value, (int)_admSpawnRange.Value);
    }
}
