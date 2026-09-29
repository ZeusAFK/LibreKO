using System.Net;
using System.Net.Sockets;
using System.Text;

namespace LibreKO.Game.Protocol;

public static class BotSimControl
{
    private const int TimeoutMs = 3000;

    public const string NotRunning = "BotSim is not running on this host";

    public static async Task<string> SendAsync(int port, string command, CancellationToken ct = default)
    {
        using var client = new TcpClient();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeoutMs);
        try
        {
            await client.ConnectAsync(IPAddress.Loopback, port, timeout.Token);
            var stream = client.GetStream();
            await stream.WriteAsync(Encoding.UTF8.GetBytes(command + "\n"), timeout.Token);
            using var reader = new StreamReader(stream, Encoding.UTF8);
            string? reply = await reader.ReadLineAsync(timeout.Token);
            return string.IsNullOrWhiteSpace(reply) ? "BotSim sent no reply" : reply.Trim();
        }
        catch (SocketException)
        {
            return NotRunning;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return "BotSim did not answer in time";
        }
        catch (IOException)
        {
            return "BotSim closed the connection";
        }
    }
}
