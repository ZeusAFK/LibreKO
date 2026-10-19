namespace LibreKO.Features.PluginBridge;

internal sealed record PluginAdapters(
    PluginCharacterBridge Character, PluginInventoryBridge Inventory, PluginTargetBridge Target, PluginWindowsBridge Windows,
    PluginChatBridge Chat, PluginHotbarBridge Hotbar, PluginMapBridge Map, PluginCommandsBridge Commands, PluginLogBridge Log,
    PluginQuestsBridge Quests, PluginSkillsBridge Skills, PluginServiceBridge Services, PluginSocialBridge Social,
    PluginAppearanceBridge BeautyShop, PluginAppearanceBridge GenderChange, PluginFriendsBridge Friends)
{
    internal void AttachTo(PluginGame game)
    {
        Character.Subscribe();
        Log.Subscribe();
        game.Attach(Character, Inventory, Target, Windows, Chat, Hotbar, Map, Commands, Log, Quests, Skills);
        game.AttachServices(Services, Services, Services);
        game.AttachSocial(Social, Social, Social);
        game.AttachWarp(Social);
        game.AttachAppearance(BeautyShop, GenderChange);
        game.AttachFriends(Friends);
    }

    internal void DetachFrom(PluginGame game)
    {
        game.Detach();
        Character.Unsubscribe();
        Log.Unsubscribe();
    }
}
