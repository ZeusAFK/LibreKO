using Godot;

namespace LibreKO.Features.PluginBridge;

internal sealed class PluginBridgeFeature : IFeature, IPluginNotices
{
    private readonly IWorldContext context;
    private readonly SelfSession self;

    public PluginBridgeFeature(IWorldContext context, SelfSession self)
    {
        this.context = context;
        this.self = self;
    }

    void IPluginNotices.LogAdd(GameLogKind kind, string text, Color colour) => LogAdd(kind, text, colour);
    void IPluginNotices.Vendor() => NotifyVendor();
    void IPluginNotices.Warehouse() => NotifyWarehouse();
    void IPluginNotices.VipVault() => NotifyVipVault();
    void IPluginNotices.Trade() => NotifyTrade();
    void IPluginNotices.Skills() => NotifySkills();
    void IPluginNotices.Quests() => NotifyQuests();
    void IPluginNotices.Party() => NotifyParty();
    void IPluginNotices.Warp() => NotifyWarp();
    void IPluginNotices.Inventory() => NotifyInventory();
    void IPluginNotices.Genie() => NotifyGenie();
    void IPluginNotices.Hotbar() => NotifyHotbar();
    void IPluginNotices.HotbarReady(int[] ready) => NotifyHotbarReady(ready);
    void IPluginNotices.Drops(int targetId) => NotifyDrops(targetId);
    void IPluginNotices.Whispers() => NotifyWhispers();
    void IPluginNotices.WhisperLine(string name, RoomPost post) => NotifyWhisperLine(name, post);
    void IPluginNotices.WhisperCollapse(string name)
    {
        if (Adapters != null) PluginHost.Game.RaiseWhisperCollapse(name);
    }

    internal PluginAdapters? Adapters { get; private set; }

    internal void Connect(PluginAdapters adapters)
    {
        Adapters = adapters;
        adapters.AttachTo(PluginHost.Game);
    }

    internal void Disconnect()
    {
        Adapters?.DetachFrom(PluginHost.Game);
        Adapters = null;
    }

    internal void NotifyDrops(int targetId) => Adapters?.Target.RaiseDrops(targetId);

    private static readonly List<GameLogLine> keptLog = new();
    private static int keptLogOwner = -1;

    internal List<GameLogLine> Log => keptLog;
    private const int LogMax = 200;
    internal static readonly Color SystemLineColor = new("#9fd3ff");
    internal const string ClanWindowId = "clan";
    internal const string PresetWindowId = "presets";
    internal const string TitlesWindowId = "titles";

    internal static readonly Dictionary<string, string> MainWindowKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        ["character_info"] = "Character",
        ["inventory"] = "Inventory",
        ["skills"] = "Skills",
        ["quests"] = "Quests",
    };

    public void Build()
    {
        if (keptLogOwner != self.MyCharId)
        {
            keptLog.Clear();
            keptLogOwner = self.MyCharId;
        }
        if (PluginHost.Ui.ExtraHud.Count > 0)
        {
            var layer = new CanvasLayer { Layer = UiLayers.Hud };
            context.Root.AddChild(layer);
            foreach (var build in PluginHost.Ui.ExtraHud)
                layer.AddChild(build());
        }
        PluginHost.ThemeChanged += OnThemeChanged;
    }

    internal void NotifySkills()
    {
        if (Adapters != null) Callable.From(PluginHost.Game.RaiseSkills).CallDeferred();
    }

    internal void LogAdd(GameLogKind kind, string text, Color color)
    {
        var line = new GameLogLine(kind, text, color);
        Log.Add(line);
        if (Log.Count > LogMax) Log.RemoveAt(0);
        if (Adapters != null) PluginHost.Game.RaiseLogLine(line);
    }

    internal void NotifyQuests()
    {
        if (Adapters != null) Callable.From(PluginHost.Game.RaiseQuests).CallDeferred();
    }

    private void OnThemeChanged()
    {
        if (!context.Ready || context.Self.Body == null || !GodotObject.IsInstanceValid(context.Self.Body)) return;
        var at = context.Self.Body.GlobalPosition;
        var (mapX, mapZ) = Coord.ToMap(at.X, at.Z);
        Game.Connection.RebuildWorldInPlace(mapX, mapZ, at.Y);
    }

    public void Dispose()
    {
        PluginHost.ThemeChanged -= OnThemeChanged;
        Disconnect();
    }

    internal void NotifyInventory() => Adapters?.Inventory.RaiseInventory();

    internal void NotifyTarget() => Adapters?.Target.RefreshTarget();

    internal void NotifyHotbar() => Adapters?.Hotbar.RaiseHotbar();

    internal void NotifyHotbarReady(int[] ready)
    {
        if (Adapters != null) PluginHost.Game.RaiseHotbarReady(ready);
    }

    internal void NotifyMap(float headingDegrees) => Adapters?.Map.RaiseMap(headingDegrees);

    internal void NotifyGenie() => Callable.From(PluginHost.Game.RaiseGenie).CallDeferred();

    internal void NotifyVendor()
    {
        if (Adapters != null) Callable.From(PluginHost.Game.RaiseVendor).CallDeferred();
    }

    internal void NotifyWarehouse()
    {
        if (Adapters != null) Callable.From(PluginHost.Game.RaiseWarehouse).CallDeferred();
    }

    internal void NotifyVipVault()
    {
        if (Adapters != null) Callable.From(PluginHost.Game.RaiseVipVault).CallDeferred();
    }

    internal static GameItem PluginItem(int slot, ItemSlot it)
    {
        if (it.IsEmpty) return GameItem.Empty(slot);
        var def = ItemData.Get(it.ItemId);
        return new GameItem(slot, it.ItemId, ItemData.DisplayName(it.ItemId), ItemData.ShownCount(def, it),
            it.Durability, def?.UnbindStones ?? 0, def?.Kind ?? 0, ItemIcons.Get(it.ItemId),
            def != null && Inventory.IsTwoHandedSlotType(def.Slot), it.ExpiresAt, it.Flag);
    }

    internal void NotifyWhispers()
    {
        if (Adapters != null) Callable.From(PluginHost.Game.RaiseWhispers).CallDeferred();
    }

    internal void NotifyWhisperLine(string name, RoomPost post)
    {
        if (Adapters != null) PluginHost.Game.RaiseWhisperLine(name, WhisperLine(post));
    }

    internal GameWhisperLine WhisperLine(RoomPost post) => new(
        post.SenderId != 0 && post.SenderId == self.MyCharId,
        post.Kind == RoomPostKind.Notice,
        post.Kind == RoomPostKind.VoiceNote ? $"Voice note ({VoiceNoteRules.Clock(post.Seconds)})" : post.Text);

    internal void NotifyParty()
    {
        if (Adapters != null) Callable.From(PluginHost.Game.RaiseParty).CallDeferred();
    }

    internal void NotifyWarp()
    {
        if (Adapters != null) Callable.From(PluginHost.Game.RaiseWarp).CallDeferred();
    }

    internal void NotifyTrade()
    {
        if (Adapters != null) Callable.From(PluginHost.Game.RaiseTrade).CallDeferred();
    }

}
