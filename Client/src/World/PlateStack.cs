using Godot;

namespace LibreKO;

public sealed class PlateStack
{
    private readonly Label3D _name;

    private Label3D? _title;
    private Label3D? _clan;

    public PlateStack(Label3D name)
    {
        _name = name;
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

    private bool? _visible;

    public void SetVisible(bool visible)
    {
        if (_visible == visible) return;
        _visible = visible;
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
            // Parent the clan/title label under the name label so it inherits the
            // name's transform (movement, sit/stand height) and always follows it.
            label = make(text, 0f);
            label.Position = Vector3.Zero;
            label.Visible = _name.Visible;
            _name.AddChild(label);
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
            NamePlate.Stack(label, line);
            label.RenderPriority = priority;
            label.OutlineRenderPriority = priority - 1;
            line++;
        }
    }
}
