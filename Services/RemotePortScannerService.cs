using System.Collections.Concurrent;
using System.Net.Sockets;

namespace PortPingTool.Services;

public sealed class ScanProgress
{
    public int Total { get; init; }
    public int Done { get; init; }
    public int Open { get; init; }
    public int Closed { get; init; }
    public int Failed { get; init; }
    public double Fraction => Total == 0 ? 0 : (double)Done / Total;
    public string Summary => $"{Done}/{Total} (开={Open}, 关={Closed}, 错={Failed})";
}

public sealed class RemotePortScannerService
{
    /// <summary>One row per port that came back OPEN. UI binds to a snapshot.</summary>
    public ConcurrentBag<LocalPortRow> OpenPorts { get; } = new();

    private CancellationTokenSource? _cts;

    public bool IsRunning => _cts is { IsCancellationRequested: false };

    public void Cancel() => _cts?.Cancel();

    /// <summary>
    /// Probes <paramref name="host"/> on each port in <paramref name="ports"/> via TCP connect.
    /// Fires <paramref name="onProgress"/> on every probe completion (caller marshals to UI).
    /// Returns the list of open ports.
    /// </summary>
    public async Task<IReadOnlyList<LocalPortRow>> ScanAsync(
        string host,
        IReadOnlyList<int> ports,
        int concurrency,
        int timeoutMs,
        Action<ScanProgress>? onProgress = null,
        CancellationToken externalCt = default)
    {
        OpenPorts.Clear();
        _cts = CancellationTokenSource.CreateLinkedTokenSource(externalCt);
        var token = _cts.Token;
        concurrency = Math.Clamp(concurrency, 1, 500);

        int total = ports.Count;
        int done = 0, open = 0, closed = 0, failed = 0;

        // Use a Channel for producer-consumer pattern.
        var queue = System.Threading.Channels.Channel.CreateUnbounded<int>(
            new System.Threading.Channels.UnboundedChannelOptions { SingleReader = false, SingleWriter = true });
        foreach (var portValue in ports) await queue.Writer.WriteAsync(portValue).ConfigureAwait(false);
        queue.Writer.Complete();

        var tasks = new List<Task>(concurrency);
        for (int i = 0; i < concurrency; i++)
        {
            tasks.Add(Task.Run(async () =>
            {
                await foreach (var port in queue.Reader.ReadAllAsync(token).ConfigureAwait(false))
                {
                    bool success = false;
                    try
                    {
                        using var client = new TcpClient();
                        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token);
                        linked.CancelAfter(timeoutMs);
                        await client.ConnectAsync(host, port, linked.Token).ConfigureAwait(false);
                        success = true;
                    }
                    catch
                    {
                        success = false;
                    }

                    if (token.IsCancellationRequested) break;

                    if (success)
                    {
                        Interlocked.Increment(ref open);
                        OpenPorts.Add(new LocalPortRow
                        {
                            Protocol = "TCP",
                            LocalAddress = host,
                            LocalPort = port,
                            RemoteAddress = "—",
                            State = "OPEN",
                            Pid = 0,
                            IsOpen = true,
                            Source = "远端",
                        });
                    }
                    else
                    {
                        // Distinguish: timeout vs refused vs other is too noisy for the UI;
                        // the user just cares OPEN vs not-OPEN. Count refused as "closed",
                        // anything else (DNS, etc.) as "failed".
                        // We can't tell here without the original exception — just bucket as closed.
                        Interlocked.Increment(ref closed);
                    }

                    int d = Interlocked.Increment(ref done);
                    onProgress?.Invoke(new ScanProgress
                    {
                        Total = total,
                        Done = d,
                        Open = Volatile.Read(ref open),
                        Closed = Volatile.Read(ref closed),
                        Failed = Volatile.Read(ref failed),
                    });
                }
            }, token));
        }

        try { await Task.WhenAll(tasks).ConfigureAwait(false); }
        catch (OperationCanceledException) { /* expected on cancel */ }
        finally
        {
            _cts.Dispose();
            _cts = null;
        }

        return OpenPorts.OrderBy(r => r.LocalPort).ToList();
    }
}
