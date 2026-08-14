using System.Net;
using System.Net.Sockets;

namespace PortPingTool.Services;

public static class PortTesterService
{
    public static async Task<PortTestResult> TestAsync(string host, int port, int timeoutMs = 3000, CancellationToken ct = default)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        if (string.IsNullOrWhiteSpace(host))
            return new PortTestResult(false, 0, "Host is empty", "");

        // Pre-compute the IP literal that the user typed (or empty if it was
        // a hostname) so we can report it back in failure paths without
        // re-parsing the input on every catch.
        var literalIp = IPAddress.TryParse(host, out _) ? host : "";
        var resolvedIp = literalIp;

        try
        {
            IPAddress[] addresses;
            try
            {
                addresses = await Dns.GetHostAddressesAsync(host, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                sw.Stop();
                return new PortTestResult(false, sw.ElapsedMilliseconds, $"DNS resolve failed: {ex.Message}", resolvedIp);
            }
            if (addresses.Length == 0)
            {
                sw.Stop();
                return new PortTestResult(false, sw.ElapsedMilliseconds, "No DNS records", resolvedIp);
            }

            var firstV4 = addresses.FirstOrDefault(a => a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork);
            var target = firstV4 ?? addresses[0];
            if (string.IsNullOrEmpty(resolvedIp)) resolvedIp = target.ToString();

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
            return new PortTestResult(false, sw.ElapsedMilliseconds, $"Timed out after {timeoutMs} ms", resolvedIp);
        }
        catch (SocketException ex)
        {
            sw.Stop();
            return new PortTestResult(false, sw.ElapsedMilliseconds, $"Socket error: {ex.SocketErrorCode} ({ex.Message})", resolvedIp);
        }
        catch (Exception ex)
        {
            sw.Stop();
            return new PortTestResult(false, sw.ElapsedMilliseconds, $"Error: {ex.Message}", resolvedIp);
        }
    }
}

public readonly record struct PortTestResult(bool IsOpen, long LatencyMs, string Detail, string ResolvedIp)
{
    public string StatusText => IsOpen ? "OPEN" : "CLOSED";
}
