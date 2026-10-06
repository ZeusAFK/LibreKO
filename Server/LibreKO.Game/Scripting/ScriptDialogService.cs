using LibreKO.Common.Enums;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.Protocol.Writers;
using LibreKO.Game.World;

namespace LibreKO.Game.Scripting;

public class ScriptDialogService(
    UserSession session,
    NpcInstance? npc,
    List<Packet> queuedPackets,
    QuestScriptContext context)
{
    public const byte NameChangeShowDialog = 1;

    public void ZoneChange(int zoneId, int x, int z) => context.RequestZoneChange(zoneId, x, z);

    public void ShowEffect(int effectId)
    {
        queuedPackets.Add(NpcDialogPacketWriter.Effect(session.CharacterId, effectId));
    }

    public void ShowNpcEffect(int effectId)
    {
        if (npc == null)
            return;

        queuedPackets.Add(NpcDialogPacketWriter.Effect(npc.UniqueId, effectId));
    }

    public void SendStatSkillDistribute()
    {
        queuedPackets.Add(ClassChangePacketWriter.OpenJobChangePanel());
    }

    public void SendNameChange()
    {
        queuedPackets.Add(MiscPacketWriter.NameChangeResult(NameChangeShowDialog));
    }

    public void SendClanNameChange()
    {
        queuedPackets.Add(MiscPacketWriter.ClanNameChangeResult(NameChangeShowDialog));
    }

    public void SendJobChangePanel()
    {
        queuedPackets.Add(ClassChangePacketWriter.OpenJobChangePanel());
    }

    public void SendRebirthPanel()
    {
        queuedPackets.Add(NpcDialogPacketWriter.RebirthPanel(session.Quest.EventNpcId, session.Quest.ActiveQuestScript));
    }

    public void SendGenderChangePanel()
    {
        queuedPackets.Add(NpcDialogPacketWriter.GenderChangePanel(session.Quest.EventNpcId, session.Quest.ActiveQuestScript));
    }

    public void SendFortunePanel()
    {
        queuedPackets.Add(NpcDialogPacketWriter.FortunePanel(session.Quest.EventNpcId, session.Quest.ActiveQuestScript));
    }

    public void SendSpecialAuctionPanel()
    {
        queuedPackets.Add(NpcDialogPacketWriter.SpecialAuctionPanel(session.Quest.EventNpcId, session.Quest.ActiveQuestScript));
    }

    public void SendItemCombinePanel()
    {
        queuedPackets.Add(NpcDialogPacketWriter.ItemCombinePanel(session.Quest.EventNpcId, session.Quest.ActiveQuestScript));
    }

    public void SendCombineRecipeBook()
    {
        queuedPackets.Add(NpcDialogPacketWriter.CombineRecipeBook(session.Quest.EventNpcId, session.Quest.ActiveQuestScript));
    }

    public void SendFamiliarPanel()
    {
        queuedPackets.Add(NpcDialogPacketWriter.FamiliarPanel(session.Quest.EventNpcId, session.Quest.ActiveQuestScript));
    }

    public void SendFamiliarShop()
    {
        queuedPackets.Add(NpcDialogPacketWriter.FamiliarShop(session.Quest.EventNpcId, session.Quest.ActiveQuestScript));
    }
}
