using Godot;

namespace LibreKO.Presentation;

public sealed class PlateStack
{
    private readonly Label3D name;
    private readonly Node parent;

    private Label3D? title;
    private string titleText = "";
    private Label3D? clan;
    private Label3D? status;
    private Label3D? seeking;
    private Sprite3D? mark;
    private Sprite3D? leader;
    private Sprite3D? rank;
    private Sprite3D? nameMark;
    private LibreKO.Domain.NameMarkKind nameMarkKind;
    private const string LeaderIcon = "system/star";

    public PlateStack(Label3D name)
    {
        this.name = name;
        parent = name.GetParent();
    }

    public Label3D Name => name;

    public void MoveTo(Vector3 position)
    {
        name.Position = position;
        foreach (var part in new Node3D?[] { title, clan, status, seeking, mark, leader, rank, nameMark })
            if (part != null && GodotObject.IsInstanceValid(part)) part.Position = position;
    }

    public int Lines
    {
        get
        {
            int lines = 1;
            foreach (var label in new[] { title, clan, status, seeking })
                if (label != null && GodotObject.IsInstanceValid(label)) lines++;
            return lines;
        }
    }

    public void SetNameText(string text)
    {
        if (name.Text == text) return;
        name.Text = text;
        Rescaled();
    }

    public void SetTitle(string text)
    {
        titleText = text;
        title = Set(title, Config.ShowTitles ? text : "", NamePlate.MakeTitle);
        Layout();
    }

    public void RefreshTitle() => SetTitle(titleText);

    public void SetClan(string text) => SetClan(text, NamePlate.ClanColor);

    public void SetClan(string text, Color colour)
    {
        clan = Set(clan, text, NamePlate.MakeClan);
        if (clan != null) NamePlate.Tint(clan, colour);
        Layout();
    }

    public void SetMark(Texture2D? texture)
    {
        if (texture == null)
        {
            if (mark != null && GodotObject.IsInstanceValid(mark)) mark.QueueFree();
            mark = null;
            PlaceLeader();
            return;
        }
        if (mark == null || !GodotObject.IsInstanceValid(mark))
        {
            mark = NamePlate.MakeMark(texture, name);
            mark.Visible = name.Visible;
            parent.AddChild(mark);
        }
        else mark.Texture = texture;
        NamePlate.PlaceMark(mark, name);
        PlaceLeader();
    }

    public void SetLeader(bool isLeader)
    {
        if (!isLeader)
        {
            if (leader != null && GodotObject.IsInstanceValid(leader)) leader.QueueFree();
            leader = null;
            return;
        }
        if (leader == null || !GodotObject.IsInstanceValid(leader))
        {
            if (UiIcons.Get(LeaderIcon) is not { } icon) return;
            leader = NamePlate.MakeMark(icon, name);
            leader.Modulate = NamePlate.LeaderColour;
            leader.Visible = name.Visible;
            parent.AddChild(leader);
        }
        PlaceLeader();
    }

    public void SetRank(Texture2D? texture)
    {
        if (texture == null)
        {
            if (rank != null && GodotObject.IsInstanceValid(rank)) rank.QueueFree();
            rank = null;
            PlaceLeader();
            return;
        }
        if (rank == null || !GodotObject.IsInstanceValid(rank))
        {
            rank = NamePlate.MakeMark(texture, name);
            rank.Visible = name.Visible;
            parent.AddChild(rank);
        }
        else rank.Texture = texture;
        PlaceLeader();
    }

    public void SetNameMark(LibreKO.Domain.NameMarkKind kind)
    {
        if (kind == nameMarkKind && (kind == LibreKO.Domain.NameMarkKind.None || nameMark != null)) return;
        nameMarkKind = kind;
        if (nameMark != null && GodotObject.IsInstanceValid(nameMark)) nameMark.QueueFree();
        nameMark = NamePlate.MakeNameMark(name, kind);
        if (nameMark == null) return;
        nameMark.Visible = name.Visible;
        parent.AddChild(nameMark);
    }

    public void Rescaled()
    {
        if (mark != null && GodotObject.IsInstanceValid(mark)) NamePlate.PlaceMark(mark, name);
        PlaceLeader();
        var kind = nameMarkKind;
        nameMarkKind = LibreKO.Domain.NameMarkKind.None;
        if (kind != LibreKO.Domain.NameMarkKind.None) SetNameMark(kind);
    }

    private void PlaceLeader()
    {
        int slot = mark != null && GodotObject.IsInstanceValid(mark) ? 1 : 0;
        if (rank != null && GodotObject.IsInstanceValid(rank)) NamePlate.PlaceMark(rank, name, slot++);
        if (leader == null || !GodotObject.IsInstanceValid(leader)) return;
        NamePlate.PlaceMark(leader, name, slot);
    }

    public void SetSeeking(string text, Color colour)
    {
        seeking = Set(seeking, text, NamePlate.MakeStatus);
        if (seeking != null) NamePlate.Tint(seeking, colour);
        Layout();
    }

    public void SetStatus(string text, Color colour)
    {
        status = Set(status, text, NamePlate.MakeStatus);
        if (status != null) status.Modulate = colour;
        Layout();
    }

    private bool? visible;

    public void SetVisible(bool show)
    {
        if (visible == show) return;
        visible = show;
        if (name.Visible != show) name.Visible = show;
        if (title != null && title.Visible != show) title.Visible = show;
        if (clan != null && clan.Visible != show) clan.Visible = show;
        if (status != null && status.Visible != show) status.Visible = show;
        if (seeking != null && seeking.Visible != show) seeking.Visible = show;
        if (mark != null && mark.Visible != show) mark.Visible = show;
        if (leader != null && leader.Visible != show) leader.Visible = show;
        if (rank != null && rank.Visible != show) rank.Visible = show;
        if (nameMark != null && nameMark.Visible != show) nameMark.Visible = show;
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
            label = make(text, name.Position.Y);
            label.Position = name.Position;
            label.Visible = name.Visible;
            parent.AddChild(label);
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
        foreach (var label in new[] { title, clan, status, seeking })
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
