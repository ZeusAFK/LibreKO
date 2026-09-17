using System.Collections.Generic;
using Godot;
using LibreKO.Network;

namespace LibreKO;

public partial class World
{
    private CanvasLayer _instanceLayer = null!;
    private HudWindow _instancePanel = null!;
    private VBoxContainer _instanceList = null!;
    private Label _instanceStatus = null!;
    private bool _instanceShown;
    private int _instanceCurrent;
    private byte _instanceMyLevel;

    private void InstanceInit()
    {
        _instanceLayer = new CanvasLayer { Layer = 74 };
        AddChild(_instanceLayer);
        _instancePanel = new HudWindow("instance", "Instance Dungeons", new Vector2(200, 130)) { Visible = false };
        _instancePanel.Closed += CloseInstance;
        _instanceLayer.AddChild(_instancePanel);
        var root = _instancePanel.Body;
        root.AddThemeConstantOverride("separation", 6);
        root.AddChild(UiTheme.SectionTitle("Instance Dungeons"));

        _instanceStatus = UiTheme.Text("Not inside an instance.", 12, UiTheme.TextLo);
        root.AddChild(_instanceStatus);

        var scroll = new ScrollContainer { CustomMinimumSize = new Vector2(360, 320), HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        root.AddChild(scroll);
        _instanceList = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _instanceList.AddThemeConstantOverride("separation", 3);
        scroll.AddChild(_instanceList);

        Net.I.InstanceListEvent += OnInstanceList;
        Net.I.InstanceEnterEvent += OnInstanceEnter;
        Net.I.InstanceLeaveEvent += OnInstanceLeave;
    }

    private void InstanceDispose()
    {
        Net.I.InstanceListEvent -= OnInstanceList;
        Net.I.InstanceEnterEvent -= OnInstanceEnter;
        Net.I.InstanceLeaveEvent -= OnInstanceLeave;
    }

    private void ToggleInstance()
    {
        if (_instanceShown) { CloseInstance(); return; }
        _instancePanel.Visible = true;
        _instanceShown = true;
        _instanceMyLevel = (byte)Sheet.Level;
        Net.I.SendInstanceList();
    }

    private void CloseInstance()
    {
        if (!_instanceShown) return;
        _instanceShown = false;
        _instancePanel.Visible = false;
    }

    private void OnInstanceList(List<InstanceEntry> list)
    {
        foreach (var c in _instanceList.GetChildren()) c.QueueFree();
        foreach (var inst in list)
        {
            var row = new PanelContainer();
            row.AddThemeStyleboxOverride("panel", UiTheme.Row());
            var hb = new HBoxContainer(); hb.AddThemeConstantOverride("separation", 8);
            row.AddChild(hb);

            var vb = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
            vb.AddThemeConstantOverride("separation", 1);
            bool eligible = _instanceMyLevel >= inst.MinLevel;
            vb.AddChild(UiTheme.Text(inst.Name, 13, eligible ? UiTheme.TextHi : UiTheme.TextLo));
            vb.AddChild(UiTheme.Text($"Lv {inst.MinLevel}+   Party {inst.PartySize}", 11, UiTheme.TextLo));
            hb.AddChild(vb);

            if (_instanceCurrent == inst.Id)
            {
                hb.AddChild(UiTheme.Text("Inside", 12, UiTheme.Gold));
            }
            else if (_instanceCurrent != 0)
            {
                hb.AddChild(UiTheme.Text("Busy", 12, UiTheme.TextLo));
            }
            else if (eligible)
            {
                int id = inst.Id;
                var btn = new Button { Text = "Enter", FocusMode = Control.FocusModeEnum.None };
                btn.Pressed += () => Net.I.SendInstanceEnter(id);
                hb.AddChild(btn);
            }
            else
            {
                hb.AddChild(UiTheme.Text("Locked", 12, UiTheme.TextLo));
            }
            _instanceList.AddChild(row);
        }

        if (_instanceCurrent != 0)
        {
            var leave = new Button { Text = "Leave Instance", FocusMode = Control.FocusModeEnum.None };
            leave.Pressed += () => Net.I.SendInstanceLeave();
            _instanceList.AddChild(leave);
        }

        if (_instanceList.GetChildCount() == 0)
        {
            var e = HudStyle.Label(13); e.Text = "No instances available.";
            _instanceList.AddChild(e);
        }

        _instanceStatus.Text = _instanceCurrent != 0
            ? $"Inside instance #{_instanceCurrent}."
            : "Not inside an instance.";
    }

    private void OnInstanceEnter(int instanceId, bool ok)
    {
        if (ok) _instanceCurrent = instanceId;
        if (ok) Net.I.SendInstanceList();
    }

    private void OnInstanceLeave(int instanceId, bool ok)
    {
        if (ok) _instanceCurrent = 0;
        if (ok) Net.I.SendInstanceList();
    }
}
