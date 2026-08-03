using System.Net;
using System.Net.Sockets;

namespace PortPingTool.Services;

public static class PortTesterService
{
    public static async Task<PortTestResult> TestAsync(string host, int port, int timeoutMs = 3000, CancellationToken ct = default)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            if (string.IsNullOrWhiteSpace(host))
                return new PortTestResult(false, 0, "Host is empty");

            IPAddress[] addresses;
            try
            {
                addresses = await Dns.GetHostAddressesAsync(host, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                sw.Stop();
                return new PortTestResult(false, sw.ElapsedMilliseconds, $"DNS resolve failed: {ex.Message}");
            }
            if (addresses.Length == 0)
            {
                sw.Stop();
                return new PortTestResult(false, sw.ElapsedMilliseconds, "No DNS records");
            }

            using var client = new TcpClient { ReceiveTimeout = timeoutMs, SendTimeout = timeoutMs };
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            linkedCts.CancelAfter(timeoutMs);

            await client.ConnectAsync(addresses[0], port, linkedCts.Token).ConfigureAwait(false);
            sw.Stop();
            return new PortTestResult(true, sw.ElapsedMilliseconds, $"Connected to {addresses[0]}");
        }
        catch (OperationCanceledException)
        {
            sw.Stop();
            return new PortTestResult(false, sw.ElapsedMilliseconds, $"Timed out after {timeoutMs} ms");
        }
        catch (SocketException ex)
        {
            sw.Stop();
            return new PortTestResult(false, sw.ElapsedMilliseconds, $"Socket error: {ex.SocketErrorCode} ({ex.Message})");
        }
        catch (Exception ex)
        {
            sw.Stop();
            return new PortTestResult(false, sw.ElapsedMilliseconds, $"Error: {ex.Message}");
        }
    }
}

public readonly record struct PortTestResult(bool IsOpen, long LatencyMs, string Detail)
{
    public string StatusText => IsOpen ? "OPEN" : "CLOSED";
}
