using System.Collections.ObjectModel;
using System.Net.NetworkInformation;

namespace PortPingTool.Services;

public enum PingIntervalMode
{
    Standard1000,
    Fast100,
}

public sealed class PingService : IDisposable
{
    private CancellationTokenSource? _cts;
    private Task? _runner;
    private readonly object _lock = new();

    public PingIntervalMode IntervalMode { get; set; } = PingIntervalMode.Standard1000;
    public int IntervalMs { get; set; } = 1000;

    public string Host { get; private set; } = "127.0.0.1";
    /// <summary>The first IP address the host resolved to. Cached so we don't re-resolve every probe.</summary>
    public string? ResolvedIp { get; private set; }
    public bool IsRunning { get; private set; }

    public ObservableCollection<PingRecord> Results { get; } = new();

    /// <summary>Subset of Results containing only failed (lost) packets, capped at 1000.</summary>
    public ObservableCollection<PingRecord> LostResults { get; } = new();

    public PingStatistics Stats { get; } = new();

    public event Action<PingRecord>? ResultArrived;
    public event Action<bool>? StateChanged;

    public async Task StartAsync(string host, int? count = null)
    {
        if (IsRunning) return;
        Host = host;
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
        // Note: do NOT clear results here — the user wants to see stats
        // after stopping. Results are cleared on the next Start.
        StateChanged?.Invoke(false);
    }

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

    private async Task<PingRecord> SendOneAsync(CancellationToken ct)
    {
        try
        {
            using var ping = new Ping();
            var reply = await ping.SendPingAsync(Host, 2000).ConfigureAwait(false);
            return new PingRecord
            {
                Seq = Stats.Sent + 1,
                Timestamp = DateTime.Now,
                Success = reply.Status == IPStatus.Success,
                LatencyMs = reply.Status == IPStatus.Success ? (long)reply.RoundtripTime : 0,
                Status = reply.Status.ToString(),
                ResolvedIp = ResolvedIp ?? "",
            };
        }
        catch (PingException ex)
        {
            return new PingRecord
            {
                Seq = Stats.Sent + 1,
                Timestamp = DateTime.Now,
                Success = false,
                LatencyMs = 0,
                Status = ex.InnerException?.Message ?? ex.Message,
                ResolvedIp = ResolvedIp ?? "",
            };
        }
        catch (Exception ex)
        {
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
}

public sealed class PingRecord
{
    public int Seq { get; init; }
    public DateTime Timestamp { get; init; }
    public bool Success { get; init; }
    public long LatencyMs { get; init; }
    public string Status { get; init; } = string.Empty;
    /// <summary>The IP that the hostname resolved to (if any). Empty if already an IP literal or DNS failed.</summary>
    public string ResolvedIp { get; init; } = "";

    public string TimeDisplay => Timestamp.ToString("HH:mm:ss.fff");
    public string IpTag => string.IsNullOrEmpty(ResolvedIp) ? "" : $" ({ResolvedIp})";
    public string Display => Success
        ? $"[{TimeDisplay}] seq={Seq}  time={LatencyMs} ms{IpTag}"
        : $"[{TimeDisplay}] seq={Seq}  *  timeout ({Status}){IpTag}";

    // For XAML: pick color based on success
    public string RowColor => Success ? "#6E6E73" : "#FF3B30";
}

public sealed class PingStatistics
{
    public int Sent { get; internal set; }
    public int Received { get; internal set; }
    public int Lost { get; internal set; }
    public long MinLatency { get; internal set; }
    public long MaxLatency { get; internal set; }
    public long SumLatency { get; internal set; }

    public double LossRate => Sent == 0 ? 0 : (double)Lost / Sent * 100.0;
    public double AvgLatency => Received == 0 ? 0 : (double)SumLatency / Received;

    public void Reset()
    {
        Sent = Received = Lost = 0;
        MinLatency = MaxLatency = SumLatency = 0;
    }
}
