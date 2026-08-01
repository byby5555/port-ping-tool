using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using PortPingTool.Services;

namespace PortPingTool;

public partial class MainWindow : Window
{
    private readonly ObservableCollection<PortListenerService> _listeners = new();
    private readonly PingService _ping = new();

    public MainWindow()
    {
        InitializeComponent();
        ListenerList.ItemsSource = _listeners;
        ConnectionLog.ItemsSource = new ObservableCollection<ConnectionRecord>();
        PingResults.ItemsSource = _ping.Results;

        _ping.ResultArrived += OnPingResult;
        _ping.StateChanged += OnPingStateChanged;

        CountFixed.Checked  += (_, _) => CountBox.IsEnabled = true;
        CountContinuous.Checked += (_, _) => CountBox.IsEnabled = false;
    }

    // ========================= Listener =========================

    private void OnAddListenerClick(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(PortInputBox.Text.Trim(), out var port) || port <= 0 || port > 65535)
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
            Dispatcher.Invoke(() =>
            {
                ((ObservableCollection<ConnectionRecord>)ConnectionLog.ItemsSource).Insert(0, record);
                // Keep log bounded
                var log = (ObservableCollection<ConnectionRecord>)ConnectionLog.ItemsSource;
                while (log.Count > 500) log.RemoveAt(log.Count - 1);
                AppendLog($"[连接] 端口 {port} 来自 {record.RemoteEndPoint}");
            });
        };
        svc.ErrorOccurred += msg => Dispatcher.Invoke(() => AppendLog($"[错误] {msg}"));

        _listeners.Add(svc);
        PortInputBox.Text = (port + 1).ToString();
    }

    private async void OnToggleListenerClick(object sender, RoutedEventArgs e)
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

    // ========================= Tester =========================

    private async void OnTestPortClick(object sender, RoutedEventArgs e)
    {
        var host = TestHostBox.Text.Trim();
        if (!int.TryParse(TestPortBox.Text.Trim(), out var port) || port <= 0 || port > 65535)
        {
            TestResultText.Text = "端口无效";
            TestResultText.Foreground = AppleTheme.DangerBrush;
            return;
        }
        if (string.IsNullOrWhiteSpace(host))
        {
            TestResultText.Text = "主机不能为空";
            TestResultText.Foreground = AppleTheme.DangerBrush;
            return;
        }

        TestResultText.Text = "测试中…";
        TestResultText.Foreground = AppleTheme.TextSecondaryBrush;
        TestPortClickBtn.IsEnabled = false;
        try
        {
            var r = await PortTesterService.TestAsync(host, port);
            TestResultText.Text = r.IsOpen
                ? $"OPEN · {r.LatencyMs} ms"
                : $"CLOSED · {r.Detail}";
            TestResultText.Foreground = r.IsOpen ? AppleTheme.SuccessBrush : AppleTheme.DangerBrush;
        }
        finally
        {
            TestPortClickBtn.IsEnabled = true;
        }
    }

    private Button TestPortClickBtn => (Button)FindName("TestPortClickBtn") ?? new Button();

    // ========================= Ping =========================

    private void OnIntervalChanged(object sender, RoutedEventArgs e)
    {
        if (_ping.IsRunning) return; // only adjust before start
        _ping.IntervalMode = Interval1000.IsChecked == true
            ? PingIntervalMode.Standard1000
            : PingIntervalMode.Fast100;
    }

    private async void OnPingToggleClick(object sender, RoutedEventArgs e)
    {
        if (_ping.IsRunning)
        {
            _ping.Stop();
            return;
        }
        var host = PingHostBox.Text.Trim();
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
        Dispatcher.Invoke(() =>
        {
            // Results list auto-scrolled by ObservableCollection; we manually scroll to bottom.
            PingResultScroller.ScrollToEnd();
            UpdateStats();
        });
    }

    private void OnPingStateChanged(bool running)
    {
        Dispatcher.Invoke(() =>
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
        // Lightweight; could route to a dedicated log panel later.
        System.Diagnostics.Debug.WriteLine($"{DateTime.Now:HH:mm:ss} {message}");
    }
}
