using Godot;

namespace LibreKO.Features.Hud;

internal sealed class TargetEffectsFeature : IFeature
{
    private readonly IWorldContext context;
    private readonly TargetStateSession state;
    private readonly ITargetStateRequests stateRequests;
    private readonly EffectFeature effect;

    public TargetEffectsFeature(IWorldContext context, TargetStateSession targetState, ITargetStateRequests targetStateRequests, EffectFeature effect)
    {
        this.context = context;
        state = targetState;
        stateRequests = targetStateRequests;
        this.effect = effect;
    }

    private const int PerRow = 10;
    private const float EffectSize = 22f;
    private const double PollInterval = 1.0;
    private const int EffectTooltipWidth = 44;
    private const int EffectTimerFont = 9;
    private const double SecondsPerMinute = 60.0;
    private static readonly Color EffectHarmful = new(0.85f, 0.3f, 0.3f);
    private static readonly Color EffectHelpful = new(0.79f, 0.64f, 0.15f);

    private GridContainer row = null!;
    private readonly List<(TargetEffect Effect, Label Timer)> targetEffectCells = new();
    private int targetEffectsFor = -1;
    private double receivedAt;
    private double pollAt;

    public void Build() => state.TargetEffectsEvent += On;

    public void Dispose() => state.TargetEffectsEvent -= On;

    internal void BuildRow(VBoxContainer frame)
    {
        row = new GridContainer
        {
            Columns = PerRow,
            Visible = false,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        row.AddThemeConstantOverride("h_separation", 2);
        row.AddThemeConstantOverride("v_separation", 2);
        frame.AddChild(row);
    }

    internal void PollTick(double now)
    {
        if (context.Selection.SelectedId < 0 || now < pollAt) return;
        pollAt = now + PollInterval;
        if (context.Entities.Views.TryGetValue(context.Selection.SelectedId, out var e) && !e.Dead && context.Selection.SelectedId != context.Self.CharId)
            stateRequests.TargetEffectsRequest(context.Selection.SelectedId);
    }

    private void On(TargetEffectsReply reply)
    {
        if (reply.TargetId != context.Selection.SelectedId) return;
        receivedAt = context.Now;
        if (reply.TargetId == targetEffectsFor && SameEffects(reply.Effects))
        {
            for (int i = 0; i < targetEffectCells.Count; i++)
                targetEffectCells[i] = (reply.Effects[i], targetEffectCells[i].Timer);
            return;
        }
        targetEffectsFor = reply.TargetId;
        Rebuild(reply.Effects);
    }

    private bool SameEffects(IReadOnlyList<TargetEffect> effects)
    {
        if (effects.Count != targetEffectCells.Count) return false;
        for (int i = 0; i < effects.Count; i++)
            if (effects[i].SkillId != targetEffectCells[i].Effect.SkillId) return false;
        return true;
    }

    private void Rebuild(IReadOnlyList<TargetEffect> effects)
    {
        foreach (var child in row.GetChildren()) child.QueueFree();
        targetEffectCells.Clear();
        foreach (var targetEffect in effects)
        {
            if (SkillData.Get(targetEffect.SkillId) is not { } s) continue;
            targetEffectCells.Add((targetEffect, AddEffectCell(s)));
        }
        row.Visible = targetEffectCells.Count > 0;
    }

    private Label AddEffectCell(SkillData.Skill s)
    {
        bool harmful = s.IsEnemy || s.MoveSpeedPercent < 100;
        var cell = new PanelContainer
        {
            CustomMinimumSize = new Vector2(EffectSize, EffectSize),
            MouseFilter = Control.MouseFilterEnum.Pass,
            TooltipText = $"{s.Name}\n{TextWrap.Wrap(s.Desc, EffectTooltipWidth)}",
        };
        var style = new StyleBoxFlat
        {
            BgColor = new Color(0.05f, 0.05f, 0.07f, 0.85f),
            BorderColor = harmful ? EffectHarmful : EffectHelpful,
        };
        style.SetCornerRadiusAll(3);
        style.SetBorderWidthAll(1);
        cell.AddThemeStyleboxOverride("panel", style);

        if (SkillIcons.Get(s.Id) is { } tex)
            cell.AddChild(new TextureRect
            {
                Texture = tex,
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                MouseFilter = Control.MouseFilterEnum.Ignore,
            });
        else
            cell.AddChild(new Label
            {
                Text = s.Name.Length > 0 ? s.Name[..1] : "?",
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                MouseFilter = Control.MouseFilterEnum.Ignore,
            });

        var timer = new Label
        {
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        timer.AddThemeFontSizeOverride("font_size", EffectTimerFont);
        timer.AddThemeColorOverride("font_outline_color", Colors.Black);
        timer.AddThemeConstantOverride("outline_size", 3);
        cell.AddChild(timer);
        row.AddChild(cell);
        return timer;
    }

    internal void TickTargetEffects()
    {
        if (targetEffectsFor >= 0 && targetEffectsFor != context.Selection.SelectedId)
        {
            targetEffectsFor = -1;
            Rebuild(System.Array.Empty<TargetEffect>());
            pollAt = 0;
        }
        if (targetEffectCells.Count == 0) return;
        double elapsed = context.Now - receivedAt;
        foreach (var (targetEffect, timer) in targetEffectCells)
        {
            double left = Mathf.Max(0, targetEffect.Seconds - (float)elapsed);
            timer.Text = left >= SecondsPerMinute ? $"{left / SecondsPerMinute:0}M" : $"{left:0}S";
        }
    }
}
