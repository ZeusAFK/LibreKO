using Godot;
using LibreKO.Scene;

namespace LibreKO.Features.Windows;

internal sealed class NpcInteractionFeature : IFeature, ILateTickFeature, IInteractionDialogs
{
    private readonly IWorldContext context;
    private readonly WorldRoot world;
    private readonly NpcQuestSession quest;
    private readonly WarehouseSession warehouse;
    private readonly INpcQuestRequests questRequests;
    private readonly IWarpRequests warpRequests;
    private readonly SelectionFeature selection;
    private readonly MapInteractablesSystem mapInteractables;
    private readonly GateSystem gate;
    private readonly EntityActionSystem entityAction;

    public NpcInteractionFeature(IWorldContext context, WorldRoot world, NpcQuestSession npcQuest, WarehouseSession warehouse, INpcQuestRequests npcQuestRequests, IWarpRequests warpRequests, SelectionFeature selection, MapInteractablesSystem mapInteractables, GateSystem gate, EntityActionSystem entityAction)
    {
        this.context = context;
        this.world = world;
        quest = npcQuest;
        this.warehouse = warehouse;
        questRequests = npcQuestRequests;
        this.warpRequests = warpRequests;
        this.selection = selection;
        this.mapInteractables = mapInteractables;
        this.gate = gate;
        this.entityAction = entityAction;
    }

    private const float TalkPickRadius = 70f;

    private const int FamiliarShopGroup = 232000;
    private const float InteractRange = NpcQuestWire.InteractRange;
    private const double RangeInterval = 0.25;

    private double rangeAccum;

    internal void TrackTalker(int npcCharId)
    {
        quest.TalkTo(npcCharId);
        rangeAccum = 0;
    }

    public void LateTick(double delta, double nowSec) => RangeTick(delta);

    public void Build()
    {
        world.PlayerSystem.SelfMoving += CloseDialogs;
        selection.TalkRequested += TalkTo;
        quest.NpcDialogEvent += OnDialog;
        quest.NpcWindowEvent += OnWindow;
        warehouse.WarehouseNpcEvent += OnInnOpen;
    }

    public void Dispose()
    {
        world.PlayerSystem.SelfMoving -= CloseDialogs;
        selection.TalkRequested -= TalkTo;
        quest.NpcDialogEvent -= OnDialog;
        quest.NpcWindowEvent -= OnWindow;
        warehouse.WarehouseNpcEvent -= OnInnOpen;
    }

    private void OnInnOpen()
    {
        world.NpcDialogWindow.ShowServiceChoice(
            "Shall we start the day at our inn and end it at our inn as well?",
            ("Use storage", world.Warehouse.OpenStorage),
            ("Use [VIP] storage", world.VipWarehouse.Toggle),
            ("Create a Clan", world.ClanCreate.CreateClanFromInn),
            ("Seal / Cancel (anti-theft)", () => world.Seal.Open(SealFeature.Mode.Secret)),
            ("Seal / Cancel", () => world.Seal.Open(SealFeature.Mode.Bind)),
            ("Character Seal/Unseal", world.CharacterSealWindow.Open));
    }

    internal bool TryInteractAt(Vector2 mouse)
    {
        if (world.MerchantStalls.TryOpenStallAt(mouse)) return true;
        if (world.PlayerMenu.TryOpen(mouse)) return true;

        var best = selection.PickEntityAt(mouse, TalkPickRadius, out int bestId, out _);
        if (best == null) return mapInteractables.TryOpenWarpGate(mouse) || mapInteractables.TryOpenAnvil(mouse);
        if (best.Attackable) return false;

        selection.Select(bestId, best);
        if (!best.Attackable) TalkTo(bestId);
        return true;
    }

    internal bool HasNearby()
    {
        if (!context.Ready || context.Self.Body == null || context.Self.Dead) return false;
        foreach (var (_, e) in context.Entities.Views)
        {
            if (!e.IsNpc || e.Dead || e.Attackable) continue;
            if (Coord.FlatDistance(context.Self.Body.Position, e.Body.Position) <= InteractRange)
                return true;
        }
        return false;
    }

    public bool TalkToNearest(string nameFragment)
    {
        if (!context.Ready || context.Self.Body == null) return false;
        int bestId = -1;
        float bestDist = float.MaxValue;
        foreach (var (id, e) in context.Entities.Views)
        {
            if (!e.IsNpc || e.Dead || e.Attackable) continue;
            if (nameFragment.Length > 0
                && !e.Name.Contains(nameFragment, System.StringComparison.OrdinalIgnoreCase)) continue;
            float d = Coord.FlatDistance(context.Self.Body.Position, e.Body.Position);
            if (d > InteractRange) continue;
            if (d < bestDist) { bestDist = d; bestId = id; }
        }
        if (bestId < 0) return false;
        TalkTo(bestId);
        return true;
    }

    internal void TalkTo(int npcUniqueId)
    {
        if (context.Self.Dead) return;
        if (!context.Entities.Views.TryGetValue(npcUniqueId, out var e) || !e.IsNpc || e.Dead || e.Attackable) return;
        if (world.RankerMessageWindow.TryOpenStatue(EntityRig.EntityInfoOf(npcUniqueId, e))) return;
        if (gate.TryOperateKeyGate(e, npcUniqueId)) return;

        if (Coord.FlatDistance(context.Self.Body.Position, e.Body.Position) > InteractRange) return;

        if (e.NpcType == NpcTypes.Lever)
        {
            gate.TryOperateObject((short)e.NpcId, npcUniqueId);
            return;
        }

        StopForTalk(e);
        if (TurnsToTalker(e) && entityAction.YawToward(e.Body.Position, context.Self.Body.Position) is { } towardTalker)
            EntityRig.FaceEntity(e, towardTalker, immediate: false);

        TrackTalker(npcUniqueId);
        quest.RememberNpc(npcUniqueId, e.Name.Length > 0
            ? e.Name
            : NpcNames.Name(e.NpcId, false));

        questRequests.NpcEvent(npcUniqueId);
        warpRequests.ListRequest(e.NpcId);
    }

    private static bool TurnsToTalker(EntityView e) =>
        e.Anim != null && !e.Statue && e.ObjectType == 0 && !EntityRig.EntityMoving(e);

    private void StopForTalk(EntityView e)
    {
        context.Movement.StopForInteraction();

        Vector3 toNpc = e.Body.Position - context.Self.Body.Position;
        toNpc.Y = 0;
        if (toNpc.LengthSquared() <= 0.0004f) return;
        context.Movement.Face(toNpc.Normalized());
    }

    internal void RangeTick(double delta)
    {
        if (quest.TalkingNpcId == NpcQuestSession.NoTalker) return;

        if (!AnyDialogOpen) { quest.EndTalk(); return; }

        rangeAccum += delta;
        if (rangeAccum < RangeInterval) return;
        rangeAccum = 0;

        bool inRange = context.Entities.Views.TryGetValue(quest.TalkingNpcId, out var e)
                       && !e.Dead
                       && Coord.FlatDistance(context.Self.Body.Position, e.Body.Position) <= InteractRange;
        if (inRange) return;

        CloseDialogs();
        quest.EndTalk();
    }

    internal void OnDialog(NpcDialog dlg)
    {
        if (dlg.Flag == NpcDialogStyle.RebirthPanel)
        {
            world.NpcDialogWindow.Close();
            world.Rebirth.OpenPicker();
            return;
        }

        if (dlg.Flag == NpcDialogStyle.RebirthResetPanel)
        {
            world.NpcDialogWindow.Close();
            world.Rebirth.OpenReset();
            return;
        }

        if (dlg.Flag == NpcDialogStyle.RepurchaseWindow)
        {
            world.NpcDialogWindow.Close();
            world.Repurchase.Open(dlg.NpcId);
            return;
        }

        if (dlg.Flag == NpcDialogStyle.FamiliarPanel)
        {
            world.NpcDialogWindow.Close();
            world.PetHatch.Open(dlg.NpcId, dlg.HeaderTextId == PetWire.RenameMenuText);
            return;
        }

        if (dlg.Flag == NpcDialogStyle.GenderChangePanel)
        {
            world.NpcDialogWindow.Close();
            world.GenderChangeWindow.Open();
            return;
        }

        if (dlg.Flag == NpcDialogStyle.YutGame)
        {
            world.NpcDialogWindow.Close();
            world.Yut.Bet();
            return;
        }

        if (dlg.Flag == NpcDialogStyle.DrakiRankBoard)
        {
            world.NpcDialogWindow.Close();
            world.DrakiBoard.Request();
            return;
        }

        if (dlg.Flag == NpcDialogStyle.FortunePanel)
        {
            world.NpcDialogWindow.Close();
            world.Fortune.Open();
            return;
        }

        if (dlg.Flag is NpcDialogStyle.SmallJar or NpcDialogStyle.LargeJar or NpcDialogStyle.RebirthJar)
        {
            world.NpcDialogWindow.Close();
            world.ExpSealWindow.UseJar(dlg.Flag);
            return;
        }

        if (dlg.Flag == NpcDialogStyle.FullMoonBoard)
        {
            world.NpcDialogWindow.Close();
            world.SeekParty.OpenFullMoonBoard();
            return;
        }

        if (dlg.Flag == NpcDialogStyle.SpecialAuctionPanel)
        {
            world.NpcDialogWindow.Close();
            world.SpecialAuctionWindow.Open();
            return;
        }

        if (dlg.Flag == NpcDialogStyle.ItemCombinePanel)
        {
            world.NpcDialogWindow.Close();
            world.ItemCombineWindow.Open(dlg.NpcId);
            return;
        }

        if (dlg.Flag == NpcDialogStyle.CombineRecipeBook)
        {
            world.NpcDialogWindow.Close();
            world.ItemCombineWindow.OpenCombineRecipeBook();
            return;
        }

        if (dlg.Flag == NpcDialogStyle.DisassemblePanel)
        {
            int talkedTo = quest.TalkingNpcId;
            world.NpcDialogWindow.Close();
            world.ItemSmashWindow.Open(dlg.NpcId, talkedTo);
            return;
        }

        if (dlg.Flag == NpcDialogStyle.FamiliarShop)
        {
            world.Vendor.Open(FamiliarShopGroup);
            return;
        }

        world.NpcDialogWindow.ShowPage(dlg);
    }

    private void OnWindow(GameOpcodes op)
    {
        context.Log.Notice("This NPC's service isn't available yet.");
    }

    private readonly List<IClosableWindow> dialogs = new();

    void IInteractionDialogs.InteractionCloses(IClosableWindow dialog) => InteractionCloses(dialog);

    internal void InteractionCloses(IClosableWindow dialog) => dialogs.Add(dialog);

    internal bool AnyDialogOpen => dialogs.Exists(dialog => dialog.IsOpen);

    internal void CloseDialogs()
    {
        bool closed = false;
        foreach (var dialog in dialogs)
            if (dialog.IsOpen) { dialog.Close(); closed = true; }
        if (closed) quest.EndTalk();
    }
}
