using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using LibreKO.Domain;
using LibreKO.Network;
using LibreKO.Plugins;

namespace LibreKO;

public partial class World
{
    private const int TradeGoldPromptLayer = 82;

    private PluginSocialBridge? _pluginSocial;
    private QuantityPrompt? _tradeGoldPrompt;

    private void PluginSocialInit()
    {
        _pluginSocial = new PluginSocialBridge(this);
        PluginHost.Game.AttachSocial(_pluginSocial, _pluginSocial, _pluginSocial);
        PluginHost.Game.AttachWarp(_pluginSocial);
    }

    private void PluginNotifyWhispers()
    {
        if (_pluginSocial != null) Callable.From(PluginHost.Game.RaiseWhispers).CallDeferred();
    }

    private void PluginNotifyWhisperLine(string name, bool mine, bool notice, string text)
    {
        if (_pluginSocial != null) PluginHost.Game.RaiseWhisperLine(name, new GameWhisperLine(mine, notice, text));
    }

    private void PluginNotifyParty()
    {
        if (_pluginSocial != null) Callable.From(PluginHost.Game.RaiseParty).CallDeferred();
    }

    private void PluginNotifyWarp()
    {
        if (_pluginSocial != null) Callable.From(PluginHost.Game.RaiseWarp).CallDeferred();
    }

    private void PluginNotifyTrade()
    {
        if (_pluginSocial != null) Callable.From(PluginHost.Game.RaiseTrade).CallDeferred();
    }

    private void AskTradeGold()
    {
        if (!_exShown) return;
        if (_exConfirmedByMe) { SetExStatus("You already confirmed.", true); return; }
        if (Sheet.Gold <= 0) { SetExStatus("Not enough gold.", true); return; }
        if (_tradeGoldPrompt == null)
        {
            _tradeGoldPrompt = new QuantityPrompt(TradeGoldPromptLayer);
            AddChild(_tradeGoldPrompt);
        }
        _tradeGoldPrompt.Open(null, "Offer gold", $"You carry {Sheet.Gold:n0}", Sheet.Gold, Sheet.Gold, n => AddTradeGold((int)n));
    }

    private sealed class PluginSocialBridge(World w) : IGameWhispers, IGameParty, IGameTrade, IGameWarp
    {
        event Action? IGameWarp.Changed { add { } remove { } }

        bool IGameWarp.IsOpen => w._warpShown;
        string IGameWarp.Title => w._warpTitle;
        int IGameWarp.Selected => w._warpSelected;
        void IGameWarp.Select(int index) => w.SelectWarpRow(index);
        void IGameWarp.Travel() => w.TravelSelectedWarp();
        void IGameWarp.Close() => w.CloseWarp();

        IReadOnlyList<GameWarpRow> IGameWarp.Rows => w._warpEntries.ConvertAll(entry =>
        {
            var info = WarpInfo(entry);
            return new GameWarpRow(info.Name, WarpRows.LevelText(info.MinLevel, info.MaxLevel), WarpRows.FeeText(entry.Fee),
                w.WarpBlockReason(entry, info), WarpData.Image(info.Image), info.Description);
        });

        event Action<string, GameWhisperLine>? IGameWhispers.LineAdded { add { } remove { } }
        event Action? IGameWhispers.Changed { add { } remove { } }
        event Action? IGameParty.Changed { add { } remove { } }
        event Action? IGameTrade.Changed { add { } remove { } }

        IReadOnlyList<string> IGameWhispers.Open => w._whispers.Keys.ToList();

        IReadOnlyList<GameWhisperLine> IGameWhispers.History(string name) =>
            Net.I.WhisperHistory(name).Select(l => new GameWhisperLine(l.Mine, l.Notice, l.Text)).ToList();

        bool IGameWhispers.StartsCollapsed(string name) =>
            w._whispers.TryGetValue(name, out var chat) && chat.StartCollapsed;

        bool IGameWhispers.Blocked => Net.I.WhispersBlocked;
        int IGameWhispers.Size => (int)Config.WhisperSize;
        float IGameWhispers.Alpha => Config.WhisperAlpha;

        void IGameWhispers.Send(string name, string text)
        {
            if (name.Length > 0 && text.Trim().Length > 0) w.Chat.SendWhisper(name, text.Trim());
        }

        void IGameWhispers.Close(string name) => w.CloseWhisper(name);
        void IGameWhispers.OpenWith(string name) => w.OpenWhisperWith(name);
        void IGameWhispers.ToggleBlock() => w.ToggleWhisperBlock();
        void IGameWhispers.SetLook(int size, float alpha) => Config.SetWhisperLook((WhisperSize)size, alpha);
        void IGameWhispers.ShowPlayerMenu(string name) => w.ShowPlayerMenuByName(name, w.GetViewport().GetMousePosition());

        bool IGameParty.InParty => w.InParty;
        bool IGameParty.IsLeader => w.AmLeader;

        IReadOnlyList<GamePartyMember> IGameParty.Members
        {
            get
            {
                int leader = PartyMembers.Count > 0 ? PartyMembers[0].CharId : -1;
                var members = w.PartyRows;
                long second = (long)Now();
                var rows = new List<GamePartyMember>(members.Count);
                foreach (var m in members)
                {
                    var view = w.PartyRowOf(m, m.CharId == leader, second);
                    rows.Add(new GamePartyMember(m.CharId, m.Name, view.Level, view.Class, view.Hp, view.MaxHp, view.Mp,
                        view.MaxMp, m.CharId == leader, m.CharId == w._partyPick, view.NameColour, view.HpColour, view.Tooltip,
                        view.HpLook));
                }
                return rows;
            }
        }

        void IGameParty.Pick(int id) => w.PickPartyMember(id);
        void IGameParty.Leave() => w.AskLeaveParty();
        void IGameParty.MakeLeader(int id) => w.RunPartyMemberMenu(id, PartyMenuMakeLeader);
        void IGameParty.Kick(int id) => w.RunPartyMemberMenu(id, PartyMenuKick);
        void IGameParty.MakeCommander(int id) => w.RunPartyMemberMenu(id, PartyMenuCommander);
        void IGameParty.ShowMemberMenu(int id) => w.ShowPartyMemberMenu(id);
        bool IGameParty.CommanderBarHidden => w.CommanderBarHidden;
        void IGameParty.ShowCommanderBar() => w.ShowCommanderBarAgain();
        byte IGameParty.RowMeterMode => Net.I.PartyRowMeterMode;
        int IGameParty.RowMeterShare(int id) => w._warBoardRowShares.TryGetValue(id, out int share) ? share : -1;
        void IGameParty.CycleRowMeter() => w.ToggleRowMeter();
        bool IGameParty.WarBoardOpen => w._warBoard is { Visible: true };
        byte IGameParty.WarBoardMode => w._warBoardMode;
        float IGameParty.WarBoardAlpha => Config.PartyBoardAlpha;
        int IGameParty.WarBoardCooldown => w._warBoardCooldown;
        IReadOnlyList<GameWarBoardRow> IGameParty.WarBoardRows => w._warBoardLines;
        void IGameParty.ToggleWarBoard() => w.ToggleWarBoard();
        void IGameParty.SetWarBoardMode(byte mode) => w.RequestWarBoardMode(mode);
        void IGameParty.SetWarBoardAlpha(float alpha) => w.SetWarBoardAlpha(alpha);
        void IGameParty.MoveRow(int id, int step) => w.MovePartyRow(id, step);
        bool IGameParty.AutoRejoin => w._partyRejoin.On;
        string IGameParty.AutoRejoinTip => w.PartyRejoinTip;
        void IGameParty.ToggleAutoRejoin() => w.TogglePartyRejoin();
        bool IGameParty.UserVoteHere => w._zone == Domain.UserVote.JuraidZone;
        void IGameParty.UserVote(int id) => w.OpenUserVote(id);

        bool IGameTrade.IsOpen => w._exShown;
        string IGameTrade.Partner => w._exPartnerName;
        IReadOnlyList<GameItem> IGameTrade.Mine => Offer(w._exMyOffer);
        IReadOnlyList<GameItem> IGameTrade.Theirs => Offer(w._exTheirOffer);
        long IGameTrade.MyGold => w._exMyGoldOffer;
        long IGameTrade.TheirGold => w._exTheirGoldOffer;
        bool IGameTrade.MyConfirmed => w._exConfirmedByMe;
        bool IGameTrade.TheirConfirmed => w._exConfirmedByPartner;
        void IGameTrade.Offer(int bagSlot) => w.OfferSlot(bagSlot);
        void IGameTrade.AskGold() => w.AskTradeGold();
        void IGameTrade.Confirm() => w.OnExchangeConfirm();
        void IGameTrade.Cancel() => w.AbortExchange(local: true);

        void IGameTrade.ShowTooltip(bool mine, int index)
        {
            var offer = mine ? w._exMyOffer : w._exTheirOffer;
            if (index < 0 || index >= offer.Count) { w.HideItemTooltip(); return; }
            var o = offer[index];
            w.ShowItemTooltip(-1, new ItemSlot { ItemId = o.ItemId, Count = (short)o.Count, Durability = o.Dura });
        }

        void IGameTrade.HideTooltip() => w.HideItemTooltip();

        private static IReadOnlyList<GameItem> Offer(List<ExOfferItem> offer)
        {
            var cells = new GameItem[TradeOfferSlots];
            for (int i = 0; i < cells.Length; i++)
                cells[i] = i < offer.Count
                    ? PluginItem(i, new ItemSlot { ItemId = offer[i].ItemId, Count = (short)offer[i].Count, Durability = offer[i].Dura })
                    : GameItem.Empty(i);
            return cells;
        }
    }
}
