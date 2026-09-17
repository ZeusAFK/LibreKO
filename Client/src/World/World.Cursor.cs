using Godot;

namespace LibreKO;

public partial class World
{
    private const float CursorHoverRadius = 48f;

    private const double CursorHoverInterval = 1.0 / 30.0;

    private double _cursorHoverAccum;
    private bool _cursorHoverAttackable;

    private void CursorInit()
    {
        GameCursor.Enable();
        GameCursor.SetNation(Net.I.LastEnter.Nation);
    }

    private void CursorDispose() => GameCursor.Set(GameCursorKind.Arrow);

    private void CursorTick(double delta)
    {
        GameCursor.SetNation(Net.I.LastEnter.Nation);

        _cursorHoverAccum += delta;
        if (_cursorHoverAccum >= CursorHoverInterval)
        {
            _cursorHoverAccum = 0;
            _cursorHoverAttackable = PointerOverAttackable();
        }

        GameCursor.Set(CurrentCursorKind());
    }

    private GameCursorKind CurrentCursorKind()
    {
        if (_repairShown) return _repairInFlight ? GameCursorKind.RepairAlt : GameCursorKind.Repair;

        if (_autoAttack || _cursorHoverAttackable) return GameCursorKind.Attack;

        if (Input.IsMouseButtonPressed(MouseButton.Left)) return GameCursorKind.Held;

        return GameCursorKind.Arrow;
    }

    private bool PointerOverAttackable()
    {
        if (_camera == null) return false;
        if (GetViewport().GuiGetHoveredControl() != null) return false;
        var hit = PickEntityAt(GetViewport().GetMousePosition(), CursorHoverRadius, out _, out _);
        return hit is { Attackable: true, Dead: false };
    }
}
