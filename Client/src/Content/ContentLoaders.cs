namespace LibreKO.Content;

internal static class ContentLoaders
{
    internal static void Install()
    {
        GameFiles.ReadText = ContentFiles.ReadText;
        NpcNames.Loader = NpcNamesLoader.Load;
        WarpData.Loader = WarpDataLoader.Load;
        QuestText.Loader = QuestTextLoader.Load;
        SkillMsgOut.Loader = SkillMsgOutLoader.Load;
        AchievementData.Loader = AchievementDataLoader.Load;
        QuestData.Loader = QuestDataLoader.Load;
        SkillData.Loader = SkillDataLoader.Load;
        ItemData.Loader = ItemDataLoader.Load;
    }
}
