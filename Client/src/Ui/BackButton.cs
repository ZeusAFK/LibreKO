using Godot;

namespace LibreKO;

public partial class BackButton : Node
{
    public override void _Ready()
    {
        if (Engine.GetMainLoop() is SceneTree tree) tree.QuitOnGoBack = false;
    }

    public override void _Notification(int what)
    {
        if (what != NotificationWMGoBackRequest) return;
        Send(true);
        Send(false);
    }

    private static void Send(bool pressed) => Input.ParseInputEvent(new InputEventKey
    {
        Keycode = Key.Escape,
        PhysicalKeycode = Key.Escape,
        Pressed = pressed,
    });
}
