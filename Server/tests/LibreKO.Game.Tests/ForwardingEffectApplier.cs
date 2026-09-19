using LibreKO.Common.Domain.Services;
using LibreKO.Game.Scripting;
using LibreKO.Game.World;

namespace LibreKO.Game.Tests;

internal sealed class ForwardingEffectApplier(IGameDataService? gameData = null) : IScriptEffectApplier
{
    public async Task ApplyAsync(UserSession session, QuestScriptContext context, string scriptName)
    {
        foreach (var packet in context.QueuedPackets)
            await session.Client.SendPacket(packet);

        if (context.PendingExperience == 0)
            return;

        session.Experience += context.PendingExperience;
        if (gameData is null)
            return;

        while (session.Experience > 0)
        {
            var maxExp = gameData.GetMaxExpForLevel(session.Level);
            if (maxExp <= 0 || session.Experience < maxExp)
                break;
            session.Experience -= maxExp;
            session.Level++;
        }
        if (session.Experience < 0)
            session.Experience = 0;
    }
}
