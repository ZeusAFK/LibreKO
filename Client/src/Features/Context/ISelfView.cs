using Godot;

namespace LibreKO.Features;

internal interface ISelfView
{
    int CharId { get; }

    bool IsGm { get; }

    bool Dead { get; }

    Node3D Body { get; }

    Node3D Visual { get; }

    PlateStack? Plate();

    (float X, float Z) MapXZ { get; }

    bool Moving { get; }

    float Lift { get; }

    Vector3 Facing { get; }

    int TransformModel();

    bool Sitting { get; }

    int[] Gear();

    void Redress(bool force = false);

    bool Cursed { get; }
}
