using Godot;

namespace LibreKO.Features.Windows;

internal sealed class BountyFeature : IFeature, IClosableWindow
{
    private readonly IWorldContext context;
    private readonly BountySession bounty;
    private readonly IBountyRequests requests;

    public BountyFeature(IWorldContext context, BountySession bounty, IBountyRequests requests)
    {
        this.context = context;
        this.bounty = bounty;
        this.requests = requests;
    }

    private CanvasLayer layer = null!;
    private HudWindow panel = null!;
    private VBoxContainer listBox = null!;
    private LineEdit targetEdit = null!;
    private LineEdit rewardEdit = null!;
    private bool shown;

    public bool IsOpen => shown;

    public void Build()
    {
        layer = new CanvasLayer { Layer = UiLayers.Windows };
        context.Root.AddChild(layer);
        panel = new HudWindow("bounty", "Bounty Board") { Visible = false };
        panel.Closed += Close;
        layer.AddChild(panel);
        var root = panel.Body;
        root.AddThemeConstantOverride("separation", 6);

        root.AddChild(UiTheme.SectionTitle("Post a Bounty"));
        var form = new HBoxContainer();
        form.AddThemeConstantOverride("separation", 6);
        root.AddChild(form);

        form.AddChild(UiTheme.Text("Target", 12, UiTheme.TextLo));
        targetEdit = new LineEdit { CustomMinimumSize = new Vector2(120, 0), PlaceholderText = "name" };
        form.AddChild(targetEdit);

        form.AddChild(UiTheme.Text("Reward", 12, UiTheme.TextLo));
        rewardEdit = new LineEdit { CustomMinimumSize = new Vector2(90, 0), PlaceholderText = "gold" };
        form.AddChild(rewardEdit);

        var postBtn = new Button { Text = "Post", FocusMode = Control.FocusModeEnum.None };
        postBtn.Pressed += OnPostPressed;
        form.AddChild(postBtn);

        root.AddChild(UiTheme.SectionTitle("Open Bounties"));
        var scroll = new ScrollContainer { CustomMinimumSize = new Vector2(380, 300), HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        root.AddChild(scroll);
        listBox = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        listBox.AddThemeConstantOverride("separation", 3);
        scroll.AddChild(listBox);

        bounty.BountyListEvent += OnList;
        bounty.BountyPostEvent += OnPost;
        bounty.BountyClaimEvent += OnClaim;
    }

    public void Dispose()
    {
        bounty.BountyListEvent -= OnList;
        bounty.BountyPostEvent -= OnPost;
        bounty.BountyClaimEvent -= OnClaim;
    }

    internal void Toggle()
    {
        if (shown) { Close(); return; }
        panel.Visible = true;
        shown = true;
        requests.List();
    }

    public void Close()
    {
        if (!shown) return;
        shown = false;
        panel.Visible = false;
    }

    private void OnPostPressed()
    {
        string target = targetEdit.Text.Trim();
        if (target.Length == 0) return;
        if (!int.TryParse(rewardEdit.Text.Trim(), out int reward) || reward <= 0) return;
        requests.Post(target, reward);
    }

    private void OnList(List<BountyEntry> list)
    {
        foreach (var c in listBox.GetChildren()) c.QueueFree();
        foreach (var b in list)
        {
            var row = new PanelContainer();
            row.AddThemeStyleboxOverride("panel", UiTheme.Row());
            var hb = new HBoxContainer(); hb.AddThemeConstantOverride("separation", 8);
            row.AddChild(hb);

            var target = UiTheme.Text(b.Target, 13, UiTheme.TextHi);
            target.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            hb.AddChild(target);

            hb.AddChild(UiTheme.Text($"by {b.Poster}", 12, UiTheme.TextLo));
            hb.AddChild(UiTheme.Text($"{b.Reward:N0}g", 12, UiTheme.Gold));

            int id = b.Id;
            var btn = new Button { Text = "Claim", FocusMode = Control.FocusModeEnum.None };
            btn.Pressed += () => requests.Claim(id);
            hb.AddChild(btn);

            listBox.AddChild(row);
        }
        if (listBox.GetChildCount() == 0)
        {
            var e = HudStyle.Label(13); e.Text = "No open bounties.";
            listBox.AddChild(e);
        }
    }

    private void OnPost(bool ok)
    {
        if (ok)
        {
            targetEdit.Text = "";
            rewardEdit.Text = "";
        }
    }

    private void OnClaim(int bountyId, bool ok)
    {
        if (ok) requests.List();
    }
}
