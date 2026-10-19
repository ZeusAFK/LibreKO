using LibreKO.Scene;

namespace LibreKO.Features.PluginBridge;

internal sealed class PluginSocialBridge(WarpFeature warp, WhispersFeature whispers, PluginBridgeFeature pluginBridge, Func<ChatSystem?> chat, PlayerMenuFeature playerMenu, PartyFeature party, PartyCommanderFeature partyCommander, PartyWarBoardFeature warBoard, IWorldContext context, UserVoteFeature userVote, ExchangeFeature exchange, ItemTooltipFeature itemTooltip, RoomMediaSession roomMedia, ChatSession chatSession, PartySession partySession, SelfSession self) : IGameWhispers, IGameParty, IGameTrade, IGameWarp
{
    event Action? IGameWarp.Changed { add { } remove { } }

    bool IGameWarp.IsOpen => warp.IsOpen;

    string IGameWarp.Title => warp.Title;

    int IGameWarp.Selected => warp.Selected;

    void IGameWarp.Select(int index) => warp.SelectRow(index);

    void IGameWarp.Travel() => warp.TravelSelected();

    void IGameWarp.Close() => warp.Close();

    IReadOnlyList<GameWarpRow> IGameWarp.Rows => warp.Entries.ConvertAll(entry =>
    {
        var info = WarpFeature.WarpInfo(entry);
        return new GameWarpRow(info.Name, WarpRows.LevelText(info.MinLevel, info.MaxLevel), WarpRows.FeeText(entry.Fee),
            warp.BlockReason(entry, info), WarpImages.Get(info.Image), info.Description);
    });

    event Action<string, GameWhisperLine>? IGameWhispers.LineAdded { add { } remove { } }

    event Action? IGameWhispers.Changed { add { } remove { } }

    event Action? IGameParty.Changed { add { } remove { } }

    event Action? IGameTrade.Changed { add { } remove { } }

    IReadOnlyList<string> IGameWhispers.Open => whispers.Windows.Keys.ToList();

    IReadOnlyList<GameWhisperLine> IGameWhispers.History(string name) =>
        roomMedia.WhisperHistory(name).Select(pluginBridge.WhisperLine).ToList();

    bool IGameWhispers.StartsCollapsed(string name) =>
        whispers.Windows.TryGetValue(name, out var chat) && chat.StartCollapsed;

    bool IGameWhispers.Blocked => chatSession.WhispersBlocked;

    int IGameWhispers.Size => (int)Config.WhisperSize;

    float IGameWhispers.Alpha => Config.WhisperAlpha;

    void IGameWhispers.Send(string name, string text)
    {
        if (name.Length > 0 && text.Trim().Length > 0) chat()?.SendWhisper(name, text.Trim());
    }

    void IGameWhispers.Close(string name) => whispers.CloseWhisper(name);

    void IGameWhispers.OpenWith(string name) => whispers.OpenWhisperWith(name);

    void IGameWhispers.ToggleBlock() => whispers.ToggleWhisperBlock();

    void IGameWhispers.SetLook(int size, float alpha) => Config.SetWhisperLook((WhisperSize)size, alpha);

    void IGameWhispers.ShowPlayerMenu(string name) => playerMenu.ShowByName(name, context.Root.GetViewport().GetMousePosition());

    bool IGameParty.InParty => partySession.InParty;

    bool IGameParty.IsLeader => partySession.IsLeader(context.Self.CharId);

    IReadOnlyList<GamePartyMember> IGameParty.Members
    {
        get
        {
            int leader = partySession.Members.Count > 0 ? partySession.Members[0].CharId : -1;
            var members = party.Rows;
            long second = (long)FrameClock.Now();
            var rows = new List<GamePartyMember>(members.Count);
            foreach (var m in members)
            {
                var view = party.RowOf(m, m.CharId == leader, second);
                rows.Add(new GamePartyMember(m.CharId, m.Name, view.Level, view.Class, view.Hp, view.MaxHp, view.Mp,
                    view.MaxMp, m.CharId == leader, m.CharId == party.Highlight, view.NameColour, view.HpColour, view.Tooltip,
                    view.HpLook));
            }
            return rows;
        }
    }

    IReadOnlyList<GamePartyBuff> IGameParty.MemberBuffs(int id)
    {
        var buffs = new List<GamePartyBuff>();
        foreach (int skillId in partySession.BuffsOf(id))
            if (SkillData.Get(skillId) is { } skill)
                buffs.Add(new GamePartyBuff(skillId, SkillIcons.Get(skillId), SkillText.BuffTooltip(skill)));
        return buffs;
    }

    int IGameParty.PingMs(int id) => party.PingOf(id);

    double IGameParty.LevelPercent(int id) => party.LevelPercentOf(id);

    void IGameParty.Pick(int id) => party.PickMember(id);

    void IGameParty.Leave() => party.AskLeave();

    void IGameParty.MakeLeader(int id) => party.RunMemberMenu(id, PartyFeature.MenuMakeLeader);

    void IGameParty.Kick(int id) => party.RunMemberMenu(id, PartyFeature.MenuKick);

    void IGameParty.MakeCommander(int id) => party.RunMemberMenu(id, PartyFeature.MenuCommander);

    void IGameParty.ShowMemberMenu(int id) => party.ShowMemberMenu(id);

    bool IGameParty.CommanderBarHidden => partyCommander.CommanderBarHidden;

    void IGameParty.ShowCommanderBar() => partyCommander.ShowCommanderBarAgain();

    PartyBoardMeterMode IGameParty.RowMeterMode => Config.PartyRowMeter;

    int IGameParty.RowMeterShare(int id) => warBoard.WarBoardRowShares.TryGetValue(id, out int share) ? share : -1;

    void IGameParty.CycleRowMeter() => warBoard.ToggleRowMeter();

    bool IGameParty.WarBoardOpen => warBoard.Panel is { Visible: true };

    PartyBoardMeterMode IGameParty.WarBoardMode => warBoard.WarBoardMode;

    float IGameParty.WarBoardAlpha => Config.PartyBoardAlpha;

    int IGameParty.WarBoardCooldown => warBoard.WarBoardCooldown;

    IReadOnlyList<GameWarBoardRow> IGameParty.WarBoardRows => warBoard.WarBoardLines;

    void IGameParty.ToggleWarBoard() => warBoard.ToggleWarBoard();

    void IGameParty.SetWarBoardMode(PartyBoardMeterMode mode) => warBoard.RequestWarBoardMode(mode);

    void IGameParty.SetWarBoardAlpha(float alpha) => warBoard.SetWarBoardAlpha(alpha);

    void IGameParty.MoveRow(int id, int step) => party.MoveRow(id, step);

    void IGameParty.MoveRowTo(int id, int index) => party.MoveRowTo(id, index);

    bool IGameParty.AutoRejoin => partySession.Rejoin.On;

    string IGameParty.AutoRejoinTip => party.RejoinTip;

    void IGameParty.ToggleAutoRejoin() => party.ToggleRejoin();

    bool IGameParty.UserVoteHere => context.Zone == TempleZones.JuraidMountain;

    void IGameParty.UserVote(int id) => userVote.Open(id);

    bool IGameTrade.IsOpen => exchange.IsOpen;

    string IGameTrade.Partner => exchange.PartnerName;

    IReadOnlyList<GameItem> IGameTrade.Mine => Offer(exchange.MyOffer);

    IReadOnlyList<GameItem> IGameTrade.Theirs => Offer(exchange.TheirOffer);

    long IGameTrade.MyGold => exchange.MyGoldOffer;

    long IGameTrade.TheirGold => exchange.TheirGoldOffer;

    bool IGameTrade.MyConfirmed => exchange.ConfirmedByMe;

    bool IGameTrade.TheirConfirmed => exchange.ConfirmedByPartner;

    void IGameTrade.Offer(int bagSlot) => exchange.OfferSlot(bagSlot);

    void IGameTrade.AskGold() => AskTradeGold();

    void IGameTrade.Confirm() => exchange.OnConfirm();

    void IGameTrade.Cancel() => exchange.Abort(local: true);

    void IGameTrade.ShowTooltip(bool mine, int index)
    {
        var offer = mine ? exchange.MyOffer : exchange.TheirOffer;
        if (index < 0 || index >= offer.Count) { itemTooltip.Hide(); return; }
        var o = offer[index];
        itemTooltip.Show(-1, new ItemSlot { ItemId = o.ItemId, Count = (short)o.Count, Durability = o.Dura });
    }

    void IGameTrade.HideTooltip() => itemTooltip.Hide();

    private static IReadOnlyList<GameItem> Offer(List<ExchangeFeature.OfferItem> offer)
    {
        var cells = new GameItem[ExchangeFeature.TradeOfferSlots];
        for (int i = 0; i < cells.Length; i++)
            cells[i] = i < offer.Count
                ? PluginBridgeFeature.PluginItem(i, new ItemSlot { ItemId = offer[i].ItemId, Count = (short)offer[i].Count, Durability = offer[i].Dura })
                : GameItem.Empty(i);
        return cells;
    }

    private QuantityPrompt? tradeGoldPrompt;

    internal void AskTradeGold()
    {
        if (!exchange.IsOpen) return;
        if (exchange.ConfirmedByMe) { exchange.SetStatus("You already confirmed.", true); return; }
        if (self.Sheet.Gold <= 0) { exchange.SetStatus("Not enough gold.", true); return; }
        if (tradeGoldPrompt == null)
        {
            tradeGoldPrompt = new QuantityPrompt(UiLayers.PluginPrompts);
            context.Root.AddChild(tradeGoldPrompt);
        }
        tradeGoldPrompt.Open(null, "Offer gold", $"You carry {self.Sheet.Gold:n0}", self.Sheet.Gold, self.Sheet.Gold, n => exchange.AddTradeGold((int)n));
    }
}
