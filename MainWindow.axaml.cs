using System.Collections.ObjectModel;
using System.Linq;
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

    // ========================= Inline port edit =========================
    // Click the "Port 8080" label to switch into edit mode (textbox).
    // Press Enter / lose focus to commit. Press Escape to cancel.

    private PortListenerService? _editingService;

    private void OnPortLabelPointerPressed(object? sender, Avalonia.Input.PointerPressedEventArgs e)
    {
        if (sender is not TextBlock tb || tb.Tag is not PortListenerService svc) return;
        if (svc.IsListening) return; // can't edit while listening
        _editingService = svc;

        // Find the sibling TextBox in the same StackPanel
        var parent = tb.Parent as StackPanel;
        if (parent is null) return;
        var editBox = parent.Children.OfType<TextBox>().FirstOrDefault();
        if (editBox is null) return;

        editBox.Text = svc.Port.ToString();
        editBox.IsVisible = true;
        editBox.Focus();
        editBox.SelectAll();
        tb.IsVisible = false;
        e.Handled = true;
    }

    private void OnPortEditKeyDown(object? sender, Avalonia.Input.KeyEventArgs e)
    {
        if (sender is not TextBox tb) return;
        if (_editingService is null) return;

        if (e.Key == Avalonia.Input.Key.Enter)
        {
            CommitPortEdit(tb);
            e.Handled = true;
        }
        else if (e.Key == Avalonia.Input.Key.Escape)
        {
            CancelPortEdit(tb);
            e.Handled = true;
        }
    }

    private void OnPortEditLostFocus(object? sender, RoutedEventArgs e)
    {
        if (sender is TextBox tb) CommitPortEdit(tb);
    }

    private void CommitPortEdit(TextBox tb)
    {
        if (_editingService is null || !tb.IsVisible) return;
        if (int.TryParse(tb.Text?.Trim(), out var newPort) && newPort > 0 && newPort <= 65535)
        {
            // Make sure no other listener has the same port
            if (_listeners.Any(l => l != _editingService && l.Port == newPort))
            {
                AppendLog($"[错误] 端口 {newPort} 已被其他监听占用");
                return; // keep edit open
            }
            _editingService.Port = newPort;
            AppendLog($"[修改] 端口已改为 {newPort}");
        }
        else
        {
            AppendLog($"[错误] 端口无效: {tb.Text}");
            return; // keep edit open
        }
        tb.IsVisible = false;
        // Restore the label visibility
        var parent = tb.Parent as StackPanel;
        var label = parent?.Children.OfType<TextBlock>().FirstOrDefault();
        if (label is not null) label.IsVisible = true;
        _editingService = null;
    }

    private void CancelPortEdit(TextBox tb)
    {
        if (!tb.IsVisible) return;
        tb.IsVisible = false;
        var parent = tb.Parent as StackPanel;
        var label = parent?.Children.OfType<TextBlock>().FirstOrDefault();
        if (label is not null) label.IsVisible = true;
        _editingService = null;
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
        _ping.LostResults.Clear();
        await _ping.StartAsync(host, count);
    }

    private void OnPingResult(PingRecord r)
    {
        Dispatcher.UIThread.InvokeAsync(() =>
        {
            // Add to "全部" list, capped at 1000
            _ping.Results.Add(r);
            while (_ping.Results.Count > 1000) _ping.Results.RemoveAt(0);

            // Add to "丢包" list (if failed), capped at 1000
            if (!r.Success)
            {
                _ping.LostResults.Add(r);
                while (_ping.LostResults.Count > 1000) _ping.LostResults.RemoveAt(0);
            }

            // Auto-scroll only the currently-shown list
            if (ShowAllRadio.IsChecked == true)
                PingResultScroller.ScrollToEnd();
            UpdateStats();
            UpdatePingListCount();
        });
    }

    private void OnPingViewChanged(object? sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton) return;
        if (ShowAllRadio.IsChecked == true)
        {
            PingResults.ItemsSource = _ping.Results;
        }
        else if (ShowLostRadio.IsChecked == true)
        {
            PingResults.ItemsSource = _ping.LostResults;
        }
        PingResultScroller.ScrollToEnd();
        UpdatePingListCount();
    }

    private void UpdatePingListCount()
    {
        var showingLost = ShowLostRadio.IsChecked == true;
        var count = showingLost ? _ping.LostResults.Count : _ping.Results.Count;
        var total = showingLost ? _ping.Stats.Lost : _ping.Stats.Sent;
        PingListCount.Text = $"显示 {count} / {total}";
    }

    private void OnPingStateChanged(bool running)
    {
        Dispatcher.UIThread.InvokeAsync(() =>
        {
            PingToggleBtn.Content = running ? "停止" : "开始";
            if (running) UpdateStats();
            UpdatePingListCount();
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
