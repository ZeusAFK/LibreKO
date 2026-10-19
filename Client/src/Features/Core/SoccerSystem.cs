using Godot;

namespace LibreKO.Features.Core;

internal sealed class SoccerSystem : IFeature
{
    private readonly IWorldContext context;
    private readonly SelfAvatar avatar;
    private EventPlatesFeature? eventPlates;
    private readonly ProgressSession progress;
    private readonly SelfSession self;
    private readonly SoccerSession soccer;
    private readonly ISoccerRequests soccerRequests;
    private readonly ScenerySystem scenery;
    private readonly EntitySystem entities;

    public SoccerSystem(IWorldContext context, ProgressSession progress, SelfSession self, SoccerSession soccer, ISoccerRequests soccerRequests, SelfAvatar avatar, ScenerySystem scenery, EntitySystem entities)
    {
        this.context = context;
        this.progress = progress;
        this.self = self;
        this.soccer = soccer;
        this.soccerRequests = soccerRequests;
        this.avatar = avatar;
        this.scenery = scenery;
        this.entities = entities;
    }

    public void Build()
    {
        entities.EntitySpawned += OnEntitySpawned;
        entities.EntityRemoved += SoccerForgetEntity;
    }

    public void Dispose()
    {
        entities.EntitySpawned -= OnEntitySpawned;
        entities.EntityRemoved -= SoccerForgetEntity;
    }

    private void OnEntitySpawned(EntityView e, EntitySnapshot info)
    {
        if (!info.IsNpc && info.SoccerTeam != Soccer.TeamNone) ApplySoccerTeam(info.Id, info.SoccerTeam);
    }

    private const string SoccerArt = "res://assets/ui/soccer/";
    private const string SoccerKicksFile = SoccerArt + "kicks.json";
    private const string SoccerBoardLayoutId = "hud_soccer_score";
    private const int SoccerKickXSlot = 0;
    private const int SoccerKickYSlot = 1;
    private const int SoccerKickZSlot = 2;
    private static readonly Color SoccerBlueColor = WarScoreStrip.ElmoradColour;
    private static readonly Color SoccerRedColor = WarScoreStrip.KarusColour;
    private static readonly Color SoccerNoticeColor = new(1f, 1f, 0f);

    internal CanvasLayer SoccerLayer { get; set; } = null!;
    private WarScoreStrip? soccerBoard;
    private double soccerSeconds;
    private bool soccerZoneChecked;

    private readonly Dictionary<int, int> soccerTeams = new();

    private readonly Dictionary<int, Node3D> soccerFx = new();

    private readonly Dictionary<int, int> soccerKicks = new();

    internal void AttachPlates(EventPlatesFeature eventPlates) => this.eventPlates = eventPlates;

    internal void SoccerInit()
    {
        SoccerLayer = new CanvasLayer { Layer = UiLayers.EventScoreboards };
        context.Root.AddChild(SoccerLayer);
        LoadSoccerKicks();
        soccer.SoccerGoalEvent += OnSoccerGoal;
        soccer.SoccerClockEvent += OnSoccerClock;
        soccer.SoccerSyncEvent += OnSoccerSync;
        soccer.SoccerEndEvent += OnSoccerEnd;
        progress.StateChangeEvent += OnSoccerStateChange;
    }

    internal void SoccerDispose()
    {
        soccer.SoccerGoalEvent -= OnSoccerGoal;
        soccer.SoccerClockEvent -= OnSoccerClock;
        soccer.SoccerSyncEvent -= OnSoccerSync;
        soccer.SoccerEndEvent -= OnSoccerEnd;
        progress.StateChangeEvent -= OnSoccerStateChange;
    }

    private void LoadSoccerKicks()
    {
        using var file = Godot.FileAccess.Open(SoccerKicksFile, Godot.FileAccess.ModeFlags.Read);
        if (file == null) return;
        foreach (var (id, distance) in Json.ParseString(file.GetAsText()).AsGodotDictionary())
            soccerKicks[id.AsString().ToInt()] = distance.AsInt32();
    }

    internal bool OnSoccerPitch()
    {
        foreach (var area in ZoneAreas.For(context.Zone))
            if (area.Id == Soccer.PitchRegion && area.Contains(context.Self.MapXZ.X, context.Self.MapXZ.Z)) return true;
        return false;
    }

    internal bool SelfOnSoccerTeam => soccer.SoccerTeam != Soccer.TeamNone;

    internal bool SoccerSkillAllowed(SkillData.Skill s) =>
        Soccer.SkillAllowed(SkillReach.IsSoccerSkill(s.Id), OnSoccerPitch(), SelfOnSoccerTeam, BuffRules.IsSpeedBuff(s));

    internal short[]? KickRequestData(int skillId, int targetId)
    {
        if (!soccerKicks.TryGetValue(skillId, out int distance) || !context.Entities.Views.TryGetValue(targetId, out var ball)) return null;
        var dest = Soccer.Landing(ball.Body.GlobalPosition, context.Self.Body.GlobalPosition, distance);
        var (mapX, mapZ) = scenery.WorldToMap(dest);
        var data = new short[MagicData.Length];
        data[SoccerKickXSlot] = (short)(int)mapX;
        data[SoccerKickYSlot] = (short)(int)dest.Y;
        data[SoccerKickZSlot] = (short)(int)mapZ;
        return data;
    }

    internal void SoccerTick(double delta)
    {
        if (!soccerZoneChecked)
        {
            soccerZoneChecked = true;
            if (SelfOnSoccerTeam && !OnSoccerPitch())
            {
                soccer.LeaveTeam();
                HideSoccerBoard();
            }
            else if (SelfOnSoccerTeam) ApplySoccerTeam(context.Self.CharId, soccer.SoccerTeam);
        }
        if (soccerBoard is not { Visible: true } board || soccerSeconds <= 0) return;
        soccerSeconds = Mathf.Max(0, soccerSeconds - delta);
        board.Centre = Soccer.Clock(soccerSeconds);
    }

    private void OnSoccerStateChange(int charId, int type, int value)
    {
        if (type != Soccer.StateChangeTeam) return;
        ApplySoccerTeam(charId, value);
        if (charId != context.Self.CharId) return;
        if (value != Soccer.TeamNone)
        {
            if (soccerBoard?.CloseButton is { } close) close.Visible = false;
            return;
        }
        HideSoccerBoard();
        if (OnSoccerPitch())
            ShowSoccerNotice(GameText.System(Soccer.ChooseTeamText, "Please choose a team from arena's manager."));
    }

    internal void ApplySoccerTeam(int charId, int team)
    {
        if (soccerFx.Remove(charId, out var running) && GodotObject.IsInstanceValid(running)) running.QueueFree();
        if (team == Soccer.TeamNone) soccerTeams.Remove(charId);
        else soccerTeams[charId] = team;

        var body = charId == context.Self.CharId ? context.Self.Body : context.Entities.Views.TryGetValue(charId, out var e) ? e.Body : null;
        if (body == null || !GodotObject.IsInstanceValid(body) || Fx.NameForId(Soccer.FxFor(team)) is not { } fxName) return;
        if (Fx.Spawn(fxName, body, Vector3.Zero) is { } fx) soccerFx[charId] = fx;
    }

    internal void SoccerForgetEntity(int id)
    {
        soccerTeams.Remove(id);
        soccerFx.Remove(id);
    }

    private int SoccerTeamOf(int id) => id == context.Self.CharId ? soccer.SoccerTeam : soccerTeams.GetValueOrDefault(id);

    private void OnSoccerGoal(SoccerGoal goal)
    {
        var board = EnsureSoccerBoard();
        if (!board.Visible)
        {
            ResetSoccerBoard();
            board.Visible = true;
        }
        SetSoccerScores(goal.Blue, goal.Red);

        string kicker = goal.KickerId == context.Self.CharId ? self.LastEnter.Name
            : context.Entities.Views.TryGetValue(goal.KickerId, out var e) && !e.IsNpc ? e.Name : "";
        if (kicker.Length > 0)
        {
            bool own = Soccer.IsOwnGoal(SoccerTeamOf(goal.KickerId), goal.GoalTeam);
            string text = own
                ? GameText.System(Soccer.OwnGoalText, "%s, you made an own goal. LOL ~~", kicker)
                : GameText.System(Soccer.GoalText, "%s Goal! Goal! Goal!", kicker);
            context.Log.Notice(text, SoccerNoticeColor);
        }

        int mine = soccer.SoccerTeam;
        if (mine != Soccer.TeamNone && mine == goal.KickerTeam && goal.KickerTeam != goal.GoalTeam)
        {
            avatar.Moving = false;
            context.Movement.CancelMoveTo();
            context.Movement.CancelAutoRun();
            context.Movement.SendMoveStop();
            context.Selection.Deselect();
            soccerRequests.Emote(Soccer.FirstProvokeEmote + (int)(GD.Randi() % Soccer.ProvokeEmotes));
        }
    }

    private void OnSoccerClock(int seconds)
    {
        var board = EnsureSoccerBoard();
        ResetSoccerBoard();
        board.Visible = true;
        SetSoccerScores(0, 0);
        soccerSeconds = seconds;
        board.Centre = Soccer.Clock(seconds);
    }

    internal void OnSoccerSync(int seconds, int blue, int red)
    {
        var board = EnsureSoccerBoard();
        ResetSoccerBoard();
        board.Visible = true;
        SetSoccerScores(blue, red);
        soccerSeconds = seconds;
        board.Centre = Soccer.Clock(seconds);
    }

    private void OnSoccerEnd(SoccerEnd end)
    {
        HideSoccerBoard();
        var (textId, blueFirst) = Soccer.Result(end.Winner);
        int first = blueFirst ? end.Blue : end.Red, second = blueFirst ? end.Red : end.Blue;
        string text = textId == Soccer.DrawText
            ? GameText.System(Soccer.DrawText, "This game ended in a draw, %d - %d")
            : GameText.System(Soccer.WinText, "%s team won this game, %d - %d", end.Winner == Soccer.TeamBlue
                ? GameText.System(Soccer.BlueText, "Blue")
                : GameText.System(Soccer.RedText, "Red"));
        int at = text.IndexOf("%d", System.StringComparison.Ordinal);
        if (at >= 0) text = text.Remove(at, 2).Insert(at, first.ToString());
        at = text.IndexOf("%d", System.StringComparison.Ordinal);
        if (at >= 0) text = text.Remove(at, 2).Insert(at, second.ToString());
        context.Log.Notice(text, SoccerNoticeColor);
    }

    private void ShowSoccerNotice(string text)
    {
        var box = new AcceptDialog { DialogText = text, Unresizable = true, Exclusive = false };
        box.Confirmed += box.QueueFree;
        box.Canceled += box.QueueFree;
        SoccerLayer.AddChild(box);
        box.PopupCentered();
    }

    private void HideSoccerBoard()
    {
        if (soccerBoard == null) return;
        ResetSoccerBoard();
        soccerBoard.Visible = false;
    }

    private void ResetSoccerBoard()
    {
        soccerSeconds = 0;
        if (soccerBoard == null) return;
        soccerBoard.Centre = Soccer.Clock(0);
        SetSoccerScores(0, 0);
        if (soccerBoard.CloseButton is { } close) close.Visible = !SelfOnSoccerTeam;
    }

    private void SetSoccerScores(int blue, int red) =>
        soccerBoard?.SetScores(Mathf.Max(0, blue), Mathf.Max(0, red));

    internal WarScoreStrip EnsureSoccerBoard()
    {
        if (soccerBoard != null) return soccerBoard;
        var board = new WarScoreStrip(withStatus: false,
            GameText.System(Soccer.BlueText, "Blue"), SoccerBlueColor,
            GameText.System(Soccer.RedText, "Red"), SoccerRedColor, closable: true)
        {
            Visible = false,
        };
        SoccerLayer.AddChild(board);
        soccerBoard = board;
        board.CloseButton!.Pressed += () =>
        {
            if (SelfOnSoccerTeam) return;
            HideSoccerBoard();
        };
        var layout = HudLayout.Attach(board, SoccerBoardLayoutId, board, () => eventPlates!.PlateSpot(board));
        eventPlates!.AddPlate(board, layout, SoccerBoardLayoutId);
        return board;
    }
}
