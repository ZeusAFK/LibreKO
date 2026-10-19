using Godot;

namespace LibreKO.Features.Windows;

internal sealed class PvpFeature : IFeature, ILateTickFeature
{
    private readonly IWorldContext context;
    private readonly PvpSession pvp;
    private readonly SelfSession self;

    public PvpFeature(IWorldContext context, PvpSession pvp, SelfSession self)
    {
        this.context = context;
        this.pvp = pvp;
        this.self = self;
    }

    public void LateTick(double delta, double nowSec)
    {
        PvpTick(nowSec);
    }

    private int rivalId = -1;
    private string rivalName = "";
    private string clanName = "";
    private int gauge;
    private bool full;
    private bool marked;
    private Node3D? marker;

    private static readonly Color LogColour = new(1f, 1f, 0f);
    private static readonly Color RivalCol = new("e0574a");

    private CanvasLayer layer = null!;
    private PanelContainer chip = null!;
    private StyleBoxFlat chipStyle = null!;
    private Label nameLbl = null!;
    private HBoxContainer pipRow = null!;

    private const int MaxGauge = 5;
    private const float ChipTop = 150f;

    public void Build()
    {
        BuildChip();
        pvp.PvpRivalAssignedEvent += OnRivalAssigned;
        pvp.PvpRivalRemovedEvent += OnRivalRemoved;
        pvp.PvpAngerEvent += OnAnger;
    }

    public void Dispose()
    {
        pvp.PvpRivalAssignedEvent -= OnRivalAssigned;
        pvp.PvpRivalRemovedEvent -= OnRivalRemoved;
        pvp.PvpAngerEvent -= OnAnger;
    }

    private void BuildChip()
    {
        layer = new CanvasLayer { Layer = UiLayers.Hud, Visible = false };
        context.Root.AddChild(layer);

        chip = new PanelContainer
        {
            OffsetLeft = 16f, OffsetTop = ChipTop,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        chipStyle = UiTheme.Chip();
        chipStyle.BorderColor = new Color(RivalCol, 0.85f);
        chip.AddThemeStyleboxOverride("panel", chipStyle);
        layer.AddChild(chip);

        var col = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        col.AddThemeConstantOverride("separation", 4);
        chip.AddChild(col);

        nameLbl = UiTheme.Text("Rival", 13, RivalCol, HorizontalAlignment.Center);
        nameLbl.MouseFilter = Control.MouseFilterEnum.Ignore;
        col.AddChild(nameLbl);

        var angerRow = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        angerRow.AddThemeConstantOverride("separation", 6);
        col.AddChild(angerRow);

        var angerLbl = UiTheme.Text("Anger", 11, UiTheme.TextLo, HorizontalAlignment.Left);
        angerLbl.MouseFilter = Control.MouseFilterEnum.Ignore;
        angerRow.AddChild(angerLbl);

        pipRow = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        pipRow.AddThemeConstantOverride("separation", 3);
        angerRow.AddChild(pipRow);
        for (int i = 0; i < MaxGauge; i++)
        {
            var pip = new PanelContainer
            {
                CustomMinimumSize = new Vector2(10, 10),
                MouseFilter = Control.MouseFilterEnum.Ignore,
            };
            pip.AddThemeStyleboxOverride("panel", PipStyle(false, false));
            pipRow.AddChild(pip);
        }

        HudLayout.Attach(chip, "hud_pvp_rival", chip, () => chip.Position);
    }

    private static StyleBoxFlat PipStyle(bool lit, bool full)
    {
        var sb = new StyleBoxFlat
        {
            BgColor = lit ? (full ? new Color("ffb23c") : RivalCol) : new Color(0.16f, 0.15f, 0.16f, 0.9f),
            BorderColor = new Color(RivalCol, lit ? 0.95f : 0.4f),
        };
        sb.SetBorderWidthAll(1);
        sb.SetCornerRadiusAll(2);
        return sb;
    }

    private void OnRivalAssigned(int rivalCharId, string rivalName, string clanName)
    {
        ClearRivalMarker();

        rivalId = rivalCharId;
        this.rivalName = string.IsNullOrEmpty(rivalName) ? "Unknown" : rivalName;
        this.clanName = clanName ?? "";
        gauge = 0;
        full = false;

        nameLbl.Text = this.clanName.Length > 0 ? $"Rival: {this.rivalName} [{this.clanName}]" : $"Rival: {this.rivalName}";
        RefreshPips();
        layer.Visible = self.Sheet.Level >= PvpRival.MinLevel;

        if (!string.IsNullOrEmpty(rivalName))
            context.Log.Notice(ItemData.Text(PvpRival.AssignedText, "%s is now your rival.").Replace("%s", rivalName), LogColour);

        TryMarkRival();
    }

    private void OnRivalRemoved()
    {
        if (rivalId >= 0)
            context.Log.Notice(ItemData.Text(PvpRival.CancelledText, "Rivalry cancelled."), LogColour);
        ClearRivalMarker();
        rivalId = -1;
        rivalName = "";
        clanName = "";
        gauge = 0;
        full = false;
        if (layer != null) layer.Visible = false;
    }

    private void OnAnger(int gauge, bool full)
    {
        this.gauge = Mathf.Clamp(gauge, 0, MaxGauge);
        this.full = full;
        RefreshPips();
    }

    private void RefreshPips()
    {
        var pips = pipRow.GetChildren();
        for (int i = 0; i < pips.Count; i++)
        {
            if (pips[i] is not PanelContainer pip) continue;
            bool lit = i < gauge;
            pip.AddThemeStyleboxOverride("panel", PipStyle(lit, lit && full));
        }
    }

    private void TryMarkRival()
    {
        if (marked || rivalId < 0) return;
        if (!PvpRival.MarksIn(context.Zone) || self.Sheet.Level < PvpRival.MinLevel) return;
        if (context.Entities.BodyOf(rivalId) is not { } body || !GodotObject.IsInstanceValid(body)) return;

        marker = Fx.Spawn(PvpRival.MarkerFx, body, Vector3.Zero);
        marked = marker != null;
    }

    private void ClearRivalMarker()
    {
        if (marker != null)
        {
            if (GodotObject.IsInstanceValid(marker)) marker.QueueFree();
            marker = null;
        }
        marked = false;
    }

    private void PvpTick(double now)
    {
        if (rivalId < 0) return;

        if (!marked)
        {
            TryMarkRival();
            return;
        }

        if (context.Entities.Find(rivalId) == null || marker == null || !GodotObject.IsInstanceValid(marker))
        {
            marker = null;
            marked = false;
        }
    }
}
