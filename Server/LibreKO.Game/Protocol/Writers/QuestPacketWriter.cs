using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.World;
using LibreKO.Quests.Runtime;
using LibreKO.Quests.Binding;

namespace LibreKO.Game.Protocol.Writers;

public sealed class QuestPacketWriter
{
    public const byte KillCountSlots = 4;

    public readonly record struct QuestEntry(short QuestId, QuestStatus Status);

    public readonly record struct TextEntry(short QuestId, string Title, string Journal);

    public const int NoCoordinate = -1;

    public static Packet TargetDetail(
        QuestLocation location, int currentZone, int nation, int questId, string questTitle,
        Func<string, string> localize)
    {
        var elmorad = nation == QuestVocabulary.Nations["elmorad"];
        var x = (elmorad ? location.ElMoradX : location.X) ?? NoCoordinate;
        var y = (elmorad ? location.ElMoradY : location.Y) ?? NoCoordinate;
        var packet = Sub(QuestSubOpcode.TargetDetail);
        packet.WriteByte(1);
        packet.WriteShort(checked((short)questId));
        packet.WriteInt(checked((int)(location.Zone ?? currentZone)));
        packet.WriteInt(checked((int)x));
        packet.WriteInt(checked((int)y));
        packet.WriteUtf8String(localize(questTitle));
        packet.WriteUtf8String(localize(location.Title));
        packet.WriteUtf8String(localize(location.About ?? string.Empty));
        packet.WriteUtf8String(localize(location.Where ?? string.Empty));
        return packet;
    }

    public static Packet RewardReceipt(int questId, IReadOnlyList<(int ItemId, int Count)> granted)
    {
        var packet = Sub(QuestSubOpcode.RewardReceipt);
        packet.WriteByte(1);
        packet.WriteShort(checked((short)questId));
        packet.WriteByte(checked((byte)granted.Count));
        foreach (var (itemId, count) in granted)
        {
            packet.WriteInt(itemId);
            packet.WriteInt(count);
        }
        return packet;
    }

    public static Packet View(QuestView view, int npcId, bool open, long nextReset, Func<string, string> localize, Func<int, string>? monsterName = null, bool notification = false)
    {
        var packet = Sub(QuestSubOpcode.View);
        packet.WriteByte(2);
        packet.WriteShort(checked((short)view.Text.QuestId));
        packet.WriteInt(npcId);
        packet.WriteInt(view.ZoneId);
        var controls = open && !notification && !view.AutoAccepted && view.Page == QuestPageKind.Quest;
        packet.WriteByte((byte)((open ? 1 : 0)
            | (controls && view.State == QuestViewState.Available ? 2 : 0)
            | (controls && view.State == QuestViewState.Claimable ? 4 : 0)
            | (view.Text.Daily ? 8 : 0) | (notification ? 16 : 0) | (view.AutoAccepted ? 32 : 0)));
        packet.WriteByte((byte)view.State);
        packet.WriteByte((byte)view.Page);
        packet.WriteLong(nextReset);
        packet.WriteUtf8String(localize(view.Text.Title ?? string.Empty));
        packet.WriteUtf8String(localize(view.Text.Journal ?? string.Empty));
        packet.WriteUtf8String(localize(view.Dialogue.Text ?? string.Empty));
        packet.WriteByte((byte)(view.Objectives?.Rule ?? ObjectiveRule.All));
        packet.WriteByte(checked((byte)(view.Objectives?.Groups.Count ?? 0)));
        for (var index = 0; index < (view.Objectives?.Groups.Count ?? 0); index++)
        {
            var goal = view.Objectives!.Groups[index];
            packet.WriteUShort(checked((ushort)goal.Count));
            packet.WriteUShort(checked((ushort)Math.Clamp(view.Counts[index], 0, goal.Count)));
            packet.WriteByte(checked((byte)goal.Monsters.Count));
            foreach (var monster in goal.Monsters)
                packet.WriteInt(monster);
            packet.WriteUtf8String(localize(monsterName?.Invoke(goal.Monsters[0]) ?? "Creature"));
            packet.WriteByte((byte)(goal.Target >= 0 ? 1 : 0));
        }
        WriteTransfers(packet, view.Rewards.Transfers);
        WriteTransfers(packet, view.Rewards.Options);
        packet.WriteUShort(checked((ushort)(open ? view.Topics.Count : 0)));
        if (open)
            foreach (var topic in view.Topics)
                packet.WriteUtf8String(localize(topic.Label.Text ?? string.Empty));
        return packet;
    }

    private static void WriteTransfers(Packet packet, IReadOnlyList<BoundStatement.Action> actions)
    {
        packet.WriteUShort(checked((ushort)actions.Count));
        foreach (var action in actions)
        {
            packet.WriteByte((byte)(action.Kind is QuestActionKind.TakeItem or QuestActionKind.TakeGold
                or QuestActionKind.TakeNationalPoints ? 1 : 0));
            var kind = TransferKind(action.Kind);
            packet.WriteByte((byte)kind);
            packet.WriteInt(action.Arguments.GetInt("item"));
            packet.WriteInt(action.Arguments.GetInt("amount", action.Arguments.GetInt("count", 1)));
            packet.WriteInt(QuestInterpreter.RentalHours(action.Arguments));
        }
    }

    public static Packet Clock(DateTime now)
    {
        var packet = Sub(QuestSubOpcode.Clock);
        packet.WriteUShort((ushort)now.Year);
        packet.WriteByte((byte)now.Month);
        packet.WriteByte((byte)now.Day);
        packet.WriteByte((byte)now.Hour);
        packet.WriteByte((byte)now.Minute);
        packet.WriteByte((byte)now.Second);
        return packet;
    }

    public static Packet QuestList(IReadOnlyCollection<QuestEntry> quests)
    {
        var packet = Sub(QuestSubOpcode.QuestList);
        packet.WriteShort((short)quests.Count);
        foreach (var quest in quests)
        {
            packet.WriteShort(quest.QuestId);
            packet.WriteByte((byte)quest.Status);
        }
        return packet;
    }

    public static Packet StateChange(short questId, QuestStatus status)
    {
        var packet = Sub(QuestSubOpcode.StateChange);
        packet.WriteShort(questId);
        packet.WriteByte((byte)status);
        return packet;
    }

    public static Packet RewardRefused(QuestRewardRefusal reason)
    {
        var packet = Sub(QuestSubOpcode.RewardRefused);
        packet.WriteByte((byte)reason);
        return packet;
    }

    public static Packet NpcEvent(int questTalkIndex, short npcId)
    {
        var packet = Sub(QuestSubOpcode.NpcEvent);
        packet.WriteInt(questTalkIndex);
        packet.WriteShort(npcId);
        return packet;
    }

    public static Packet KillCountUpdate(short questId, int groupIndex, ushort count)
    {
        var packet = Sub(QuestSubOpcode.KillCounts);
        packet.WriteByte((byte)QuestKillCountForm.Single);
        packet.WriteShort(questId);
        packet.WriteByte((byte)(groupIndex + 1));
        packet.WriteUShort(count);
        return packet;
    }

    public static Packet KillCounts(short questId, IReadOnlyList<ushort> counts)
    {
        var packet = Sub(QuestSubOpcode.KillCounts);
        packet.WriteByte((byte)QuestKillCountForm.All);
        packet.WriteShort(questId);
        for (var index = 0; index < KillCountSlots; index++)
            packet.WriteUShort(index < counts.Count ? counts[index] : (ushort)0);
        return packet;
    }

    public static int TransferKind(QuestActionKind kind) => kind switch
    {
        QuestActionKind.GiveGold or QuestActionKind.TakeGold => 1,
        QuestActionKind.GiveExperience => 2,
        QuestActionKind.GiveNationalPoints or QuestActionKind.TakeNationalPoints => 3,
        QuestActionKind.PromoteNovice => 4,
        QuestActionKind.Promote => 5,
        _ => 0
    };

    public static Packet Objectives(QuestObjectives objectives)
    {
        var packet = Sub(QuestSubOpcode.Objectives);
        packet.WriteShort(checked((short)objectives.QuestId));
        packet.WriteByte((byte)objectives.Rule);
        packet.WriteByte(checked((byte)objectives.Groups.Count));
        foreach (var group in objectives.Groups)
        {
            packet.WriteUShort(checked((ushort)group.Count));
            packet.WriteByte(checked((byte)group.Monsters.Count));
            foreach (var monster in group.Monsters)
                packet.WriteInt(monster);
        }
        return packet;
    }

    public static Packet Texts(IReadOnlyCollection<TextEntry> quests)
    {
        var packet = Sub(QuestSubOpcode.Text);
        packet.WriteShort((short)quests.Count);
        foreach (var quest in quests)
        {
            packet.WriteShort(quest.QuestId);
            packet.WriteUtf8String(quest.Title);
            packet.WriteUtf8String(quest.Journal);
        }
        return packet;
    }

    private static Packet Sub(QuestSubOpcode sub)
    {
        var packet = new Packet(GameOpcodes.GS_QUEST);
        packet.WriteByte((byte)sub);
        return packet;
    }
}
