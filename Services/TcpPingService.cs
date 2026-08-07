using System.Collections.ObjectModel;
using System.Net.Sockets;

namespace PortPingTool.Services;

/// <summary>
/// TCP handshake "ping" — opens a TCP connection to a (host, port) target
/// at a configurable interval, records latency / success / failure.
/// Unlike ICMP, this CAN run at 1ms intervals because each iteration
/// is just a system-level TCP connect (no kernel ICMP throttling).
///
/// Only useful for targets that have an open TCP port (e.g. 1.1.1.1:443).
/// </summary>
public sealed class TcpPingService : IDisposable
{
    private CancellationTokenSource? _cts;
    private Task? _runner;
    private readonly object _lock = new();

    public int IntervalMs { get; set; } = 1000;
    public string Host { get; private set; } = "127.0.0.1";
    public int Port { get; private set; } = 443;
    /// <summary>The first IP the host resolved to. Cached.</summary>
    public string? ResolvedIp { get; private set; }
    public bool IsRunning { get; private set; }

    public ObservableCollection<PingRecord> Results { get; } = new();
    public ObservableCollection<PingRecord> LostResults { get; } = new();
    public PingStatistics Stats { get; } = new();

    public event Action<PingRecord>? ResultArrived;
    public event Action<bool>? StateChanged;

    public async Task StartAsync(string host, int port, int? count = null)
    {
        if (IsRunning) return;
        Host = host;
        Port = port;
        ResolvedIp = await ResolveFirstAsync(host).ConfigureAwait(false);
        ResetStats();

        _cts = new CancellationTokenSource();
        IsRunning = true;
        StateChanged?.Invoke(true);
        var token = _cts.Token;

        _runner = Task.Run(async () =>
        {
            int sent = 0;
            while (!token.IsCancellationRequested)
            {
                if (count.HasValue && sent >= count.Value) break;
                sent++;

                var record = await SendOneAsync(token).ConfigureAwait(false);
                UpdateStats(record);
                ResultArrived?.Invoke(record);

                try { await Task.Delay(IntervalMs, token).ConfigureAwait(false); }
                catch (OperationCanceledException) { break; }
            }

            IsRunning = false;
            StateChanged?.Invoke(false);
        }, token);
        await Task.CompletedTask;
    }

    public void Stop()
    {
        try
        {
            _cts?.Cancel();
            _cts?.Dispose();
        }
        catch { /* swallow */ }
        _cts = null;
        IsRunning = false;
        // Note: do NOT clear results here — user wants to see stats after stop.
        // Results are cleared on the next Start.
        StateChanged?.Invoke(false);
    }

    private async Task<PingRecord> SendOneAsync(CancellationToken ct)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            using var client = new TcpClient();
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            // Hard upper bound for the connect attempt; 5s is plenty.
            linkedCts.CancelAfter(5000);
            await client.ConnectAsync(Host, Port, linkedCts.Token).ConfigureAwait(false);
            sw.Stop();
            return new PingRecord
            {
                Seq = Stats.Sent + 1,
                Timestamp = DateTime.Now,
                Success = true,
                LatencyMs = sw.ElapsedMilliseconds,
                Status = "Connected",
                ResolvedIp = ResolvedIp ?? "",
            };
        }
        catch (OperationCanceledException)
        {
            sw.Stop();
            return new PingRecord
            {
                Seq = Stats.Sent + 1,
                Timestamp = DateTime.Now,
                Success = false,
                LatencyMs = 0,
                Status = "TimedOut",
                ResolvedIp = ResolvedIp ?? "",
            };
        }
        catch (SocketException ex)
        {
            sw.Stop();
            return new PingRecord
            {
                Seq = Stats.Sent + 1,
                Timestamp = DateTime.Now,
                Success = false,
                LatencyMs = 0,
                Status = ex.SocketErrorCode.ToString(),
                ResolvedIp = ResolvedIp ?? "",
            };
        }
        catch (Exception ex)
        {
            sw.Stop();
            return new PingRecord
            {
                Seq = Stats.Sent + 1,
                Timestamp = DateTime.Now,
                Success = false,
                LatencyMs = 0,
                Status = ex.Message,
                ResolvedIp = ResolvedIp ?? "",
            };
        }
    }

    private void ResetStats()
    {
        lock (_lock)
        {
            Stats.Reset();
            Results.Clear();
            LostResults.Clear();
        }
    }

    private void UpdateStats(PingRecord r)
    {
        lock (_lock)
        {
            Stats.Sent++;
            if (r.Success)
            {
                Stats.Received++;
                Stats.SumLatency += r.LatencyMs;
                if (Stats.MinLatency == 0 || r.LatencyMs < Stats.MinLatency) Stats.MinLatency = r.LatencyMs;
                if (r.LatencyMs > Stats.MaxLatency) Stats.MaxLatency = r.LatencyMs;
            }
            else
            {
                Stats.Lost++;
            }
        }
    }

    public void Dispose() => Stop();

    private static async Task<string?> ResolveFirstAsync(string host)
    {
        if (System.Net.IPAddress.TryParse(host, out _)) return host;
        try
        {
            var addrs = await System.Net.Dns.GetHostAddressesAsync(host).ConfigureAwait(false);
            var first = addrs.FirstOrDefault(a => a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork);
            return first?.ToString() ?? addrs.FirstOrDefault()?.ToString();
        }
        catch
        {
            return null;
        }
    }
}
