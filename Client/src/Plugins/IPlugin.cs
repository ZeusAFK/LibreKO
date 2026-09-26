namespace LibreKO.Plugins;

public interface IPlugin
{
    void Initialize(PluginContext context);

    void Shutdown()
    {
    }
}
