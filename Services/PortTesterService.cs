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
                return new PortTestResult(false, 0, "Host is empty", "");

            // Resolve the host (if it's a hostname) to its first IPv4 so we
            // can show the resolved IP in the result. We pick the IPv4
            // address when available since ICMP/Ping/most tooling prefers
            // IPv4 for clarity.
            IPAddress[] addresses;
            try
            {
                addresses = await Dns.GetHostAddressesAsync(host, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                sw.Stop();
                return new PortTestResult(false, sw.ElapsedMilliseconds, $"DNS resolve failed: {ex.Message}", "");
            }
            if (addresses.Length == 0)
            {
                sw.Stop();
                return new PortTestResult(false, sw.ElapsedMilliseconds, "No DNS records", "");
            }

            var firstV4 = addresses.FirstOrDefault(a => a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork);
            var target = firstV4 ?? addresses[0];
            var resolvedIp = IPAddress.TryParse(host, out _) ? host : target.ToString();

            using var client = new TcpClient { ReceiveTimeout = timeoutMs, SendTimeout = timeoutMs };
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            linkedCts.CancelAfter(timeoutMs);

            await client.ConnectAsync(target, port, linkedCts.Token).ConfigureAwait(false);
            sw.Stop();
            return new PortTestResult(true, sw.ElapsedMilliseconds, $"Connected to {target}", resolvedIp);
        }
        catch (OperationCanceledException)
        {
            sw.Stop();
            // Best-effort: the host might be an IP literal that we never
            // resolved, in which case the caller already knows it.
            var ip = IPAddress.TryParse(host, out _) ? host : "";
            return new PortTestResult(false, sw.ElapsedMilliseconds, $"Timed out after {timeoutMs} ms", ip);
        }
        catch (SocketException ex)
        {
            sw.Stop();
            var ip = IPAddress.TryParse(host, out _) ? host : "";
            return new PortTestResult(false, sw.ElapsedMilliseconds, $"Socket error: {ex.SocketErrorCode} ({ex.Message})", ip);
        }
        catch (Exception ex)
        {
            sw.Stop();
            var ip = IPAddress.TryParse(host, out _) ? host : "";
            return new PortTestResult(false, sw.ElapsedMilliseconds, $"Error: {ex.Message}", ip);
        }
    }
}

public readonly record struct PortTestResult(bool IsOpen, long LatencyMs, string Detail, string ResolvedIp)
{
    public string StatusText => IsOpen ? "OPEN" : "CLOSED";
}
