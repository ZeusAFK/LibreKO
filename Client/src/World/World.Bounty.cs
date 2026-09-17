using System.Collections.Generic;
using Godot;
using LibreKO.Network;

namespace LibreKO;

public partial class World
{
    private CanvasLayer _bountyLayer = null!;
    private HudWindow _bountyPanel = null!;
    private VBoxContainer _bountyList = null!;
    private LineEdit _bountyTargetEdit = null!;
    private LineEdit _bountyRewardEdit = null!;
    private bool _bountyShown;

    private void BountyInit()
    {
        _bountyLayer = new CanvasLayer { Layer = 74 };
        AddChild(_bountyLayer);
        _bountyPanel = new HudWindow("bounty", "Bounty Board", new Vector2(200, 120)) { Visible = false };
        _bountyPanel.Closed += CloseBounty;
        _bountyLayer.AddChild(_bountyPanel);
        var root = _bountyPanel.Body;
        root.AddThemeConstantOverride("separation", 6);

        root.AddChild(UiTheme.SectionTitle("Post a Bounty"));
        var form = new HBoxContainer();
        form.AddThemeConstantOverride("separation", 6);
        root.AddChild(form);

        form.AddChild(UiTheme.Text("Target", 12, UiTheme.TextLo));
        _bountyTargetEdit = new LineEdit { CustomMinimumSize = new Vector2(120, 0), PlaceholderText = "name" };
        form.AddChild(_bountyTargetEdit);

        form.AddChild(UiTheme.Text("Reward", 12, UiTheme.TextLo));
        _bountyRewardEdit = new LineEdit { CustomMinimumSize = new Vector2(90, 0), PlaceholderText = "gold" };
        form.AddChild(_bountyRewardEdit);

        var postBtn = new Button { Text = "Post", FocusMode = Control.FocusModeEnum.None };
        postBtn.Pressed += OnBountyPostPressed;
        form.AddChild(postBtn);

        root.AddChild(UiTheme.SectionTitle("Open Bounties"));
        var scroll = new ScrollContainer { CustomMinimumSize = new Vector2(380, 300), HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        root.AddChild(scroll);
        _bountyList = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _bountyList.AddThemeConstantOverride("separation", 3);
        scroll.AddChild(_bountyList);

        Net.I.BountyListEvent += OnBountyList;
        Net.I.BountyPostEvent += OnBountyPost;
        Net.I.BountyClaimEvent += OnBountyClaim;
    }

    private void BountyDispose()
    {
        Net.I.BountyListEvent -= OnBountyList;
        Net.I.BountyPostEvent -= OnBountyPost;
        Net.I.BountyClaimEvent -= OnBountyClaim;
    }

    private void ToggleBounty()
    {
        if (_bountyShown) { CloseBounty(); return; }
        _bountyPanel.Visible = true;
        _bountyShown = true;
        Net.I.SendBountyList();
    }

    private void CloseBounty()
    {
        if (!_bountyShown) return;
        _bountyShown = false;
        _bountyPanel.Visible = false;
    }

    private void OnBountyPostPressed()
    {
        string target = _bountyTargetEdit.Text.Trim();
        if (target.Length == 0) return;
        if (!int.TryParse(_bountyRewardEdit.Text.Trim(), out int reward) || reward <= 0) return;
        Net.I.SendBountyPost(target, reward);
    }

    private void OnBountyList(List<BountyEntry> list)
    {
        foreach (var c in _bountyList.GetChildren()) c.QueueFree();
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
            btn.Pressed += () => Net.I.SendBountyClaim(id);
            hb.AddChild(btn);

            _bountyList.AddChild(row);
        }
        if (_bountyList.GetChildCount() == 0)
        {
            var e = HudStyle.Label(13); e.Text = "No open bounties.";
            _bountyList.AddChild(e);
        }
    }

    private void OnBountyPost(bool ok)
    {
        if (ok)
        {
            _bountyTargetEdit.Text = "";
            _bountyRewardEdit.Text = "";
        }
    }

    private void OnBountyClaim(int bountyId, bool ok)
    {
        if (ok) Net.I.SendBountyList();
    }
}
