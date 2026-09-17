using System.Collections.Generic;
using Godot;
using LibreKO.Network;

namespace LibreKO;

public partial class World
{
    private CanvasLayer _disguiseLayer = null!;
    private HudWindow _disguisePanel = null!;
    private VBoxContainer _disguiseList = null!;
    private bool _disguiseShown;
    private int _disguiseCurrent;

    private void DisguiseInit()
    {
        _disguiseLayer = new CanvasLayer { Layer = 74 };
        AddChild(_disguiseLayer);
        _disguisePanel = new HudWindow("disguise", "Disguise", new Vector2(190, 130)) { Visible = false };
        _disguisePanel.Closed += CloseDisguise;
        _disguiseLayer.AddChild(_disguisePanel);
        var root = _disguisePanel.Body;
        root.AddThemeConstantOverride("separation", 6);
        root.AddChild(UiTheme.SectionTitle("Disguise"));
        var scroll = new ScrollContainer { CustomMinimumSize = new Vector2(340, 320), HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        root.AddChild(scroll);
        _disguiseList = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _disguiseList.AddThemeConstantOverride("separation", 3);
        scroll.AddChild(_disguiseList);

        Net.I.DisguiseListEvent += OnDisguiseList;
        Net.I.DisguiseApplyEvent += OnDisguiseApply;
        Net.I.DisguiseRemoveEvent += OnDisguiseRemove;
    }

    private void DisguiseDispose()
    {
        Net.I.DisguiseListEvent -= OnDisguiseList;
        Net.I.DisguiseApplyEvent -= OnDisguiseApply;
        Net.I.DisguiseRemoveEvent -= OnDisguiseRemove;
    }

    private void ToggleDisguise()
    {
        if (_disguiseShown) { CloseDisguise(); return; }
        _disguisePanel.Visible = true;
        _disguiseShown = true;
        Net.I.SendDisguiseList();
    }

    private void CloseDisguise()
    {
        if (!_disguiseShown) return;
        _disguiseShown = false;
        _disguisePanel.Visible = false;
    }

    private void OnDisguiseList(List<DisguiseEntry> list)
    {
        foreach (var c in _disguiseList.GetChildren()) c.QueueFree();
        foreach (var d in list)
        {
            bool active = d.Id == _disguiseCurrent && _disguiseCurrent != 0;
            var row = new PanelContainer();
            row.AddThemeStyleboxOverride("panel", UiTheme.Row());
            var hb = new HBoxContainer(); hb.AddThemeConstantOverride("separation", 8);
            row.AddChild(hb);
            var name = UiTheme.Text(d.Name, 13, active ? UiTheme.Gold : UiTheme.TextHi);
            name.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            hb.AddChild(name);
            if (active)
            {
                var rbtn = new Button { Text = "Remove", FocusMode = Control.FocusModeEnum.None };
                rbtn.Pressed += () => Net.I.SendDisguiseRemove();
                hb.AddChild(rbtn);
            }
            else
            {
                int id = d.Id;
                var btn = new Button { Text = "Apply", FocusMode = Control.FocusModeEnum.None };
                btn.Pressed += () => Net.I.SendDisguiseApply(id);
                hb.AddChild(btn);
            }
            _disguiseList.AddChild(row);
        }
        if (_disguiseList.GetChildCount() == 0)
        {
            var e = HudStyle.Label(13); e.Text = "No disguises available.";
            _disguiseList.AddChild(e);
        }
    }

    private void OnDisguiseApply(int disguiseId, bool ok)
    {
        if (ok)
        {
            _disguiseCurrent = disguiseId;
            Chat.Info("Disguise applied. (model swap deferred)");
            Net.I.SendDisguiseList();
        }
    }

    private void OnDisguiseRemove(int disguiseId, bool ok)
    {
        if (ok)
        {
            _disguiseCurrent = 0;
            Chat.Info("Disguise removed.");
            Net.I.SendDisguiseList();
        }
    }
}
