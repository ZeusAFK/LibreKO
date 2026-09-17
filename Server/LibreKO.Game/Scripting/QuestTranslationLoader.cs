using LibreKO.Game.Configuration;
using LibreKO.Quests.Localization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LibreKO.Game.Scripting;

public static class QuestTranslationLoader
{
    public const string DirectoryName = "lang";

    public static IQuestTranslations Load(IServiceProvider provider)
    {
        var settings = provider.GetRequiredService<IOptions<GameServerSettings>>().Value;
        var environment = provider.GetRequiredService<IHostEnvironment>();
        var logger = provider.GetRequiredService<ILogger<QuestScriptEngine>>();

        var translations = new QuestTranslations();
        foreach (var questsDirectory in GameAssetPathResolver.GetCandidateDirectories(
                     settings.QuestsDirectory, environment.ContentRootPath, "Quests"))
        {
            var directory = Path.Combine(questsDirectory, DirectoryName);
            if (translations.LoadDirectory(directory) == 0)
                continue;

            foreach (var language in translations.Languages)
                logger.LogInformation("Loaded {Count} quest translations for {Language} from {Directory}",
                    translations.Count(language), language, directory);
            break;
        }

        return translations;
    }
}
