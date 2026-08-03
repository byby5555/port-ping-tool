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
    private readonly ObservableCollection<LocalPortRow> _scanResults = new();
    private readonly PingService _ping = new();
    private readonly TcpPingService _tcpPing = new();

    public MainWindow()
    {
        InitializeComponent();
        ListenerList.ItemsSource = _listeners;
        ConnectionLog.ItemsSource = _connectionLog;
        ScanResults.ItemsSource = _scanResults;
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

    // Active ping service: ICMP or TCP, depending on Mode radio.
    private PingService? ActivePing => ModeTcp.IsChecked == true ? null : _ping;
    private TcpPingService? ActiveTcpPing => ModeTcp.IsChecked == true ? _tcpPing : null;

    private int CurrentIntervalMs()
    {
        if (Interval1.IsChecked == true) return 1;
        if (Interval10.IsChecked == true) return 10;
        if (Interval100.IsChecked == true) return 100;
        return 1000;
    }

    private void OnModeChanged(object? sender, RoutedEventArgs e)
    {
        // Enable / disable the port input depending on mode
        if (PingPortBox is null) return;
        bool isTcp = ModeTcp.IsChecked == true;
        PingPortBox.IsEnabled = isTcp;
    }

    private void OnIntervalChanged(object? sender, RoutedEventArgs e)
    {
        // Apply to whichever service might be running (only one runs at a time)
        int ms = CurrentIntervalMs();
        if (!_ping.IsRunning) _ping.IntervalMs = ms;
        if (!_tcpPing.IsRunning) _tcpPing.IntervalMs = ms;
    }

    private async void OnPingToggleClick(object? sender, RoutedEventArgs e)
    {
        bool isTcp = ModeTcp.IsChecked == true;

        if (isTcp ? _tcpPing.IsRunning : _ping.IsRunning)
        {
            if (isTcp) _tcpPing.Stop(); else _ping.Stop();
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
        int port = 0;
        if (isTcp)
        {
            if (!int.TryParse(PingPortBox.Text?.Trim(), out port) || port <= 0 || port > 65535)
            {
                AppendLog("[错误] TCP 模式需要合法端口");
                return;
            }
        }

        if (isTcp)
        {
            _tcpPing.IntervalMs = CurrentIntervalMs();
            await _tcpPing.StartAsync(host, port, count);
        }
        else
        {
            _ping.IntervalMs = CurrentIntervalMs();
            await _ping.StartAsync(host, count);
        }
    }

    private void OnPingResult(PingRecord r)
    {
        Dispatcher.UIThread.InvokeAsync(() =>
        {
            // Identify which service fired (we subscribe to both).
            // In practice we route via the active service: by the time the
            // event fires, exactly one of them is running. We can detect
            // which by checking the current sender via reflection-free
            // proxy: just add to whichever list does NOT already contain
            // this record (cheap O(n) over 1000, but we use IsRunning).
            // Simpler: add to BOTH; the UI shows whichever collection
            // is currently bound, so cross-talk is invisible.
            // Cleaner: route by checking which service is running.
            if (_ping.IsRunning)
            {
                AppendToResults(_ping.Results, _ping.LostResults, r);
            }
            else if (_tcpPing.IsRunning)
            {
                AppendToResults(_tcpPing.Results, _tcpPing.LostResults, r);
            }
            // Auto-scroll only when "全部" is active
            if (ShowAllRadio.IsChecked == true)
                PingResultScroller.ScrollToEnd();
            UpdateStats();
            UpdatePingListCount();
        });
    }

    private static void AppendToResults(
        System.Collections.ObjectModel.ObservableCollection<PingRecord> results,
        System.Collections.ObjectModel.ObservableCollection<PingRecord> lost,
        PingRecord r)
    {
        results.Add(r);
        while (results.Count > 1000) results.RemoveAt(0);
        if (!r.Success)
        {
            lost.Add(r);
            while (lost.Count > 1000) lost.RemoveAt(0);
        }
    }

    private void OnPingViewChanged(object? sender, RoutedEventArgs e)
    {
        // Identify the target view by SENDER, not by IsChecked state. The
        // latter has a race during radio switches where both radions briefly
        // show IsChecked == false, and we can't tell which view the user
        // is switching into.
        bool showingLost = sender == ShowLostRadio;
        var activeResults = ActivePing?.Results ?? ActiveTcpPing?.Results;
        var activeLost    = ActivePing?.LostResults ?? ActiveTcpPing?.LostResults;
        if (activeResults is null || activeLost is null) return;
        // Re-bind to the new source. We null-then-set to force the
        // ItemsControl to drop any cached DataTemplate / virtualization
        // state from the previous collection.
        PingResults.ItemsSource = null;
        PingResults.ItemsSource = showingLost ? activeLost : activeResults;
        PingResultScroller.ScrollToEnd();
        UpdateStats();
        UpdatePingListCount();
    }

    private void UpdatePingListCount()
    {
        bool showingLost = ShowLostRadio.IsChecked == true;
        var activePing = ActivePing ?? (object)ActiveTcpPing!;
        int count = showingLost
            ? (ActivePing?.LostResults.Count ?? _tcpPing.LostResults.Count)
            : (ActivePing?.Results.Count     ?? _tcpPing.Results.Count);
        int total = showingLost
            ? (ActivePing?.Stats.Lost        ?? _tcpPing.Stats.Lost)
            : (ActivePing?.Stats.Sent        ?? _tcpPing.Stats.Sent);
        PingListCount.Text = $"显示 {count} / {total}";
    }

    private void OnPingStateChanged(bool running)
    {
        Dispatcher.UIThread.InvokeAsync(() =>
        {
            PingToggleBtn.Content = running ? "停止" : "开始";

            // Re-bind ItemsSource to whichever service just became active so
            // the user sees the right list.
            var activePing = ActivePing;
            var activeTcp  = ActiveTcpPing;
            if (activePing is not null)
                PingResults.ItemsSource = ShowLostRadio.IsChecked == true
                    ? activePing.LostResults : activePing.Results;
            else if (activeTcp is not null)
                PingResults.ItemsSource = ShowLostRadio.IsChecked == true
                    ? activeTcp.LostResults : activeTcp.Results;

            if (running)
            {
                // Starting: clear stat cards and results so the panel starts clean.
                // This is also the recovery path if a previous bug left the view stale.
                if (activePing is not null) { activePing.Results.Clear(); activePing.LostResults.Clear(); }
                if (activeTcp  is not null) { activeTcp.Results.Clear();  activeTcp.LostResults.Clear(); }
                StatSent.Text  = "0";
                StatRecv.Text  = "0";
                StatLoss.Text  = "0.0%";
                StatAvg.Text   = "0 ms";
            }
            // On stop: keep the results so the user can review the stats.
            UpdateStats();
            UpdatePingListCount();
        });
    }

    private void UpdateStats()
    {
        // "全部" 模式:显示所有统计
        // "只丢包" 模式:统计针对丢包子集
        bool showingLost = ShowLostRadio.IsChecked == true;
        if (showingLost)
        {
            // Lost-only view: sent/recv/loss computed over the lost-list size
            int lostCount = _ping.LostResults.Count;
            int totalAttempts = _ping.Stats.Sent;
            int totalLost = _ping.Stats.Lost;
            StatSent.Text = $"{totalLost} (丢包)";
            StatRecv.Text = $"0 (全失败)";
            StatLoss.Text = totalAttempts == 0 ? "0.0%" : $"{(double)totalLost / totalAttempts * 100:F1}%";
            StatAvg.Text  = "—";
        }
        else
        {
            var s = _ping.Stats;
            StatSent.Text = s.Sent.ToString();
            StatRecv.Text = s.Received.ToString();
            StatLoss.Text = $"{s.LossRate:F1}%";
            StatAvg.Text  = $"{s.AvgLatency:F0} ms";
        }
    }

    // ========================= Scanner =========================

    private async void OnScanClick(object? sender, RoutedEventArgs e)
    {
        var target = ScanTargetBox.Text?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(target))
        {
            ScanStatusText.Text = "请输入目标";
            return;
        }
        ScanBtn.IsEnabled = false;
        ScanStatusText.Text = "扫描中…";
        _scanResults.Clear();
        try
        {
            // For now this only enumerates LISTENING sockets on the LOCAL
            // machine (netstat -ano). The target field is accepted for
            // forward compatibility — remote scanning would need a different
            // transport (TCP connect probe per port) and is out of scope for
            // this version. We just inform the user of the actual scope.
            bool isLocal = target is "127.0.0.1" or "localhost" or "0.0.0.0";
            if (!isLocal)
            {
                ScanStatusText.Text = "暂仅支持本机扫描,目标已忽略";
            }
            var rows = await LocalPortScannerService.ScanAsync();
            foreach (var row in rows) _scanResults.Add(row);
            if (isLocal)
                ScanStatusText.Text = $"找到 {rows.Count} 个 LISTEN 端口";
            else
                ScanStatusText.Text = $"找到 {rows.Count} 个本机 LISTEN 端口(远程暂未实现)";
        }
        catch (Exception ex)
        {
            ScanStatusText.Text = $"扫描失败: {ex.Message}";
        }
        finally
        {
            ScanBtn.IsEnabled = true;
        }
    }

    // ========================= Misc =========================

    private void AppendLog(string message)
    {
        System.Diagnostics.Debug.WriteLine($"{DateTime.Now:HH:mm:ss} {message}");
    }
}
