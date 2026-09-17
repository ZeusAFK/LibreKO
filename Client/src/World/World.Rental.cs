using System.Collections.Generic;
using Godot;
using LibreKO.Domain;
using LibreKO.Network;

namespace LibreKO;

public partial class World
{
    private CanvasLayer _rentLayer = null!;
    private HudWindow _rentPanel = null!;
    private VBoxContainer _rentList = null!;
    private Label _rentStatus = null!;
    private bool _rentShown;

    private void RentalInit()
    {
        _rentLayer = new CanvasLayer { Layer = 73 };
        AddChild(_rentLayer);
        _rentPanel = new HudWindow("rental", "Rentals", new Vector2(160, 120)) { Visible = false };
        _rentPanel.Closed += CloseRental;
        _rentLayer.AddChild(_rentPanel);
        var root = _rentPanel.Body;
        root.AddThemeConstantOverride("separation", 6);
        root.AddChild(UiTheme.SectionTitle("Rentals"));
        var scroll = new ScrollContainer { CustomMinimumSize = new Vector2(340, 200), HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        root.AddChild(scroll);
        _rentList = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _rentList.AddThemeConstantOverride("separation", 3);
        scroll.AddChild(_rentList);
        _rentStatus = HudStyle.Label(13);
        root.AddChild(_rentStatus);

        Net.I.RentalListEvent += OnRentalList;
        Net.I.RentalRentEvent += OnRentalRent;
        Net.I.RentalOpenEvent += OnRentalOpen;
    }

    private void RentalDispose()
    {
        Net.I.RentalListEvent -= OnRentalList;
        Net.I.RentalRentEvent -= OnRentalRent;
        Net.I.RentalOpenEvent -= OnRentalOpen;
    }

    private void OnRentalOpen() { if (!_rentShown) ToggleRental(); }

    private void ToggleRental()
    {
        if (_rentShown) { CloseRental(); return; }
        _rentPanel.Visible = true;
        _rentShown = true;
        _rentStatus.Text = "";
        Net.I.SendRentalList();
    }

    private void CloseRental()
    {
        if (!_rentShown) return;
        _rentShown = false;
        _rentPanel.Visible = false;
    }

    private void OnRentalList(List<RentalItem> list)
    {
        foreach (var c in _rentList.GetChildren()) c.QueueFree();
        if (list.Count == 0)
        {
            var e = HudStyle.Label(13); e.Text = "Nothing to rent.";
            _rentList.AddChild(e);
            return;
        }
        foreach (var it in list)
        {
            int itemId = it.ItemId;
            var row = new PanelContainer();
            row.AddThemeStyleboxOverride("panel", UiTheme.Row());
            var hb = new HBoxContainer(); hb.AddThemeConstantOverride("separation", 8);
            row.AddChild(hb);
            var info = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
            info.AddThemeConstantOverride("separation", -2);
            info.AddChild(UiTheme.Text(ItemData.DisplayName(itemId), 13, UiTheme.TextHi));
            info.AddChild(UiTheme.Text($"{it.Days} days  ·  {it.Cost:n0} gold", 11, UiTheme.TextLo));
            hb.AddChild(info);
            var btn = new Button { Text = "Rent", FocusMode = Control.FocusModeEnum.None };
            btn.Pressed += () => Net.I.SendRentalRent(itemId);
            hb.AddChild(btn);
            _rentList.AddChild(row);
        }
    }

    private void OnRentalRent(int itemId, bool ok)
    {
        _rentStatus.Text = ok ? $"Rented {ItemData.DisplayName(itemId)}." : "Couldn't rent that (not enough gold?).";
    }
}
