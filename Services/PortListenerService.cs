using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Net;
using System.Net.Sockets;

namespace PortPingTool.Services;

/// <summary>
/// One TCP listener instance. Tracks every accepted connection so the user
/// can see who hit their port (essential for network ops debugging).
/// </summary>
public sealed class PortListenerService : IDisposable
{
    public int Port { get; }
    public DateTime StartedAt { get; private set; }
    public bool IsListening => _listener is not null && _isRunning;

    private TcpListener? _listener;
    private CancellationTokenSource? _cts;
    private bool _isRunning;

    /// <summary>Live, thread-safe log of incoming connections.</summary>
    public ObservableCollection<ConnectionRecord> Connections { get; } = new();

    private readonly ConcurrentBag<ConnectionRecord> _all = new();
    public int TotalConnections => _all.Count;

    public event Action<ConnectionRecord>? ConnectionArrived;
    public event Action<string>? ErrorOccurred;

    public PortListenerService(int port) => Port = port;

    public async Task StartAsync()
    {
        if (_isRunning) return;
        try
        {
            _listener = new TcpListener(IPAddress.Any, Port);
            _listener.Start();
            _isRunning = true;
            StartedAt = DateTime.Now;
            _cts = new CancellationTokenSource();

            // Accept loop runs on background; UI updates marshalled through Dispatcher by caller.
            _ = Task.Run(() => AcceptLoopAsync(_cts.Token));
        }
        catch (Exception ex)
        {
            _isRunning = false;
            ErrorOccurred?.Invoke($"Failed to start listener on {Port}: {ex.Message}");
            throw;
        }
    }

    private async Task AcceptLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && _listener is not null)
        {
            try
            {
                var client = await _listener.AcceptTcpClientAsync(ct).ConfigureAwait(false);
                var record = new ConnectionRecord
                {
                    Timestamp = DateTime.Now,
                    RemoteEndPoint = client.Client.RemoteEndPoint?.ToString() ?? "unknown",
                };
                _all.Add(record);
                ConnectionArrived?.Invoke(record);
                try { client.Close(); } catch { /* ignore */ }
            }
            catch (OperationCanceledException) { break; }
            catch (ObjectDisposedException) { break; }
            catch (Exception ex)
            {
                ErrorOccurred?.Invoke($"Accept error on {Port}: {ex.Message}");
            }
        }
    }

    public void Stop()
    {
        if (!_isRunning) return;
        try
        {
            _cts?.Cancel();
            _listener?.Stop();
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke($"Error stopping listener on {Port}: {ex.Message}");
        }
        finally
        {
            _isRunning = false;
            _listener = null;
            _cts?.Dispose();
            _cts = null;
        }
    }

    public void Dispose()
    {
        Stop();
    }
}

public sealed class ConnectionRecord
{
    public DateTime Timestamp { get; init; }
    public string RemoteEndPoint { get; init; } = string.Empty;
    public string TimestampDisplay => Timestamp.ToString("HH:mm:ss.fff");
    public string Display => $"[{TimestampDisplay}]  ←  {RemoteEndPoint}";
}
