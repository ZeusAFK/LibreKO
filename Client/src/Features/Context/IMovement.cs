using Godot;

namespace LibreKO.Features;

internal interface IMovement
{
    void StopForInteraction();

    void SetCollisions(bool on);

    void MoveTo(Vector3 target);

    void CancelMoveTo();

    void Face(Vector3 direction);

    void StopMoving();

    void SendMoveStop();

    void CancelClickMove();

    void CancelAutoRun();
}
