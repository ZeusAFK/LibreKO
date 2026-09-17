namespace LibreKO.Network;

public readonly struct QuestEntry
{
    public readonly int QuestId;
    public readonly int State;

    public QuestEntry(int questId, int state)
    {
        QuestId = questId;
        State = state;
    }
}
