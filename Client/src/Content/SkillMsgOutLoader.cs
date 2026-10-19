namespace LibreKO.Content;

internal static class SkillMsgOutLoader
{
    internal static void Load()
    {
        if (JsonTables.ReadObject("res://assets/skills/msgout.json") is not { } root) return;
        JsonTables.ReadIdStrings(root, "cast", SkillMsgOut.AddCast);
        JsonTables.ReadIdStrings(root, "effect", SkillMsgOut.AddEffect);
    }
}
