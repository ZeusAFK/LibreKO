using Godot;
using LibreKO.Scene;

namespace LibreKO.Features.Windows;

internal sealed class CommandListFeature : IFeature
{
    private readonly IWorldContext context;
    private readonly WorldRoot world;
    private readonly ClanSession clan;
    private readonly PartySession party;
    private readonly UserOptionSession userOption;
    private readonly IChatRequests chatRequests;
    private readonly IClanRequests clanRequests;
    private readonly IClanBattleWarRequests clanBattleWarRequests;
    private readonly IUserOptionRequests userOptionRequests;

    public CommandListFeature(IWorldContext context, WorldRoot world, ClanSession clan, PartySession party, UserOptionSession userOption, IChatRequests chatRequests, IClanRequests clanRequests, IClanBattleWarRequests clanBattleWarRequests, IUserOptionRequests userOptionRequests)
    {
        this.context = context;
        this.world = world;
        this.clan = clan;
        this.party = party;
        this.userOption = userOption;
        this.chatRequests = chatRequests;
        this.clanRequests = clanRequests;
        this.clanBattleWarRequests = clanBattleWarRequests;
        this.userOptionRequests = userOptionRequests;
    }

    private const int Width = 220;
    private const int GroupRowsShown = 6;
    private const int RowsShown = 12;
    private const float RowHeight = 22f;
    private const float Padding = 16f;
    private const int TextInset = 14;
    private static readonly Color GroupColour = new("ffff00");
    private static readonly Color EntryColour = new("c6c6fb");
    private static readonly Color UnavailableColour = new("808080");
    private static readonly Color RefusalColour = new("ff3b3b");

    private static readonly HashSet<CommandId> CommandsNotBuilt =
    [CommandId.Recommend, CommandId.EditMerchant, CommandId.ClanCommand, CommandId.ClanBattle, CommandId.ClanKick, CommandId.BanKnights, CommandId.Report];

    internal CanvasLayer Layer { get; set; } = null!;
    internal HudWindow Window { get; private set; } = null!;
    internal ItemList Groups { get; private set; } = null!;
    private ItemList entries = null!;
    private readonly List<CommandEntry> cmdShown = new();

    public void Build()
    {
        Layer = new CanvasLayer { Layer = UiLayers.HudButtons };
        context.Root.AddChild(Layer);
        BuildWindow();
        Config.TradeRequestsBlockedChanged += SendTradeState;
    }

    public void Dispose() => Config.TradeRequestsBlockedChanged -= SendTradeState;

    private void SendTradeState() => userOptionRequests.TradeState(Config.TradeRequestsBlocked);

    internal void Toggle()
    {
        if (Window.Visible)
        {
            Window.Visible = false;
            return;
        }
        if (Groups.GetSelectedItems().Length == 0) Groups.Select(0);
        FillEntries(SelectedGroup());
        Window.Visible = true;
    }

    private CommandGroup SelectedGroup()
    {
        int[] picked = Groups.GetSelectedItems();
        return CommandCatalog.Groups[picked.Length > 0 ? picked[0] : 0];
    }

    internal void BuildWindow()
    {
        Window = new HudWindow("command_list", "Command", Width) { Visible = false };
        Window.Closed += () => Window.Visible = false;
        Layer.AddChild(Window);
        var body = new VBoxContainer();
        body.AddThemeConstantOverride("separation", 10);
        Window.Body.AddChild(body);

        Groups = MakeList(GroupRowsShown);
        foreach (var group in CommandCatalog.Groups)
        {
            int row = Groups.AddItem(ItemData.Text(CommandCatalog.GroupText(group), CommandCatalog.GroupFallback(group)));
            Groups.SetItemCustomFgColor(row, GroupColour);
        }
        Groups.ItemSelected += index => FillEntries(CommandCatalog.Groups[(int)index]);
        body.AddChild(Groups);

        entries = MakeList(RowsShown);
        entries.ItemActivated += index => RunListed((int)index);
        body.AddChild(entries);
    }

    private static ItemList MakeList(int rows)
    {
        var list = new ItemList
        {
            CustomMinimumSize = new Vector2(0, rows * RowHeight + Padding),
            FocusMode = Control.FocusModeEnum.None,
            SelectMode = ItemList.SelectModeEnum.Single,
            AllowReselect = true,
        };
        list.AddThemeStyleboxOverride("panel", UiTheme.Inset());
        list.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
        list.AddThemeStyleboxOverride("selected", UiTheme.ListRow(true));
        list.AddThemeStyleboxOverride("selected_focus", UiTheme.ListRow(true));
        list.AddThemeStyleboxOverride("hovered", UiTheme.ListRow(false));
        list.AddThemeStyleboxOverride("cursor", new StyleBoxEmpty());
        list.AddThemeStyleboxOverride("cursor_unfocused", new StyleBoxEmpty());
        list.AddThemeFontSizeOverride("font_size", 13);
        list.AddThemeColorOverride("font_selected_color", UiTheme.GoldBright);
        list.AddThemeColorOverride("font_hovered_color", UiTheme.TextHi);
        list.AddThemeConstantOverride("v_separation", 4);
        list.AddThemeConstantOverride("h_separation", TextInset);
        return list;
    }

    internal void FillEntries(CommandGroup group)
    {
        entries.Clear();
        cmdShown.Clear();
        foreach (var entry in CommandCatalog.Entries(group))
        {
            int row = entries.AddItem(ItemData.Text(entry.NameText, entry.Fallback));
            bool built = !CommandsNotBuilt.Contains(entry.Id);
            string hint = CommandCatalog.Hint(entry, ItemData.Text);
            entries.SetItemTooltip(row, built ? hint : $"{hint}\n(not available yet)".TrimStart('\n'));
            entries.SetItemCustomFgColor(row, built ? EntryColour : UnavailableColour);
            cmdShown.Add(entry);
        }
    }

    private void RunListed(int index)
    {
        if (index < 0 || index >= cmdShown.Count) return;
        var entry = cmdShown[index];
        if (CommandsNotBuilt.Contains(entry.Id)) return;
        string word = ItemData.Text(entry.NameText, entry.Fallback);
        if (entry.Prefill) context.Chat?.OpenWith($"/{word} ");
        else context.Chat?.RunCommand(word);
    }

    private bool TargetPlayer(out int id, out string name)
    {
        id = -1;
        name = "";
        if (context.Selection.SelectedId < 0 || context.Selection.SelectedId == context.Self.CharId || !context.Entities.Views.TryGetValue(context.Selection.SelectedId, out var e) || e.IsNpc) return false;
        id = context.Selection.SelectedId;
        name = e.Name;
        return true;
    }

    private void Refusal(int textId, string fallback) =>
        context.Log.Add(ItemData.Text(textId, fallback), CombatLogKind.Status, RefusalColour);

    private const string VaultCommand = "vault";

    internal bool RunLocalCommand(string command)
    {
        string[] parts = command.Split(' ', System.StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return false;

        if (parts[0].Equals("leave", System.StringComparison.OrdinalIgnoreCase)
            || parts[0].Equals("exit", System.StringComparison.OrdinalIgnoreCase))
        {
            if (TempleZones.IsTempleEventZone(context.Zone))
            {
                world.Bifrost.OnInZoneLeavePressed();
                return true;
            }
        }

        if (CommanderOrders.SlashSlot(parts[0], ItemData.Text) is var commanderSlot and not CommanderOrders.NoSlot)
        {
            if (world.PartyCommander.AmCommander) world.PartyCommander.UseCommanderSlot(commanderSlot);
            return true;
        }

        if (parts[0].Equals(VaultCommand, System.StringComparison.OrdinalIgnoreCase))
        {
            world.VipWarehouse.OpenVaultByCommand();
            return true;
        }

        if (CommandCatalog.Find(parts[0], ItemData.Text) is { } catalogCommand
            && RunCatalog(catalogCommand, command[parts[0].Length..].Trim()))
            return true;

        if (!parts[0].Equals("collision", System.StringComparison.OrdinalIgnoreCase))
            return false;
        return world.PlayerSystem.CollisionCommand(parts.Length < 2 ? null : parts[1]);
    }

    internal bool RunCatalog(CommandEntry entry, string args)
    {
        if (CommandsNotBuilt.Contains(entry.Id)) return false;
        switch (entry.Id)
        {
            case CommandId.Pm:
            {
                int gap = args.IndexOf(' ');
                string name = gap < 0 ? args : args[..gap];
                string message = gap < 0 ? "" : args[(gap + 1)..].Trim();
                if (name.Length == 0) context.Chat?.OpenWith($"/{ItemData.Text(entry.NameText, entry.Fallback)} ");
                else if (message.Length == 0) world.Whispers.OpenWhisperWith(name);
                else context.Chat?.SendWhisper(name, message);
                return true;
            }
            case CommandId.Town:
                world.TownRecall.Press();
                return true;
            case CommandId.Exit:
                world.GameMenu.RequestExit(GameMenuFeature.ExitKind.Quit);
                return true;
            case CommandId.IndividualBattle:
                if (args.Length > 0) world.Challenge.ByName(args);
                else if (TargetPlayer(out _, out string duelName)) world.Challenge.ByName(duelName);
                else Refusal(CommandCatalog.NoTargetText, "No target selected.");
                return true;
            case CommandId.SitStand:
                world.PlayerSystem.ToggleSitting();
                return true;
            case CommandId.WalkRun:
                world.PlayerSystem.ToggleRunMode();
                return true;
            case CommandId.Location:
                context.Log.Add($"{ZoneCatalog.Name(context.Zone)} ({context.Self.MapXZ.X:0}, {context.Self.MapXZ.Z:0})", CombatLogKind.Status);
                return true;
            case CommandId.AddFriend:
                if (args.Length > 0) world.Friends.RequestFriend(args);
                else if (TargetPlayer(out _, out string friendName)) world.Friends.RequestFriend(friendName);
                else context.Chat?.OpenWith($"/{ItemData.Text(entry.NameText, entry.Fallback)} ");
                return true;
            case CommandId.ChatBlock:
                world.Whispers.ToggleWhisperBlock();
                return true;
            case CommandId.Vault:
                world.VipWarehouse.OpenVaultByCommand();
                return true;
            case CommandId.Achieve:
                world.Achievement.ToggleAchievements();
                return true;
            case CommandId.Trade:
                if (TargetPlayer(out int tradeId, out string tradeName)) world.PlayerMenu.RequestTradeWith(tradeId, tradeName);
                else Refusal(CommandCatalog.NoTargetText, "No target selected.");
                return true;
            case CommandId.BlockTrade or CommandId.AllowTrade:
                Config.SetTradeRequestsBlocked(entry.Id == CommandId.BlockTrade);
                Refusal(Config.TradeRequestsBlocked ? CommandCatalog.TradeBlockedText : CommandCatalog.TradeAllowedText,
                    Config.TradeRequestsBlocked ? "Block all Trade Requests." : "Allow all Trade Requests.");
                return true;
            case CommandId.Merchant:
                if (!world.Merchant.MenuShown) world.Merchant.ToggleMenu();
                return true;
            case CommandId.Price:
                world.MarketPrice.OpenMarketPrice();
                return true;
            case CommandId.Party:
                if (args.Length > 0) world.PlayerMenu.InviteToParty(args);
                else if (TargetPlayer(out _, out string partyName)) world.PlayerMenu.InviteToParty(partyName);
                else Refusal(CommandCatalog.NoTargetText, "No target selected.");
                return true;
            case CommandId.QuitParty:
                if (party.InParty) world.Party.AskLeave();
                else context.Log.Notice("You are not in a party.");
                return true;
            case CommandId.SeekingParty:
                world.SeekParty.ToggleSeek();
                return true;
            case CommandId.BlockParty or CommandId.AllowParty:
                userOption.BlockPartyRequests(entry.Id == CommandId.BlockParty);
                Refusal(userOption.PartyRequestsBlocked ? CommandCatalog.PartyBlockedText : CommandCatalog.PartyAllowedText,
                    userOption.PartyRequestsBlocked ? "Block all Party Requests." : "Allow all Party Requests.");
                return true;
            case CommandId.JoinClan:
                if (clan.MyClan.CanInvite && TargetPlayer(out int recruitId, out string recruitName))
                {
                    clanRequests.Invite(recruitId);
                    context.Log.Notice($"Inviting {recruitName} to the clan…");
                }
                return true;
            case CommandId.QuitClan:
                if (clan.MyClan.InClan) world.Clan.LeaveAsk.PopupCentered();
                return true;
            case CommandId.BanFromClan:
                if (args.Length == 0) context.Chat?.OpenWith($"/{ItemData.Text(entry.NameText, entry.Fallback)} ");
                else if (clan.MyClan.IsChief)
                    Notice.Confirm(context.Root, TextTemplate.Fill(ItemData.Text(CommandCatalog.BanFromClanAskText, "Do you want to ban %s from clan?"), args),
                        "OK", "Cancel", () => clanRequests.Kick(args), title: "Clan");
                return true;
            case CommandId.ClanWar:
                if ((clan.MyClan.IsChief || clan.MyClan.Fame == ClanRanks.CommandCaptain) && TargetPlayer(out int rivalId, out _))
                    clanBattleWarRequests.ClanBattleChallenge(rivalId);
                return true;
            case CommandId.Surrender:
                if (clan.MyClan.IsChief || clan.MyClan.Fame == ClanRanks.CommandCaptain) clanBattleWarRequests.ClanBattleSurrender();
                return true;
            case CommandId.AppointVice:
                if (args.Length == 0) context.Chat?.OpenWith($"/{ItemData.Text(entry.NameText, entry.Fallback)} ");
                else if (clan.MyClan.IsChief) clanRequests.PromoteVice(args);
                return true;
            case CommandId.ClanChat:
                if (args.Length == 0) context.Chat?.OpenWith($"/{ItemData.Text(entry.NameText, entry.Fallback)} ");
                else if (clan.MyClan.InClan) chatRequests.Send(args, (byte)ChatType.Clan);
                return true;
            case CommandId.ClanNotice:
                if (args.Length == 0) context.Chat?.OpenWith($"/{ItemData.Text(entry.NameText, entry.Fallback)} ");
                else if (clan.MyClan.IsChief || clan.MyClan.IsViceChief) clanRequests.Notice(args);
                return true;
            case CommandId.Confederacy:
                if (clan.MyClan.IsChief && TargetPlayer(out int allyId, out _))
                {
                    if (clan.MyClan.AllianceId == clan.MyClan.ClanId) clanRequests.AllianceInsert(allyId);
                    else clanRequests.AllianceCreate(allyId);
                }
                return true;
            case CommandId.QuitKnights:
                if (clan.MyClan.IsChief && clan.MyClan.AllianceId > 0) clanRequests.AllianceLeave();
                return true;
            default:
                return false;
        }
    }
}
