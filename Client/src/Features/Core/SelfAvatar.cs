using Godot;

namespace LibreKO.Features.Core;

internal sealed class SelfAvatar
{
    public int CharId;
    public CharacterBody3D Body = null!;
    public Node3D Visual = null!;
    public Transform3D StandingVisualTransform;
    public AnimationPlayer? Anim;
    public string? Clip;
    public double ActionUntil;
    public int ActionRank;
    public int ActionAnim = ActionAnimation.NoActionAnim;
    public int StrikeAnim = ActionAnimation.NoActionAnim;
    public const int NoStrikeTarget = -1;
    public const float DefaultBodyRadius = 0.9f;
    public readonly AttackChain AttackChain = new();
    public int StrikeTarget = NoStrikeTarget;
    public Flinch? Flinch;
    public float Lift = PlayerSystem.CapsuleHalf;
    public CapsuleShape3D? Capsule;
    public float CapsuleOffsetY;
    public bool NoClip;
    public Vector3 LastFreePosition;
    public double QuietWarpsUntil;
    public bool Moving;
    public bool MovingBackward;
    public bool Running = true;
    public bool Dead;
    public float MapX;
    public float MapY;
    public float MapZ;
    public Vector3 Facing = Vector3.Forward;
    public bool MoveKeyPressed;
    public bool Sitting;
    public HoverClips? Hover;
    private Label3D? nameTag;

    public Label3D? NameTag
    {
        get
        {
            if (Body != null && (nameTag == null || !GodotObject.IsInstanceValid(nameTag)))
                nameTag = NodeSearch.FindFirst<Label3D>(Body);
            return nameTag;
        }
    }
}
