namespace LibreKO.Features.PluginBridge;

internal sealed class PluginLogBridge(PluginBridgeFeature pluginBridge, ZoneSession zone) : IGameLog
{
    public void Subscribe() => zone.NoticeEvent += OnNotice;

    public void Unsubscribe() => zone.NoticeEvent -= OnNotice;

    event Action<GameLogLine>? IGameLog.LineAdded { add { } remove { } }

    private void OnNotice(string text) => pluginBridge.LogAdd(GameLogKind.System, text, PluginBridgeFeature.SystemLineColor);

    public IReadOnlyList<GameLogLine> History => pluginBridge.Log;
}
