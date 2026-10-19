namespace LibreKO.Content;

internal static class QuestTextLoader
{
    internal static void Load()
    {
        if (JsonTables.ReadObject("res://assets/quests/quest_text.json",
                "[quest] missing quest_text.json (run tools/bake_quest_text.py)") is not { } root) return;
        JsonTables.ReadIdStrings(root, "menu", QuestText.AddMenu);
        JsonTables.ReadIdStrings(root, "talk", QuestText.AddTalk);
    }
}
