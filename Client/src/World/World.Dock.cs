using System.Collections.Generic;
using System.Linq;
using Godot;
using LibreKO.Domain;

namespace LibreKO;

public partial class World
{
    private const string EdgeDockMigration = "edge_dock";
    private const string CentreOnOpenMigration = "centre_on_open";
    private const double HudSettleSeconds = 0.5;
    private static readonly string[] LeftDocked = { "Character" };
    private static readonly string[] RightDocked = { "Inventory", "Skills", "Party" };

    private readonly List<string> _dockOrder = new();
    private bool _dockQueued;

    private void DockInit()
    {
        var docked = LeftDocked.Concat(RightDocked)
            .Where(key => _mainWindows.ContainsKey(key))
            .ToArray();
        Config.ForgetWindowPositionsOnce(EdgeDockMigration, docked.Select(key => _mainWindows[key].Id));

        foreach (string key in docked)
        {
            var window = _mainWindows[key];
            string dockKey = key;
            window.DockTo(() => DockSpot(dockKey));
            window.SetDocked(!Config.HasWindowPos(window.Id));
            window.VisibilityChanged += () => OnDockedVisibility(dockKey, window);
            window.Resized += QueueDockLayout;
            window.Layout.Placed += () =>
            {
                window.SetDocked(false);
                QueueDockLayout();
            };
            if (window.Visible) _dockOrder.Add(key);
        }
        GetViewport().SizeChanged += QueueDockLayout;
        GetTree().CreateTimer(HudSettleSeconds).Timeout += () => { if (IsInstanceValid(this)) QueueDockLayout(); };
    }

    private void CentredWindowsInit() =>
        Config.ForgetWindowPositionsOnce(CentreOnOpenMigration, HudWindow.CentredIds);

    private void Redock(string key)
    {
        if (!_mainWindows.TryGetValue(key, out var window) || !Config.HasWindowPos(window.Id)) return;
        Config.ForgetWindowPos(window.Id);
        window.SetDocked(true);
        Audio.PlayUi(Sfx.InventoryOpen);
        QueueDockLayout();
    }

    private void DockDispose()
    {
        if (IsInsideTree()) GetViewport().SizeChanged -= QueueDockLayout;
    }

    private void OnDockedVisibility(string key, HudWindow window)
    {
        _dockOrder.Remove(key);
        if (window.Visible) _dockOrder.Add(key);
        QueueDockLayout();
    }

    private void QueueDockLayout()
    {
        if (_dockQueued) return;
        _dockQueued = true;
        Callable.From(RunDockLayout).CallDeferred();
    }

    private void RunDockLayout()
    {
        _dockQueued = false;
        foreach (string key in _dockOrder)
            if (_mainWindows.TryGetValue(key, out var window) && FollowsDock(window))
                window.Layout.ReapplyDefault();
    }

    private static bool FollowsDock(HudWindow window) =>
        GodotObject.IsInstanceValid(window) && window.Visible && !Config.HasWindowPos(window.Id);

    private Vector2 DockSpot(string key)
    {
        bool left = LeftDocked.Contains(key);
        var stack = new List<string>();
        foreach (string k in _dockOrder)
        {
            if (LeftDocked.Contains(k) != left) continue;
            if (k == key) break;
            if (_mainWindows.TryGetValue(k, out var earlier) && FollowsDock(earlier)) stack.Add(k);
        }
        stack.Add(key);

        var sizes = stack.Select(k => Footprint(_mainWindows[k])).ToArray();
        var frame = new EdgeDock.Frame(
            GetViewport().GetVisibleRect().Size,
            Platform.TouchUi ? HudPlacement.TouchEdge : HudAnchor.Edge,
            HudPlacement.BottomInset);
        var spots = EdgeDock.Place(left ? EdgeDock.Side.Left : EdgeDock.Side.Right, sizes, DockHudRects(), frame);
        return spots[^1];
    }

    private static Vector2 Footprint(Control window)
    {
        Vector2 min = window.GetCombinedMinimumSize();
        return new Vector2(Mathf.Max(window.Size.X, min.X), Mathf.Max(window.Size.Y, min.Y)) * window.Scale;
    }

    private List<Rect2> DockHudRects()
    {
        var rects = new List<Rect2>();
        foreach (var hud in new Control?[]
                 {
                     _miniMap, _clockLabel, _hudLauncherRoot, _townButton, _trophy, _attendanceGift,
                     _mailIconButton, _powerUpStoreIcon, _lotteryIcon, _statusHud, _hotbarBox, _buffPanel,
                     _premiumChip, _combatLogFrame, Chat?.Panel,
                 })
            if (hud != null && GodotObject.IsInstanceValid(hud) && hud.IsVisibleInTree())
                rects.Add(hud.GetGlobalRect());
        return rects;
    }
}
