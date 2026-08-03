using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using PortPingTool.Services;

namespace PortPingTool;

public partial class MainWindow : Window
{
    private readonly ObservableCollection<PortListenerService> _listeners = new();
    private readonly ObservableCollection<ConnectionRecord> _connectionLog = new();
    private readonly PingService _ping = new();

    public MainWindow()
    {
        InitializeComponent();
        ListenerList.ItemsSource = _listeners;
        ConnectionLog.ItemsSource = _connectionLog;
        PingResults.ItemsSource = _ping.Results;

        _ping.ResultArrived += OnPingResult;
        _ping.StateChanged += OnPingStateChanged;

        CountFixed.IsCheckedChanged   += (_, _) => { if (CountFixed.IsChecked == true) CountBox.IsEnabled = true; };
        CountContinuous.IsCheckedChanged += (_, _) => { if (CountContinuous.IsChecked == true) CountBox.IsEnabled = false; };
    }

    // ========================= Listener =========================

    private void OnAddListenerClick(object? sender, RoutedEventArgs e)
    {
        if (!int.TryParse(PortInputBox.Text?.Trim(), out var port) || port <= 0 || port > 65535)
        {
            AppendLog($"[错误] 端口无效: {PortInputBox.Text}");
            return;
        }
        if (_listeners.Any(l => l.Port == port))
        {
            AppendLog($"[提示] 端口 {port} 已在列表中");
            return;
        }

        var svc = new PortListenerService(port);
        svc.ConnectionArrived += record =>
        {
            Dispatcher.UIThread.InvokeAsync(() =>
            {
                _connectionLog.Insert(0, record);
                while (_connectionLog.Count > 500) _connectionLog.RemoveAt(_connectionLog.Count - 1);
                AppendLog($"[连接] 端口 {port} 来自 {record.RemoteEndPoint}");
            });
        };
        svc.ErrorOccurred += msg => Dispatcher.UIThread.InvokeAsync(() => AppendLog($"[错误] {msg}"));

        _listeners.Add(svc);
        PortInputBox.Text = (port + 1).ToString();
    }

    private async void OnToggleListenerClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not PortListenerService svc) return;
        try
        {
            if (svc.IsListening)
            {
                svc.Stop();
                AppendLog($"[停止] 端口 {svc.Port} 已停止");
            }
            else
            {
                await svc.StartAsync();
                AppendLog($"[启动] 端口 {svc.Port} 正在监听");
            }
        }
        catch (Exception ex)
        {
            AppendLog($"[错误] {ex.Message}");
        }
    }

    private void OnRemoveListenerClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not PortListenerService svc) return;
        if (svc.IsListening)
        {
            AppendLog($"[提示] 端口 {svc.Port} 正在监听,先停止再删除");
            return;
        }
        _listeners.Remove(svc);
        AppendLog($"[删除] 端口 {svc.Port} 已从列表移除");
    }

    // ========================= Tester =========================

    private async void OnTestPortClick(object? sender, RoutedEventArgs e)
    {
        var host = TestHostBox.Text?.Trim() ?? "";
        if (!int.TryParse(TestPortBox.Text?.Trim(), out var port) || port <= 0 || port > 65535)
        {
            TestResultText.Text = "端口无效";
            TestResultText.Foreground = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#FF3B30"));
            return;
        }
        if (string.IsNullOrWhiteSpace(host))
        {
            TestResultText.Text = "主机不能为空";
            TestResultText.Foreground = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#FF3B30"));
            return;
        }

        TestResultText.Text = "测试中…";
        TestResultText.Foreground = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#6E6E73"));
        TestPortClickBtn.IsEnabled = false;
        try
        {
            var r = await PortTesterService.TestAsync(host, port);
            TestResultText.Text = r.IsOpen
                ? $"OPEN · {r.LatencyMs} ms"
                : $"CLOSED · {r.Detail}";
            var color = r.IsOpen ? "#34C759" : "#FF3B30";
            TestResultText.Foreground = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse(color));
        }
        finally
        {
            TestPortClickBtn.IsEnabled = true;
        }
    }

    // ========================= Ping =========================

    private void OnIntervalChanged(object? sender, RoutedEventArgs e)
    {
        if (_ping.IsRunning) return;
        _ping.IntervalMode = Interval1000.IsChecked == true
            ? PingIntervalMode.Standard1000
            : PingIntervalMode.Fast100;
    }

    private async void OnPingToggleClick(object? sender, RoutedEventArgs e)
    {
        if (_ping.IsRunning)
        {
            _ping.Stop();
            return;
        }
        var host = PingHostBox.Text?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(host))
        {
            AppendLog("[错误] Ping 主机不能为空");
            return;
        }
        int? count = null;
        if (CountFixed.IsChecked == true)
        {
            if (!int.TryParse(CountBox.Text, out var n) || n <= 0)
            {
                AppendLog("[错误] 自定义次数必须为正整数");
                return;
            }
            count = n;
        }
        _ping.IntervalMode = Interval1000.IsChecked == true
            ? PingIntervalMode.Standard1000
            : PingIntervalMode.Fast100;
        _ping.Results.Clear();
        await _ping.StartAsync(host, count);
    }

    private void OnPingResult(PingRecord r)
    {
        Dispatcher.UIThread.InvokeAsync(() =>
        {
            PingResultScroller.ScrollToEnd();
            UpdateStats();
        });
    }

    private void OnPingStateChanged(bool running)
    {
        Dispatcher.UIThread.InvokeAsync(() =>
        {
            PingToggleBtn.Content = running ? "停止" : "开始";
            if (running) UpdateStats();
        });
    }

    private void UpdateStats()
    {
        var s = _ping.Stats;
        StatSent.Text = s.Sent.ToString();
        StatRecv.Text = s.Received.ToString();
        StatLoss.Text = $"{s.LossRate:F1}%";
        StatAvg.Text  = $"{s.AvgLatency:F0} ms";
    }

    // ========================= Misc =========================

    private void AppendLog(string message)
    {
        System.Diagnostics.Debug.WriteLine($"{DateTime.Now:HH:mm:ss} {message}");
    }
}
