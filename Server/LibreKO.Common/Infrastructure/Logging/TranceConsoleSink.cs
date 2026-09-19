using Serilog.Core;
using Serilog.Events;

namespace LibreKO.Common.Infrastructure.Logging;

public class TranceConsoleSink : ILogEventSink
{
    private static readonly Lock ConsoleLock = new();

    public void Emit(LogEvent logEvent)
    {
        lock (ConsoleLock)
        {
            var time = logEvent.Timestamp.ToString("HH:mm:ss");

            var source = "";
            if (logEvent.Properties.TryGetValue("SourceContext", out var sourceVal))
            {
                source = sourceVal.ToString().Trim('"');
            }

            var msg = logEvent.RenderMessage();

            if (msg.Contains("Accepting connections", StringComparison.OrdinalIgnoreCase) ||
                msg.Contains("Starting Game Server", StringComparison.OrdinalIgnoreCase) ||
                msg.Contains("Starting Login Server", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            // Suppress verbose informational loading spam on the console unless it's a Warning or Error
            if (logEvent.Level < LogEventLevel.Warning)
            {
                if (msg.StartsWith("Loaded ", StringComparison.OrdinalIgnoreCase) ||
                    msg.Contains("Loaded ", StringComparison.OrdinalIgnoreCase) ||
                    msg.StartsWith("Loading maps from", StringComparison.OrdinalIgnoreCase) ||
                    msg.StartsWith("Spawned ", StringComparison.OrdinalIgnoreCase) ||
                    msg.StartsWith("NpcPositions count:", StringComparison.OrdinalIgnoreCase) ||
                    msg.StartsWith("Starting game data seeding", StringComparison.OrdinalIgnoreCase) ||
                    msg.StartsWith("Game data seeding completed", StringComparison.OrdinalIgnoreCase) ||
                    msg.StartsWith("Thread-pool minimum threads", StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }
            }

            var (category, catColor) = DetermineCategoryAndColor(logEvent.Level, source, msg);
            var catPadded = category.Length > 9 ? category[..9] : category.PadRight(9);

            // Left vertical border
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.Write("  │ ");

            // Timestamp
            Console.ForegroundColor = ConsoleColor.DarkCyan;
            Console.Write($"{time} ");

            // Category brackets
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.Write("[");
            Console.ForegroundColor = catColor;
            Console.Write(catPadded);
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.Write("]  ");

            // Message text
            Console.ForegroundColor = ConsoleColor.White;
            Console.WriteLine(msg);

            // Exception
            if (logEvent.Exception != null)
            {
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.Write("  │ ");
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"       ↳ EXCEPTION: {logEvent.Exception.Message}");
            }

            Console.ResetColor();
        }
    }

    private static (string Category, ConsoleColor Color) DetermineCategoryAndColor(LogEventLevel level, string source, string message)
    {
        if (level is LogEventLevel.Error or LogEventLevel.Fatal)
            return ("ERROR", ConsoleColor.Red);

        if (level is LogEventLevel.Warning)
            return ("WARN", ConsoleColor.Yellow);

        if (source.Contains("SocketServer", StringComparison.OrdinalIgnoreCase))
        {
            if (message.Contains("Accepting", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("Starting", StringComparison.OrdinalIgnoreCase))
                return ("READY", ConsoleColor.Green);

            if (message.Contains("disconnected", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("connected", StringComparison.OrdinalIgnoreCase))
                return ("NETWORK", ConsoleColor.Magenta);

            if (message.Contains("rate-limit", StringComparison.OrdinalIgnoreCase))
                return ("SECURITY", ConsoleColor.Yellow);

            return ("NETWORK", ConsoleColor.Magenta);
        }

        if (source.Contains("LoginService", StringComparison.OrdinalIgnoreCase) ||
            source.Contains("LoginPacketHandler", StringComparison.OrdinalIgnoreCase) ||
            source.Contains("Account", StringComparison.OrdinalIgnoreCase))
        {
            if (message.Contains("Auto-created", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("logged in", StringComparison.OrdinalIgnoreCase))
                return ("AUTH/OK", ConsoleColor.Green);

            return ("AUTH", ConsoleColor.Cyan);
        }

        if (source.Contains("GameServerBootstrapper", StringComparison.OrdinalIgnoreCase))
        {
            if (message.Contains("NPC", StringComparison.OrdinalIgnoreCase))
                return ("NPC/SPAWN", ConsoleColor.Green);

            if (message.Contains("map", StringComparison.OrdinalIgnoreCase))
                return ("MAP/ZONE", ConsoleColor.DarkYellow);

            if (message.Contains("clan", StringComparison.OrdinalIgnoreCase))
                return ("CLAN", ConsoleColor.Yellow);

            return ("BOOT", ConsoleColor.Cyan);
        }

        if (source.Contains("MapManager", StringComparison.OrdinalIgnoreCase))
            return ("MAP/ZONE", ConsoleColor.DarkYellow);

        if (source.Contains("GameData", StringComparison.OrdinalIgnoreCase))
            return ("DATABASE", ConsoleColor.DarkYellow);

        if (source.Contains("UserSession", StringComparison.OrdinalIgnoreCase) ||
            source.Contains("Player", StringComparison.OrdinalIgnoreCase))
            return ("PLAYER", ConsoleColor.Cyan);

        if (level is LogEventLevel.Information)
            return ("INFO", ConsoleColor.Cyan);

        return ("DEBUG", ConsoleColor.DarkGray);
    }
}
