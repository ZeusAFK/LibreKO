namespace LibreKO.Features.PluginBridge;

internal sealed class PluginQuestsBridge(QuestTrackerFeature tracker) : IGameQuests
{
    event Action? IGameQuests.Changed { add { } remove { } }

    public IReadOnlyList<GameQuestTrack> Tracked => tracker.TrackedQuestsForPlugins();
}
