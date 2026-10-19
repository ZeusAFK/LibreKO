using Godot;

namespace LibreKO.Features;

internal interface ISelection
{
    int SelectedId { get; }

    void Select(int id);

    void Deselect();

    void FaceSelectedWhenStill();

    bool Selectable(EntityView e);

    EntityView? PickAt(Vector2 screen, out int id);

    void WithSelected(int id, Action action);
}
