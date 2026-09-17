using Serilog.Core;
using Serilog.Events;

namespace LibreKO.Common.Infrastructure.Logging;

public sealed class ShortSourceContextEnricher : ILogEventEnricher
{
    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        if (!logEvent.Properties.TryGetValue(Constants.SourceContextPropertyName, out var value))
            return;

        var full = value.ToString().Trim('"');
        var dot = full.LastIndexOf('.');
        var shortName = dot >= 0 ? full[(dot + 1)..] : full;

        logEvent.AddOrUpdateProperty(propertyFactory.CreateProperty("ShortSourceContext", shortName));
    }
}
