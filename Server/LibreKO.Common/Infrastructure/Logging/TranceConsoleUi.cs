using System.Text;

namespace LibreKO.Common.Infrastructure.Logging;

public static class TranceConsoleUi
{
    private static readonly Lock LogLock = new();

    public static void Initialize(string windowTitle, int width = 110, int height = 38)
    {
        try
        {
            Console.OutputEncoding = Encoding.UTF8;
            Console.InputEncoding = Encoding.UTF8;
            Console.Title = windowTitle;

            if (OperatingSystem.IsWindows())
            {
                try
                {
                    var targetWidth = Math.Min(width, Console.LargestWindowWidth > 0 ? Console.LargestWindowWidth : width);
                    var targetHeight = Math.Min(height, Console.LargestWindowHeight > 0 ? Console.LargestWindowHeight : height);
                    if (targetWidth > 0 && targetHeight > 0)
                    {
                        Console.SetWindowSize(targetWidth, targetHeight);
                    }
                }
                catch
                {
                    // Ignore window size resize limits if display resolution differs
                }
            }
        }
        catch
        {
            // Ignore if console handle is redirected
        }
    }

    public static void PrintBanner(string serverType, string version = "v2.6.19", string author = "By Zeus x Design by Ahmad")
    {
        lock (LogLock)
        {
            Console.WriteLine();

            // Top Border (86 double horizontal lines, 90 chars total)
            Console.ForegroundColor = ConsoleColor.DarkCyan;
            Console.WriteLine("  ╔══════════════════════════════════════════════════════════════════════════════════════════╗");

            // Row 1
            Console.Write("  ║                   ");
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.Write("██╗       ██╗    ██████╗ ");
            Console.ForegroundColor = ConsoleColor.Magenta;
            Console.Write("    ██████╗    ███████╗");
            Console.ForegroundColor = ConsoleColor.DarkCyan;
            Console.WriteLine("                   ║");

            // Row 2
            Console.Write("  ║                   ");
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.Write("██║       ██║    ██╔══██╗");
            Console.ForegroundColor = ConsoleColor.Magenta;
            Console.Write("    ██╔══██╗   ██╔════╝");
            Console.ForegroundColor = ConsoleColor.DarkCyan;
            Console.WriteLine("                   ║");

            // Row 3
            Console.Write("  ║                   ");
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.Write("██║       ██║    ██████╔╝");
            Console.ForegroundColor = ConsoleColor.Magenta;
            Console.Write("    ██████╔╝   █████╗  ");
            Console.ForegroundColor = ConsoleColor.DarkCyan;
            Console.WriteLine("                   ║");

            // Row 4
            Console.Write("  ║                   ");
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.Write("██║       ██║    ██╔══██╗");
            Console.ForegroundColor = ConsoleColor.Magenta;
            Console.Write("    ██╔══██╗   ██╔══╝  ");
            Console.ForegroundColor = ConsoleColor.DarkCyan;
            Console.WriteLine("                   ║");

            // Row 5
            Console.Write("  ║                   ");
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.Write("███████╗  ██║    ██████╔╝");
            Console.ForegroundColor = ConsoleColor.Magenta;
            Console.Write("    ██║  ██║   ███████╗");
            Console.ForegroundColor = ConsoleColor.DarkCyan;
            Console.WriteLine("                   ║");

            // Row 6
            Console.Write("  ║                   ");
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.Write("╚══════╝  ╚═╝    ╚═════╝ ");
            Console.ForegroundColor = ConsoleColor.Magenta;
            Console.Write("    ╚═╝  ╚═╝   ╚══════╝");
            Console.ForegroundColor = ConsoleColor.DarkCyan;
            Console.WriteLine("                   ║");

            // Empty line
            Console.ForegroundColor = ConsoleColor.DarkCyan;
            Console.WriteLine("  ║                                                                                          ║");

            // Subtitle Line
            var rawSub = $"⚡ {serverType}  •  {version}  •  {author} ⚡";
            var totalInner = 86;
            var padLeft = Math.Max(0, (totalInner - rawSub.Length) / 2);
            var padRight = Math.Max(0, totalInner - padLeft - rawSub.Length);

            Console.Write("  ║" + new string(' ', padLeft));
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.Write($"⚡ {serverType}");
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.Write("  •  ");
            Console.ForegroundColor = ConsoleColor.Green;
            Console.Write(version);
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.Write("  •  ");
            Console.ForegroundColor = ConsoleColor.Magenta;
            Console.Write(author);
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.Write(" ⚡");
            Console.ForegroundColor = ConsoleColor.DarkCyan;
            Console.WriteLine(new string(' ', padRight) + "║");

            // Bottom border
            Console.WriteLine("  ╚══════════════════════════════════════════════════════════════════════════════════════════╝");
            Console.ResetColor();
            Console.WriteLine();
        }
    }

    public static void PrintDualCards(
        string title1,
        IReadOnlyList<(string Key, string Value)> items1,
        string title2,
        IReadOnlyList<(string Key, string Value)> items2,
        int cardWidth = 42)
    {
        lock (LogLock)
        {
            var maxRows = Math.Max(items1.Count, items2.Count);

            // Header Border
            var top1 = $"  ┌── [ {title1} ] ";
            var pad1 = cardWidth - top1.Length + 2;
            if (pad1 > 0) top1 += new string('─', pad1);
            top1 += "┐";

            var top2 = $"┌── [ {title2} ] ";
            var pad2 = cardWidth - top2.Length;
            if (pad2 > 0) top2 += new string('─', pad2);
            top2 += "┐";

            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine($"{top1}  {top2}");

            for (var i = 0; i < maxRows; i++)
            {
                // Card 1
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.Write("  │ ");
                if (i < items1.Count)
                {
                    Console.ForegroundColor = ConsoleColor.Cyan;
                    Console.Write("• ");
                    Console.ForegroundColor = ConsoleColor.Gray;
                    Console.Write($"{items1[i].Key}: ");
                    Console.ForegroundColor = ConsoleColor.White;
                    Console.Write(items1[i].Value);
                    var len = 2 + items1[i].Key.Length + 2 + items1[i].Value.Length;
                    var fill = Math.Max(0, cardWidth - 3 - len);
                    Console.Write(new string(' ', fill));
                }
                else
                {
                    Console.Write(new string(' ', cardWidth - 3));
                }
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.Write("│  │ ");

                // Card 2
                if (i < items2.Count)
                {
                    Console.ForegroundColor = ConsoleColor.Magenta;
                    Console.Write("• ");
                    Console.ForegroundColor = ConsoleColor.Gray;
                    Console.Write($"{items2[i].Key}: ");
                    Console.ForegroundColor = ConsoleColor.White;
                    Console.Write(items2[i].Value);
                    var len = 2 + items2[i].Key.Length + 2 + items2[i].Value.Length;
                    var fill = Math.Max(0, cardWidth - 3 - len);
                    Console.Write(new string(' ', fill));
                }
                else
                {
                    Console.Write(new string(' ', cardWidth - 3));
                }
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.WriteLine("│");
            }

            var bot1 = $"  └{new string('─', cardWidth - 2)}┘";
            var bot2 = $"└{new string('─', cardWidth - 2)}┘";
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine($"{bot1}  {bot2}");
            Console.ResetColor();
            Console.WriteLine();
        }
    }

    public static void PrintSectionHeader(string title, int totalWidth = 88)
    {
        lock (LogLock)
        {
            var bar = $"  ┌── [ {title} ] ";
            var fill = totalWidth - bar.Length;
            if (fill > 0) bar += new string('─', fill);
            bar += "┐";
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine(bar);
            Console.ResetColor();
        }
    }

    public static void PrintServerReadyBadge(
        string serverType,
        IReadOnlyList<(string Label, string Value)> details)
    {
        lock (LogLock)
        {
            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("  ╔══════════════════════════════════════════════════════════════════════════════════════════╗");

            // Header line
            var headerText = $"★★★  LIBRE {serverType.ToUpperInvariant()} ONLINE & READY  ★★★";
            var padL = Math.Max(0, (86 - headerText.Length) / 2);
            var padR = Math.Max(0, 86 - headerText.Length - padL);

            Console.Write("  ║" + new string(' ', padL));
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.Write("★★★  ");
            Console.ForegroundColor = ConsoleColor.White;
            Console.Write($"LIBRE {serverType.ToUpperInvariant()} ONLINE & READY");
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.Write("  ★★★");
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine(new string(' ', padR) + "║");

            // Divider
            Console.WriteLine("  ╠══════════════════════════════════════════════════════════════════════════════════════════╣");

            // Details rows
            foreach (var (label, value) in details)
            {
                Console.Write("  ║  ");
                Console.ForegroundColor = ConsoleColor.Green;
                Console.Write("✔ ");
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.Write($"{label}: ");
                Console.ForegroundColor = ConsoleColor.White;
                Console.Write(value);

                var used = 2 + 2 + label.Length + 2 + value.Length;
                var fill = Math.Max(0, 86 - used);
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine(new string(' ', fill) + "║");
            }

            // Bottom border
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("  ╚══════════════════════════════════════════════════════════════════════════════════════════╝");
            Console.ResetColor();
            Console.WriteLine();
        }
    }

    public static void PrintStatusLine(string category, ConsoleColor color, string message)
    {
        lock (LogLock)
        {
            var time = DateTime.Now.ToString("HH:mm:ss");
            var catPadded = category.Length > 9 ? category[..9] : category.PadRight(9);

            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.Write("  │ ");
            Console.ForegroundColor = ConsoleColor.DarkCyan;
            Console.Write($"{time} ");
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.Write("[");
            Console.ForegroundColor = color;
            Console.Write(catPadded);
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.Write("]  ");
            Console.ForegroundColor = ConsoleColor.Gray;
            Console.WriteLine(message);
            Console.ResetColor();
        }
    }
}
