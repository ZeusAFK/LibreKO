using LibreKO.Features.CombatLog;
using LibreKO.Features.Controls;

namespace LibreKO.Features.PluginBridge;

internal sealed class PluginCommandsBridge(TownRecallFeature townRecall, PlayerSystem player, CombatLogFeature log, ExchangeFeature exchange, PlayerMenuFeature playerMenu, GameMenuFeature gameMenu, HotkeysFeature hotkeys, PluginTargetBridge target) : IGameCommands
{
    private const string SelectPlayerNotice = "Select a player to trade with.";

    private const int NoTargetText = 7680;

    public void GoTown() => townRecall.Press();

    public void ToggleSit() => player.ToggleSitting();

    public void TradeWithTarget()
    {
        if (!target.IsPlayer)
        {
            log.Notice(SelectPlayerNotice);
            return;
        }
        exchange.BeginTradeRequest(target.Id, target.TargetName);
    }

    public void InviteTarget()
    {
        if (!target.IsPlayer)
        {
            log.Notice(ItemData.Text(NoTargetText, "No target selected."));
            return;
        }
        playerMenu.InviteToParty(target.TargetName);
    }

    public void ShowExitMenu() => gameMenu.Toggle(true);

    public void OpenSettings() => gameMenu.OpenSettings();

    public void Run(KeyAction action) => hotkeys.RunKeyAction(action);
}
