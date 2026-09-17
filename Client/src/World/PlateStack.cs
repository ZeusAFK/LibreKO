using Godot;

namespace LibreKO;

public sealed class PlateStack
{
    private readonly Label3D _name;
    private readonly Node _parent;

    private Label3D? _title;
    private Label3D? _clan;

    public PlateStack(Label3D name)
    {
        _name = name;
        _parent = name.GetParent();
    }

    public Label3D Name => _name;

    public void SetTitle(string text)
    {
        _title = Set(_title, text, NamePlate.MakeTitle);
        Layout();
    }

    public void SetClan(string text)
    {
        _clan = Set(_clan, text, NamePlate.MakeClan);
        Layout();
    }

    public void SetVisible(bool visible)
    {
        if (_name.Visible != visible) _name.Visible = visible;
        if (_title != null && _title.Visible != visible) _title.Visible = visible;
        if (_clan != null && _clan.Visible != visible) _clan.Visible = visible;
    }

    private Label3D? Set(Label3D? label, string text, System.Func<string, float, Label3D> make)
    {
        if (text.Length == 0)
        {
            if (label != null && GodotObject.IsInstanceValid(label)) label.QueueFree();
            label = null;
        }
        else if (label == null || !GodotObject.IsInstanceValid(label))
        {
            label = make(text, _name.Position.Y);
            label.Position = _name.Position;
            label.Visible = _name.Visible;
            _parent.AddChild(label);
        }
        else
        {
            label.Text = text;
        }

        return label;
    }

    private void Layout()
    {
        int line = 1;
        foreach (var label in new[] { _title, _clan })
        {
            if (label == null || !GodotObject.IsInstanceValid(label)) continue;
            int priority = NamePlate.StackPriority + line * 2;
            label.Offset = new Vector2(0f, line * NamePlate.LinePx);
            label.RenderPriority = priority;
            label.OutlineRenderPriority = priority - 1;
            line++;
        }
    }
}
