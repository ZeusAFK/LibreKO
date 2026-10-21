using Godot;
using LibreKO.Features.Core;
using LibreKO.Scene;

namespace LibreKO.Features.Social;

internal sealed partial class PlayerMenuFeature : IFeature
{
    private readonly IWorldContext context;
    private readonly WorldRoot world;
    private readonly ClanSession clan;
    private readonly HostilityRules hostility;
    private readonly PartySession party;
    private readonly IClanRequests clanRequests;
    private readonly IClanBattleWarRequests clanBattleWarRequests;
    private readonly IPartyRequests partyRequests;

    public PlayerMenuFeature(IWorldContext context, WorldRoot world, ClanSession clan, HostilityRules hostility, PartySession party, IClanRequests clanRequests, IClanBattleWarRequests clanBattleWarRequests, IPartyRequests partyRequests)
    {
        this.context = context;
        this.world = world;
        this.clan = clan;
        this.hostility = hostility;
        this.party = party;
        this.clanRequests = clanRequests;
        this.clanBattleWarRequests = clanBattleWarRequests;
        this.partyRequests = partyRequests;
    }

    private enum PlayerMenuAction
    {
        RequestParty = 1,
        Report,
        UserTrade,
        Whisper,
        AddFriend,
        UserInformation,
        Duel,
        EquipmentView,
        ClanInvite,
        CopyName,
        ClanBattle,
        ClanSurrender,
    }

    private static readonly System.Collections.Generic.Dictionary<PlayerMenuAction, (string Key, string Caption)> PlayerMenuRows = new()
    {
        [PlayerMenuAction.RequestParty] = ("party", "Request a party"),
        [PlayerMenuAction.Report] = ("report", "Report"),
        [PlayerMenuAction.UserTrade] = ("trade", "User trade"),
        [PlayerMenuAction.Whisper] = ("whisper", "Whisper"),
        [PlayerMenuAction.AddFriend] = ("friend", "Add friend"),
        [PlayerMenuAction.UserInformation] = ("info", "User Information"),
        [PlayerMenuAction.Duel] = ("duel", "Duel"),
        [PlayerMenuAction.EquipmentView] = ("equipment", "Equipment View"),
        [PlayerMenuAction.ClanInvite] = ("clan_invite", "Clan invite"),
        [PlayerMenuAction.CopyName] = ("copy_name", "Copy name"),
        [PlayerMenuAction.ClanBattle] = ("clan_battle", "Clan battle"),
        [PlayerMenuAction.ClanSurrender] = ("clan_surrender", "Surrender the clan battle"),
    };

    private PopupMenu playerMenu = null!;

    private int id = -1;

    private string name = "";
    private CanvasLayer? skinLayer;

    public void Build()
    {
        playerMenu = new PopupMenu();
        playerMenu.IdPressed += OnAction;
        context.Root.AddChild(playerMenu);
    }

    public void Dispose()
    {
        if (GodotObject.IsInstanceValid(playerMenu))
            playerMenu.IdPressed -= OnAction;
    }

    internal bool TryOpen(Vector2 mouse)
    {
        var target = world.Selection.PickEntityAt(mouse, SelectionFeature.ClickPickRadius, out int id, out _);
        if (target == null || target.IsNpc || target.Dead || id == context.Self.CharId) return false;

        Show(id, target, mouse);
        return true;
    }

    internal void Show(int id, EntityView target, Vector2 at)
    {
        world.Selection.Select(id, target);
        this.id = id;
        name = target.Name;

        var rows = new System.Collections.Generic.List<PlayerMenuAction>
        {
            PlayerMenuAction.RequestParty, PlayerMenuAction.Report, PlayerMenuAction.UserTrade, PlayerMenuAction.Whisper,
            PlayerMenuAction.AddFriend, PlayerMenuAction.UserInformation, PlayerMenuAction.Duel, PlayerMenuAction.EquipmentView,
        };
        if (clan.MyClan.CanInvite && target.KnightsId == 0) rows.Add(PlayerMenuAction.ClanInvite);
        if (world.ClanBattleWar.CanChallenge(target)) rows.Add(PlayerMenuAction.ClanBattle);
        if (clan.MyClan.IsChief && hostility.AtClanWarWith(target.KnightsId))
            rows.Add(PlayerMenuAction.ClanSurrender);
        rows.Add(PlayerMenuAction.CopyName);
        Present(rows, at);
    }

    private void Present(System.Collections.Generic.List<PlayerMenuAction> rows, Vector2 at)
    {
        CloseSkin();
        if (PluginHost.Ui.PlayerMenuBuilder is { } build)
        {
            var request = new LibreKO.Plugins.PlayerMenuRequest(name, at,
                rows.ConvertAll(action => new LibreKO.Plugins.PlayerMenuRow(PlayerMenuRows[action].Key, PlayerMenuRows[action].Caption,
                    () => { CloseSkin(); OnAction((long)action); })),
                CloseSkin);
            skinLayer = new CanvasLayer { Layer = UiLayers.Menus };
            context.Root.AddChild(skinLayer);
            skinLayer.AddChild(build(request));
            return;
        }
        playerMenu.Clear();
        foreach (var action in rows) playerMenu.AddItem(PlayerMenuRows[action].Caption, (int)action);
        UiKit.PopupAtCursor(context.Root, playerMenu, at);
    }

    private void CloseSkin()
    {
        if (skinLayer != null && GodotObject.IsInstanceValid(skinLayer)) skinLayer.QueueFree();
        skinLayer = null;
    }

    internal void ShowByName(string name, Vector2 at)
    {
        foreach (var (id, e) in context.Entities.Views)
        {
            if (e.IsNpc || e.Dead || id == context.Self.CharId) continue;
            if (!string.Equals(e.Name, name, System.StringComparison.OrdinalIgnoreCase)) continue;
            Show(id, e, at);
            return;
        }

        id = -1;
        this.name = name;
        Present(new System.Collections.Generic.List<PlayerMenuAction>
        {
            PlayerMenuAction.Whisper, PlayerMenuAction.AddFriend, PlayerMenuAction.RequestParty,
            PlayerMenuAction.UserInformation, PlayerMenuAction.EquipmentView, PlayerMenuAction.Report, PlayerMenuAction.CopyName,
        }, at);
    }

    internal bool HasNearbyForUserInfo()
    {
        if (!context.Ready || context.Self.Body == null || context.Self.Dead) return false;
        foreach (var (id, e) in context.Entities.Views)
        {
            if (id == context.Self.CharId || e.IsNpc || e.Dead) continue;
            if (Coord.FlatDistance(context.Self.Body.Position, e.Body.Position) <= ExchangeFeature.TradeRange)
                return true;
        }
        return false;
    }

    internal bool TryFindInteraction(bool merchantOnly, out int bestId, out EntityView? best)
    {
        bestId = -1;
        best = null;
        if (!context.Ready || context.Self.Body == null || context.Self.Dead) return false;
        bool Eligible(int id, EntityView e) => id != context.Self.CharId && !e.IsNpc && !e.Dead
            && (!merchantOnly || world.EntityPlatesSystem.HasStall(id))
            && Coord.FlatDistance(context.Self.Body.Position, e.Body.Position) <= ExchangeFeature.TradeRange;
        if (context.Entities.Views.TryGetValue(context.Selection.SelectedId, out var selected) && Eligible(context.Selection.SelectedId, selected))
        {
            bestId = context.Selection.SelectedId;
            best = selected;
            return true;
        }
        float distance = ExchangeFeature.TradeRange;
        foreach (var (id, e) in context.Entities.Views)
        {
            if (!Eligible(id, e)) continue;
            float d = Coord.FlatDistance(context.Self.Body.Position, e.Body.Position);
            if (d > distance) continue;
            distance = d;
            bestId = id;
            best = e;
        }
        return best != null;
    }

    internal void OpenNearest(Vector2 at)
    {
        if (TryFindInteraction(false, out int id, out var target) && target != null)
            Show(id, target, at);
    }

    private void OnAction(long actionId)
    {
        if (this.name.Length == 0) return;
        string name = this.name;
        int id = this.id;

        switch ((PlayerMenuAction)actionId)
        {
            case PlayerMenuAction.RequestParty:
                InviteToParty(name);
                break;

            case PlayerMenuAction.Report:
                OpenReportAgainst(name);
                break;

            case PlayerMenuAction.UserTrade:
                RequestTradeWith(id, name);
                break;

            case PlayerMenuAction.Whisper:
                world.Whispers.OpenWhisperWith(name);
                break;

            case PlayerMenuAction.AddFriend:
                world.Friends.RequestFriend(name);
                break;

            case PlayerMenuAction.UserInformation:
                world.UserInfo.RequestInformation(name);
                break;

            case PlayerMenuAction.Duel:
                world.Challenge.ByName(name);
                break;

            case PlayerMenuAction.EquipmentView:
                world.EquipView.RequestEquipmentView(name);
                break;

            case PlayerMenuAction.ClanInvite:
                clanRequests.Invite(id);
                context.Log.Notice($"Inviting {name} to the clan…");
                break;

            case PlayerMenuAction.CopyName:
                DisplayServer.ClipboardSet(name);
                context.Log.Notice($"Copied {name}.");
                break;

            case PlayerMenuAction.ClanBattle:
                clanBattleWarRequests.ClanBattleChallenge(id);
                break;

            case PlayerMenuAction.ClanSurrender:
                Notice.Confirm(context.Root, "Surrender the clan battle?", "Surrender", "Cancel", clanBattleWarRequests.ClanBattleSurrender);
                break;
        }
    }

    internal void InviteToParty(string name)
    {
        if (party.InParty && !party.IsLeader(context.Self.CharId))
        {
            context.Log.Notice("Only the party leader can invite.");
            return;
        }
        if (party.InParty && party.Members.Count >= PartyWire.MaxMembers)
        {
            context.Log.Notice("Your party is full.");
            return;
        }

        if (party.InParty) partyRequests.Invite(name);
        else partyRequests.Create(name);
    }

    internal void RequestTradeWith(int charId, string name)
    {
        if (world.Exchange.TradeHoldsInput) { context.Log.Notice("You are already trading."); return; }
        if (context.Self.Dead) return;

        if (!context.Entities.Views.TryGetValue(charId, out var e) || e.Dead)
        {
            context.Log.Notice($"{name} is no longer nearby.");
            return;
        }

        if (e.Body.Position.DistanceTo(context.Self.Body.Position) > ExchangeFeature.TradeRange)
        {
            context.Log.Notice($"{name} is too far away to trade.");
            return;
        }

        world.Exchange.BeginTradeRequest(charId, name);
    }

    private void OpenReportAgainst(string name)
    {
        if (!world.Report.IsOpen) world.Report.Toggle();
        world.Report.TargetEdit.Text = name;
        world.Report.ReasonEdit.GrabFocus();
    }
}
