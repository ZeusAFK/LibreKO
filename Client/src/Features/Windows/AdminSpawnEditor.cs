using Godot;
using static LibreKO.Features.Windows.AdminPanelUi;

namespace LibreKO.Features.Windows;

internal sealed class AdminSpawnEditor
{
    private const int SpawnCoordinateMax = 8191;
    private const int SpawnDirectionMax = 359;
    private const int SpawnCountMax = 50;
    private const int SpawnRespawnMax = 86_400;
    private const int SpawnRangeMax = 1_000;
    private const float SpawnFieldWidth = 120;
    private const float SpawnLabelWidth = 118;

    private readonly IAdminRequests requests;
    private readonly Action<string, bool> setStatus;

    public AdminSpawnEditor(IAdminRequests requests, Action<string, bool> setStatus)
    {
        this.requests = requests;
        this.setStatus = setStatus;
    }

    internal HudWindow SpawnWindow { get; private set; } = null!;
    private Label spawnTitle = null!;
    private Label spawnInfo = null!;
    private SpinBox spawnX = null!;
    private SpinBox spawnZ = null!;
    private SpinBox spawnDirection = null!;
    private SpinBox spawnCount = null!;
    private SpinBox spawnRespawn = null!;
    private SpinBox spawnRange = null!;
    private Button spawnPersistBtn = null!;
    private AdminSpawnRow? spawnRow;

    internal bool Shown { get; private set; }

    internal void Build(Node layer)
    {
        SpawnWindow = new HudWindow("spawn-row", "Spawn row", new Vector2(560, 160), 400) { Visible = false };
        SpawnWindow.Closed += Close;
        layer.AddChild(SpawnWindow);

        var root = SpawnWindow.Body;
        root.AddThemeConstantOverride("separation", 8);

        spawnTitle = UiTheme.Text("", 15, UiTheme.GoldBright);
        root.AddChild(spawnTitle);
        spawnInfo = UiTheme.Text("", 12, UiTheme.TextLo);
        spawnInfo.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        root.AddChild(spawnInfo);
        root.AddChild(UiTheme.Rule());

        var grid = new GridContainer { Columns = 4 };
        grid.AddThemeConstantOverride("h_separation", 10);
        grid.AddThemeConstantOverride("v_separation", 8);
        root.AddChild(grid);

        spawnX = SpawnField(grid, "X", 0, SpawnCoordinateMax, "Spawn centre, east-west");
        spawnZ = SpawnField(grid, "Z", 0, SpawnCoordinateMax, "Spawn centre, north-south");
        spawnDirection = SpawnField(grid, "Facing", 0, SpawnDirectionMax, "Degrees; 0 lets the client pick");
        spawnCount = SpawnField(grid, "Quantity", 1, SpawnCountMax, "How many stand here");
        spawnRespawn = SpawnField(grid, "Respawn s", 0, SpawnRespawnMax, "Seconds between a death and the return");
        spawnRange = SpawnField(grid, "Range", 0, SpawnRangeMax, "Radius each one may spawn within; 0 is the exact spot");

        root.AddChild(UiTheme.Rule());
        var actions = new HBoxContainer();
        actions.AddThemeConstantOverride("separation", 8);
        root.AddChild(actions);

        var set = Button("Set", 90);
        set.TooltipText = "Apply to the running server only";
        set.Pressed += () => SendSpawnEdit(persist: false);
        actions.AddChild(set);

        spawnPersistBtn = Button("Persist", 90);
        spawnPersistBtn.TooltipText = "Apply and write the spawn row to the seed file of this zone";
        spawnPersistBtn.Pressed += () => SendSpawnEdit(persist: true);
        actions.AddChild(spawnPersistBtn);

        var spacer = new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        actions.AddChild(spacer);

        var close = Button("Close", 90);
        close.Pressed += Close;
        actions.AddChild(close);
    }

    private static SpinBox SpawnField(GridContainer grid, string label, int min, int max, string tooltip)
    {
        var caption = FieldLabel(label, SpawnLabelWidth);
        grid.AddChild(caption);
        var spin = Spin(min, max, SpawnFieldWidth);
        spin.TooltipText = tooltip;
        grid.AddChild(spin);
        return spin;
    }

    internal void Show(AdminSpawnRow row)
    {
        spawnRow = row;
        spawnTitle.Text = $"{row.Name}  ({row.NpcId})";
        spawnInfo.Text = $"{(row.Monster ? "Monster" : "NPC")} spawn row {row.Index} in {ZoneName(row.Zone)} ({row.Zone})"
            + $"  ·  ground height {row.Y:0.0}  ·  {row.Alive} of {row.Count} alive";
        spawnX.Value = row.X;
        spawnZ.Value = row.Z;
        spawnDirection.Value = row.Direction;
        spawnCount.Value = row.Count;
        spawnRespawn.Value = row.RespawnSeconds;
        spawnRange.Value = row.SpawnRange;
        spawnPersistBtn.Visible = row.CanPersist;
        SpawnWindow.Visible = true;
        Shown = true;
        Callable.From(SpawnWindow.ResetSize).CallDeferred();
    }

    internal void Close()
    {
        if (!Shown) return;
        Shown = false;
        SpawnWindow.Visible = false;
    }

    private void SendSpawnEdit(bool persist)
    {
        if (spawnRow is not { } row) return;
        setStatus(persist ? $"Persisting spawn row {row.Index}…" : $"Setting spawn row {row.Index}…", false);
        requests.SpawnEdit(persist, row.Index, (int)spawnX.Value, (int)spawnZ.Value,
            (int)spawnDirection.Value, (int)spawnCount.Value, (int)spawnRespawn.Value, (int)spawnRange.Value);
    }
}
