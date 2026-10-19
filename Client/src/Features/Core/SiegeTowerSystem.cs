using Godot;

namespace LibreKO.Features.Core;

internal sealed class SiegeTowerSystem : IFeature
{
    private readonly IWorldContext context;
    private readonly SelfAvatar avatar;
    private readonly ClanSession clan;
    private readonly SelfSession selfSession;
    private readonly SiegeSession siege;
    private readonly ISiegeRequests siegeRequests;
    private readonly ScenerySystem scenery;

    public SiegeTowerSystem(IWorldContext context, ClanSession clan, SelfSession selfSession, SiegeSession siege, ISiegeRequests siegeRequests, SelfAvatar avatar, ScenerySystem scenery)
    {
        this.context = context;
        this.clan = clan;
        this.selfSession = selfSession;
        this.siege = siege;
        this.siegeRequests = siegeRequests;
        this.avatar = avatar;
        this.scenery = scenery;
    }

    public void Build()
    {
    }

    public void Dispose()
    {
    }

    private const int TowerNoButton = -1;
    private const int TowerBoardMode = 0;
    private const int TowerUseMode = 1;
    private const int DropOffLeave = 1;
    private const int DropOffCancel = 2;
    internal const float DropOffWidth = 85f;
    internal const float DropOffHeight = 25f;
    private const float DropOffBelowCentre = 100f;
    private const float DropOffBeside = 2f;
    private const double TowerWarpQuiet = 3.0;
    private static readonly Color TowerLineColour = new(1f, 1f, 0f);
    private static readonly Color TowerTownColour = new(1f, 0f, 1f);

    internal Button TowerButton { get; private set; } = null!;
    private int towerButtonMode = TowerNoButton;
    private CanvasLayer dropOffLayer = null!;
    internal Button DropOff { get; set; } = null!;
    private int dropOffMode;
    private bool onTower;
    private int towerHostId = -1;
    private double towerCooldownUntil;

    internal void BuildTowerButton(VBoxContainer box)
    {
        var row = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        box.AddChild(row);
        TowerButton = UiTheme.SmallButton("", "");
        TowerButton.Visible = false;
        TowerButton.Pressed += OnTowerButton;
        row.AddChild(TowerButton);
    }

    internal void SiegeTowerInit()
    {
        dropOffLayer = new CanvasLayer { Layer = UiLayers.SideWindows };
        context.Root.AddChild(dropOffLayer);
        DropOff = UiTheme.ActionButton("", "");
        DropOff.CustomMinimumSize = new Vector2(DropOffWidth, DropOffHeight);
        DropOff.Visible = false;
        DropOff.Pressed += OnDropOff;
        dropOffLayer.AddChild(DropOff);
        context.Root.GetViewport().SizeChanged += PlaceDropOff;
        siege.SiegeTowerEvent += OnSiegeTower;
    }

    internal void SiegeTowerDispose()
    {
        context.Root.GetViewport().SizeChanged -= PlaceDropOff;
        siege.SiegeTowerEvent -= OnSiegeTower;
    }

    internal static string TowerText(int id) => ItemData.Text(id, SiegeTower.Fallback(id));

    private void TowerLine((int Id, string Fallback) text) => context.Log.Notice(ItemData.Text(text.Id, text.Fallback), TowerLineColour);

    private void TowerLine(int id) => context.Log.Notice(TowerText(id), TowerLineColour);

    private int LookOf(int skillId) => SkillData.Get(skillId)?.TransformModelId ?? 0;

    private bool InSiegeLook => SiegeTower.InSiegeLook(LookOf(selfSession.TransformSkill));

    internal bool SiegeSkillAllowed(SkillData.Skill s)
    {
        var verdict = SiegeSkills.Check(s.Id, s.SelfAnim2, selfSession.TransformSkill != 0, LookOf(selfSession.TransformSkill), onTower, context.Zone,
            SiegeWar.OwnerSide(clan.MyClan.InClan ? clan.MyClan.ClanId : 0, clan.MyClan.InClan ? clan.MyClan.AllianceId : 0, siege.SiegeWar),
            SiegeTargetKind());
        switch (verdict)
        {
            case SiegeSkills.Verdict.Failed:
                context.Log.Add(TextTemplate.Fill(GameText.System(SiegeSkills.FailedText, "%s failed"), s.Name), CombatLogKind.Incoming);
                return false;
            case SiegeSkills.Verdict.ZoneRefused:
                context.Log.Notice(GameText.System(SiegeSkills.ZoneRefusedText, "It is not allowed in this zone."));
                return false;
            case SiegeSkills.Verdict.NotDefender:
                context.Log.Notice(GameText.System(SiegeSkills.NotDefenderText, "You cannot use Siege Member"));
                return false;
            default:
                return true;
        }
    }

    private SiegeSkills.TargetKind SiegeTargetKind()
    {
        if (context.Selection.SelectedId < 0 || context.Selection.SelectedId == context.Self.CharId || !context.Entities.Views.TryGetValue(context.Selection.SelectedId, out var e))
            return SiegeSkills.TargetKind.None;
        if (!e.IsNpc) return SiegeSkills.TargetKind.Player;
        return e.Attackable && NpcTypes.IsStructure(e.NpcType)
            ? SiegeSkills.TargetKind.HostileStructure
            : SiegeSkills.TargetKind.OtherNpc;
    }

    private int TowerModeFor(int id)
    {
        if (id < 0 || !context.Entities.Views.TryGetValue(id, out var e) || e.Dead || onTower) return TowerNoButton;
        if (!e.IsNpc && LookOf(e.TransformSkill) == SiegeTower.MovingTowerLook && !InSiegeLook
            && SiegeTower.MayBoard(context.Zone == SiegeWar.Delos, clan.MyClan.InClan ? clan.MyClan.ClanId : 0, clan.MyClan.InClan ? clan.MyClan.AllianceId : 0,
                e.KnightsId, e.AllianceId, e.Attackable))
            return TowerBoardMode;
        if (e.IsNpc && e.NpcType == SiegeTower.GuardTowerNpcType && selfSession.TransformSkill == 0 && !e.Attackable)
            return TowerUseMode;
        return TowerNoButton;
    }

    private int towerTargetId = -1;

    internal void SyncTowerButton(int shownId)
    {
        towerTargetId = shownId;
        int mode = TowerModeFor(shownId);
        if (mode == towerButtonMode) return;
        towerButtonMode = mode;
        TowerButton.Visible = mode != TowerNoButton;
        if (mode != TowerNoButton)
            TowerButton.Text = TowerText(mode == TowerBoardMode ? SiegeTower.BoardingText : SiegeTower.UseText);
    }

    private void OnTowerButton()
    {
        double left = towerCooldownUntil - context.Now;
        if (left > 0)
        {
            context.Log.Notice(TextTemplate.Fill(TowerText(SiegeTower.CooldownText), SiegeTower.CooldownSeconds(left)), TowerLineColour);
            return;
        }
        RequestTower(towerTargetId, towerButtonMode);
    }

    private void RequestTower(int id, int mode)
    {
        if (siege.SiegeTowerPending || mode == TowerNoButton || TowerModeFor(id) != mode) return;
        if (!context.Entities.Views.TryGetValue(id, out var e)) return;
        if (e.Body.Position.DistanceTo(avatar.Body.Position) > SiegeTower.BoardReach)
        {
            TowerLine(SiegeTower.TooFarText);
            return;
        }
        context.Movement.StopMoving();
        context.Movement.SendMoveStop();
        if (mode == TowerBoardMode)
        {
            if (siegeRequests.TowerBoard(id)) towerHostId = id;
        }
        else siegeRequests.TowerUse(id);
    }

    private void OnDropOff()
    {
        if (dropOffMode == DropOffCancel)
        {
            if (LookOf(selfSession.TransformSkill) == SiegeTower.GuardTowerLook) siegeRequests.TowerExit();
            return;
        }
        if (!onTower)
        {
            TowerLine(SiegeTower.NotOnboardText);
            return;
        }
        if (!context.Entities.Views.TryGetValue(towerHostId, out var tower))
        {
            siegeRequests.TowerLeave(SiegeTower.Wire(avatar.MapX), SiegeTower.Wire(avatar.MapZ));
            return;
        }
        var side = tower.Body.GlobalTransform.Basis.X.Normalized() * DropOffBeside;
        float x = tower.MapX + side.X, z = tower.MapZ + side.Z;
        var ground = scenery.EntityGroundPos(x, z, ScenerySystem.NoServerHeight, 0f);
        if (!float.IsFinite(ground.Y))
        {
            TowerLine(SiegeTower.UnloadableText);
            return;
        }
        siegeRequests.TowerLeave(SiegeTower.Wire(x), SiegeTower.Wire(z));
    }

    private void ShowDropOff(int mode)
    {
        dropOffMode = mode;
        DropOff.Text = TowerText(mode == DropOffCancel ? SiegeTower.CancelText : SiegeTower.ExitText);
        DropOff.Visible = true;
        PlaceDropOff();
    }

    private void PlaceDropOff()
    {
        if (DropOff == null || !GodotObject.IsInstanceValid(DropOff)) return;
        var screen = context.Root.GetViewport().GetVisibleRect().Size;
        DropOff.Position = new Vector2((screen.X - DropOffWidth) * 0.5f, (screen.Y - DropOffHeight) * 0.5f + DropOffBelowCentre);
    }

    private void OnSiegeTower(SiegeTowerReply reply)
    {
        bool ok = reply.Result == SiegeTowerResult.Succeeded;
        switch (reply.Sub)
        {
            case SiegeTowerSubOpcode.Board:
                if (!ok) { towerHostId = -1; TowerLine(SiegeTower.BoardRefusal((byte)reply.Result)); break; }
                onTower = true;
                avatar.QuietWarpsUntil = context.Now + TowerWarpQuiet;
                ShowDropOff(DropOffLeave);
                break;
            case SiegeTowerSubOpcode.Leave:
                if (!ok) { TowerLine(SiegeTower.LeaveRefusal((byte)reply.Result)); break; }
                avatar.QuietWarpsUntil = context.Now + TowerWarpQuiet;
                LeaveTower();
                break;
            case SiegeTowerSubOpcode.Ended:
                TowerLine(SiegeTower.EndedText(reply.Result));
                LeaveTower();
                break;
            case SiegeTowerSubOpcode.Use:
                if (!ok) { TowerLine(SiegeTower.UseRefusal((byte)reply.Result)); break; }
                if (reply.UserId == context.Self.CharId)
                {
                    avatar.QuietWarpsUntil = context.Now + TowerWarpQuiet;
                    ShowDropOff(DropOffCancel);
                }
                else PlaceOnTower(reply.UserId, reply.X, reply.Z);
                break;
            case SiegeTowerSubOpcode.Exit:
                if (!ok) { TowerLine(SiegeTower.ExitRefusal); break; }
                if (reply.UserId != context.Self.CharId) break;
                DropOff.Visible = false;
                towerCooldownUntil = context.Now + SiegeTower.Cooldown;
                break;
        }
    }

    private void LeaveTower()
    {
        DropOff.Visible = false;
        onTower = false;
        towerHostId = -1;
        towerCooldownUntil = context.Now + SiegeTower.Cooldown;
        if (Fx.NameForId(SiegeTower.LeaveEffect) is { } fx) context.Entities.SpawnFxOn(context.Self.CharId, fx, 0f);
    }

    private void PlaceOnTower(int charId, float x, float z)
    {
        if (!context.Entities.Views.TryGetValue(charId, out var e)) return;
        var pos = scenery.EntityGroundPos(x, z, ScenerySystem.NoServerHeight, e.Lift);
        e.Body.Position = pos;
        e.Target = pos;
        e.HasTarget = true;
        e.Speed = 0f;
        e.MapX = x;
        e.MapZ = z;
    }

    internal bool RefusedTownOnTower()
    {
        if (!onTower && !InSiegeLook) return false;
        context.Log.Notice(TowerText(SiegeTower.TownRefusedText), TowerTownColour);
        return true;
    }
}
