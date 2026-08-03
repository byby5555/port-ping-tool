using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;

namespace PortPingTool.Services;

/// <summary>
/// One TCP listener instance. Tracks every accepted connection so the user
/// can see who hit their port (essential for network ops debugging).
/// </summary>
public sealed class PortListenerService : IDisposable, INotifyPropertyChanged
{
    private int _port;
    public int Port
    {
        get => _port;
        set
        {
            if (_port == value) return;
            if (IsListening)
            {
                // Refuse to change port while listening — caller should stop first.
                OnPropertyChanged();
                return;
            }
            _port = value;
            OnPropertyChanged();
        }
    }

    public DateTime StartedAt { get; private set; }
    public bool IsListening => _listener is not null && _isRunning;

    // Status indicator color (exposed for XAML binding; avoids MultiBinding)
    public string StatusDotColor => IsListening ? "#34C759" : "#8E8E93";

    // Status text (运行中 / 已停止)
    public string StatusText => IsListening ? "● 运行中" : "○ 已停止";

    // Toggle button label: 启动 / 停止
    public string ToggleButtonText => IsListening ? "停止" : "启动";

    // Toggle button background (red when listening, blue otherwise)
    public string ToggleButtonColor => IsListening ? "#FF3B30" : "#007AFF";

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    private TcpListener? _listener;
    private CancellationTokenSource? _cts;
    private bool _isRunning;

    /// <summary>Live, thread-safe log of incoming connections (most recent first).</summary>
    public ObservableCollection<ConnectionRecord> Connections { get; } = new();

    private readonly ConcurrentBag<ConnectionRecord> _all = new();
    public int TotalConnections => _all.Count;

    public event Action<ConnectionRecord>? ConnectionArrived;
    public event Action<string>? ErrorOccurred;

    public PortListenerService(int port) => _port = port;

    public Task StartAsync()
    {
        if (_isRunning) return Task.CompletedTask;
        try
        {
            _listener = new TcpListener(IPAddress.Any, Port);
            _listener.Start();
            _isRunning = true;
            StartedAt = DateTime.Now;
            _cts = new CancellationTokenSource();

            _ = Task.Run(() => AcceptLoopAsync(_cts.Token));

            OnPropertyChanged(nameof(IsListening));
            OnPropertyChanged(nameof(StatusDotColor));
            OnPropertyChanged(nameof(StatusText));
            OnPropertyChanged(nameof(ToggleButtonText));
            OnPropertyChanged(nameof(ToggleButtonColor));
            return Task.CompletedTask;
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
            OnPropertyChanged(nameof(IsListening));
            OnPropertyChanged(nameof(StatusDotColor));
            OnPropertyChanged(nameof(StatusText));
            OnPropertyChanged(nameof(ToggleButtonText));
            OnPropertyChanged(nameof(ToggleButtonColor));
        }
    }

    public void Dispose() => Stop();
}

public sealed class ConnectionRecord
{
    public DateTime Timestamp { get; init; }
    public string RemoteEndPoint { get; init; } = string.Empty;
    public string TimestampDisplay => Timestamp.ToString("HH:mm:ss.fff");
    public string Display => $"[{TimestampDisplay}]  ←  {RemoteEndPoint}";
}
