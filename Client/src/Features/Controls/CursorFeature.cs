using Godot;

namespace LibreKO.Features.Controls;

internal sealed class CursorFeature : IFeature
{
    private readonly IWorldContext context;
    private readonly SelfSession self;
    private readonly BuffSystem buff;
    private readonly RepairFeature repair;
    private readonly SelectionFeature selection;
    private readonly BasicAttackSystem basicAttack;

    public CursorFeature(IWorldContext context, SelfSession self, BuffSystem buff, RepairFeature repair, SelectionFeature selection, BasicAttackSystem basicAttack)
    {
        this.context = context;
        this.self = self;
        this.buff = buff;
        this.repair = repair;
        this.selection = selection;
        this.basicAttack = basicAttack;
    }

    private const float CursorHoverRadius = 48f;

    private double cursorHoverAccum;

    private bool cursorHoverAttackable;

    public void Build()
    {
        GameCursor.Enable();
        GameCursor.SetNation(self.LastEnter.Nation);
    }

    public void Dispose() => GameCursor.Set(GameCursorKind.Arrow);

    internal void CursorTick(double delta)
    {
        GameCursor.SetNation(self.LastEnter.Nation);

        if (buff.ContinuanceSkill != 0 && Input.IsMouseButtonPressed(MouseButton.Right)) buff.CancelContinuance();

        cursorHoverAccum += delta;
        if (cursorHoverAccum >= 1.0 / CrowdModes.Of(Config.Crowd).HoverHz)
        {
            cursorHoverAccum = 0;
            cursorHoverAttackable = PointerOverAttackable();
        }

        GameCursor.Set(CurrentCursorKind());
    }

    private GameCursorKind CurrentCursorKind()
    {
        if (repair.IsOpen) return repair.InFlight ? GameCursorKind.RepairAlt : GameCursorKind.Repair;

        if (buff.ContinuanceSkill != 0) return GameCursorKind.Held;

        if (basicAttack.AutoAttack || cursorHoverAttackable) return GameCursorKind.Attack;

        if (Input.IsMouseButtonPressed(MouseButton.Left)) return GameCursorKind.Held;

        return GameCursorKind.Arrow;
    }

    private bool PointerOverAttackable()
    {
        if (context.Camera == null) return false;
        if (context.Root.GetViewport().GuiGetHoveredControl() != null) return false;
        var hit = selection.PickEntityAt(context.Root.GetViewport().GetMousePosition(), CursorHoverRadius, out _, out _);
        return hit is { Attackable: true, Dead: false };
    }
}
