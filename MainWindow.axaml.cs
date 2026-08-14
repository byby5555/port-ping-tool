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
    private readonly ObservableCollection<string> _logLines = new();
    private readonly PingService _ping = new();
    private readonly TcpPingService _tcpPing = new();
    private readonly PublicIpService _publicIp = new();
    private const int LogMaxLines = 500;
    private PublicIpInfo? _outsideInfo;
    private PublicIpInfo? _chinaInfo;

    public MainWindow()
    {
        InitializeComponent();
        ListenerList.ItemsSource = _listeners;
        ConnectionLog.ItemsSource = _connectionLog;
        ScanResults.ItemsSource = _scanResults;
        PingResults.ItemsSource = _ping.Results;
        LogItems.ItemsSource = _logLines;

        _ping.ResultArrived += OnPingResultIcmp;
        _ping.StateChanged += OnPingStateChanged;
        _tcpPing.ResultArrived += OnPingResultTcp;
        _tcpPing.StateChanged += OnPingStateChanged;

        // Scanner slider live update — use the ValueChanged routed event
        // (not PropertyChanged) because RangeBase exposes it specifically and
        // PropertyChanged doesn't always fire for the Value DP in all
        // Avalonia versions.
        ScanConcurrencySlider.AddHandler(
            Avalonia.Controls.Primitives.RangeBase.ValueChangedEvent,
            new System.EventHandler<Avalonia.Controls.Primitives.RangeBaseValueChangedEventArgs>(OnScanConcurrencyChanged));

        CountFixed.IsCheckedChanged   += (_, _) => { if (CountFixed.IsChecked == true) CountBox.IsEnabled = true; };
        CountContinuous.IsCheckedChanged += (_, _) => { if (CountContinuous.IsChecked == true) CountBox.IsEnabled = false; };

        // Fire public-IP lookup on startup
        _ = RefreshPublicIpAsync();

        // Initialise the interval-mode hint
        UpdateIntervalHint();
    }

    // ========================= Network info (top-right card) =========================

    private async Task RefreshPublicIpAsync()
    {
        NetInfoOutside.Text = "正在查询公网信息(国际)…";
        NetInfoChina.Text   = "国内侧查询中…";
        NetInfoProxy.Text   = "代理状态: 查询中…";

        var outsideTask = _publicIp.LookupAsync();
        var chinaTask   = _publicIp.LookupChinaAsync();
        await Task.WhenAll(outsideTask, chinaTask).ConfigureAwait(false);
        _outsideInfo = outsideTask.Result;
        _chinaInfo   = chinaTask.Result;

        Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (!string.IsNullOrEmpty(_outsideInfo.Error))
                NetInfoOutside.Text = $"公网(国际): 查询失败 — {_outsideInfo.Error}";
            else
                NetInfoOutside.Text = $"公网(国际): {_outsideInfo.Ip}  ·  {_outsideInfo.Location}  ·  {_outsideInfo.Operator}";

            if (!string.IsNullOrEmpty(_chinaInfo.Error))
                NetInfoChina.Text = $"国内侧: 查询失败 — {_chinaInfo.Error}";
            else
                NetInfoChina.Text = $"国内侧: {_chinaInfo.Ip}  ·  {_chinaInfo.Location}  ·  {_chinaInfo.Operator}";

            if (string.IsNullOrEmpty(_outsideInfo.Ip) || string.IsNullOrEmpty(_chinaInfo.Ip))
            {
                NetInfoProxy.Text = "代理状态: 未知(IP 查询失败)";
            }
            else if (!string.Equals(_outsideInfo.Ip, _chinaInfo.Ip, StringComparison.Ordinal))
            {
                NetInfoProxy.Foreground = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#FF9500"));
                NetInfoProxy.Text = $"⚠ 检测到代理 — 国际 {_outsideInfo.Ip} ≠ 国内 {_chinaInfo.Ip}";
            }
            else
            {
                NetInfoProxy.Foreground = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#34C759"));
                NetInfoProxy.Text = $"代理状态: 未检测到代理(出口 IP 一致 = {_outsideInfo.Ip})";
            }
        });
    }

    private async void OnNetInfoRefreshClick(object? sender, RoutedEventArgs e)
    {
        NetInfoRefreshBtn.IsEnabled = false;
        try { await RefreshPublicIpAsync(); }
        finally { NetInfoRefreshBtn.IsEnabled = true; }
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
                // Add to END (O(1)) rather than Insert(0) (O(n)). Cap at
                // 500 entries so the operator has a useful rolling window
                // for diagnosing a hammered port. The ListBox virtualizes
                // beyond that so memory cost stays bounded.
                _connectionLog.Add(record);
                while (_connectionLog.Count > 500) _connectionLog.RemoveAt(0);
                // Auto-scroll to the newest entry (at the bottom).
                ConnectionLogScroller.ScrollToEnd();
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
            // Build the result line. If the host was a hostname we show the
            // resolved IP next to the latency / detail, e.g.
            //   "OPEN · 42 ms (1.1.1.1)"
            // For IP literals, the ResolvedIp equals the input and we
            // skip the parenthetical to avoid noise.
            string ipTag = "";
            if (!string.IsNullOrEmpty(r.ResolvedIp)
                && !string.Equals(r.ResolvedIp, host, StringComparison.Ordinal))
            {
                ipTag = $"  ({r.ResolvedIp})";
            }
            TestResultText.Text = r.IsOpen
                ? $"OPEN · {r.LatencyMs} ms{ipTag}"
                : $"CLOSED · {r.Detail}{ipTag}";
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

    /// <summary>Stats object for the currently active ping service. Returns ICMP stats as a sane default.</summary>
    private PingStatistics ActiveStats => ModeTcp.IsChecked == true ? _tcpPing.Stats : _ping.Stats;

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

        // Switching mode while a ping is running would otherwise leave two
        // services in flight at once. Stop whichever is no longer selected.
        if (isTcp && _ping.IsRunning) _ping.Stop();
        if (!isTcp && _tcpPing.IsRunning) _tcpPing.Stop();

        UpdateIntervalHint();
    }

    private void OnIntervalChanged(object? sender, RoutedEventArgs e)
    {
        // Apply to whichever service might be running (only one runs at a time)
        int ms = CurrentIntervalMs();
        if (!_ping.IsRunning) _ping.IntervalMs = ms;
        if (!_tcpPing.IsRunning) _tcpPing.IntervalMs = ms;
        UpdateIntervalHint();
    }

    private void UpdateIntervalHint()
    {
        if (IntervalHint is null) return;
        bool isTcp = ModeTcp.IsChecked == true;
        int ms = CurrentIntervalMs();
        if (isTcp)
        {
            // TCP can actually hit the requested interval (no kernel throttling).
            IntervalHint.Foreground = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#34C759"));
            IntervalHint.Text = ms switch
            {
                1   => "⚡ 1ms 实际可达:系统 TCP 握手无内核节流,适合高频端口响应监测。",
                10  => "10ms 高频 TCP 握手,适合端口抖动监测。",
                100 => "100ms 较快 TCP 握手,默认推荐。",
                _   => "1000ms 标准 TCP 握手,默认间隔。",
            };
        }
        else
        {
            // ICMP is throttled by the Windows kernel.
            IntervalHint.Foreground = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#8E8E93"));
            IntervalHint.Text = ms switch
            {
                1   => "⚠ 1ms ICMP 实际不可达:Windows 内核 ICMP 限流,实测最快约 5-10ms。",
                10  => "⚠ 10ms ICMP 会被节流,丢包率会虚高,推荐用 1000ms 或切 TCP。",
                100 => "100ms ICMP 高频,丢包率会被系统节流影响,推荐用 TCP 模式获得真 1ms。",
                _   => "1000ms 标准 ICMP,默认推荐。",
            };
        }
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

    private void OnPingResultIcmp(PingRecord r) => HandlePingResult(r, _ping.Results, _ping.LostResults);
    private void OnPingResultTcp(PingRecord r) => HandlePingResult(r, _tcpPing.Results, _tcpPing.LostResults);

    private void HandlePingResult(
        PingRecord r,
        System.Collections.ObjectModel.ObservableCollection<PingRecord> results,
        System.Collections.ObjectModel.ObservableCollection<PingRecord> lost)
    {
        Dispatcher.UIThread.InvokeAsync(() =>
        {
            AppendToResults(results, lost, r);
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
        var stats = ActiveStats;
        var lostList = ModeTcp.IsChecked == true ? _tcpPing.LostResults : _ping.LostResults;
        var allList  = ModeTcp.IsChecked == true ? _tcpPing.Results     : _ping.Results;
        int count = showingLost ? lostList.Count : allList.Count;
        int total = showingLost ? stats.Lost     : stats.Sent;
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
        bool showingLost = ShowLostRadio.IsChecked == true;
        var s = ActiveStats;
        if (showingLost)
        {
            // Lost-only view: sent = total lost attempts; recv = 0 (all failed)
            StatSent.Text = $"{s.Lost} (丢包)";
            StatRecv.Text = "0 (全失败)";
            StatLoss.Text = s.Sent == 0 ? "0.0%" : $"{(double)s.Lost / s.Sent * 100:F1}%";
            StatAvg.Text  = "—";
        }
        else
        {
            StatSent.Text = s.Sent.ToString();
            StatRecv.Text = s.Received.ToString();
            StatLoss.Text = $"{s.LossRate:F1}%";
            StatAvg.Text  = $"{s.AvgLatency:F0} ms";
        }
    }

    // ========================= Scanner =========================

    private readonly RemotePortScannerService _remoteScanner = new();

    private void OnScanConcurrencyChanged(object? sender, Avalonia.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (ScanConcurrencyText is not null)
            ScanConcurrencyText.Text = ((int)e.NewValue).ToString();
    }

    private void OnScanCancelClick(object? sender, RoutedEventArgs e)
    {
        _remoteScanner.Cancel();
        ScanStatusText.Text = "已取消";
    }

    private async void OnScanClick(object? sender, RoutedEventArgs e)
    {
        var target = ScanTargetBox.Text?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(target))
        {
            ScanStatusText.Text = "请输入目标";
            return;
        }
        var portSpec = ScanPortsBox.Text?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(portSpec))
        {
            ScanStatusText.Text = "请输入端口";
            return;
        }
        var ports = PortRangeParser.Parse(portSpec);
        if (ports.Count == 0)
        {
            ScanStatusText.Text = "端口解析失败";
            return;
        }
        int concurrency = (int)ScanConcurrencySlider.Value;

        bool isLocal = target.Equals("127.0.0.1", StringComparison.OrdinalIgnoreCase)
                    || target.Equals("localhost", StringComparison.OrdinalIgnoreCase)
                    || target.Equals("0.0.0.0", StringComparison.OrdinalIgnoreCase);

        ScanBtn.IsEnabled = false;
        ScanCancelBtn.IsEnabled = !isLocal;
        ScanStatusText.Text = isLocal ? "读取 netstat…" : $"扫描中… ({ports.Count} 个端口, 并发 {concurrency})";
        _scanResults.Clear();
        ScanProgressBar.Value = 0;

        try
        {
            if (isLocal)
            {
                // Local: filter netstat output to the user-supplied port set
                var localResult = await LocalPortScannerService.ScanAsync(ports);
                if (localResult.HasError)
                {
                    ScanStatusText.Text = $"扫描失败: {localResult.Error}";
                }
                else
                {
                    foreach (var row in localResult.Rows) _scanResults.Add(row);
                    ScanStatusText.Text = $"本机 LISTEN 中匹配端口 {localResult.Rows.Count} / {ports.Count}";
                }
                ScanProgressBar.Value = 1;
            }
            else
            {
                // Remote: TCP connect probe each port. The progress callback
                // already populates _scanResults incrementally as opens are
                // found, so we don't add again at the end (which would
                // duplicate entries).
                var open = await _remoteScanner.ScanAsync(
                    host: target,
                    ports: ports,
                    concurrency: concurrency,
                    timeoutMs: 1500,
                    onProgress: p => Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        ScanProgressBar.Value = p.Fraction;
                        ScanStatusText.Text = p.Summary;
                        // Re-bind incrementally so user sees open ports appear.
                        // Only refresh the list when the open-port count changes
                        // to avoid per-probe UI churn.
                        if (_scanResults.Count != _remoteScanner.OpenPorts.Count)
                        {
                            _scanResults.Clear();
                            foreach (var r in _remoteScanner.OpenPorts.OrderBy(x => x.LocalPort))
                                _scanResults.Add(r);
                        }
                    }));
                ScanStatusText.Text = open.Count == ports.Count
                    ? $"完成: 开放 {open.Count} / {ports.Count}"
                    : $"完成: 开放 {open.Count} / {ports.Count}({ports.Count - open.Count} 已探测关闭或超时)";
            }
        }
        catch (Exception ex)
        {
            ScanStatusText.Text = $"扫描失败: {ex.Message}";
        }
        finally
        {
            ScanBtn.IsEnabled = true;
            ScanCancelBtn.IsEnabled = false;
        }
    }

    // ========================= Misc =========================

    private void AppendLog(string message)
    {
        var line = $"[{DateTime.Now:HH:mm:ss}] {message}";
        System.Diagnostics.Debug.WriteLine(line);

        // Update status bar immediately (always-on, one-line)
        if (StatusBarText is not null)
            StatusBarText.Text = line;

        // Append to log popup (capped at LogMaxLines to avoid unbounded growth)
        if (LogItems is not null)
        {
            _logLines.Add(line);
            while (_logLines.Count > LogMaxLines) _logLines.RemoveAt(0);
        }
    }

    private void OnShowLogClick(object? sender, RoutedEventArgs e)
    {
        if (LogPopup is null) return;
        LogPopup.IsVisible = !LogPopup.IsVisible;
    }

    private void OnCloseLogClick(object? sender, RoutedEventArgs e)
    {
        if (LogPopup is not null) LogPopup.IsVisible = false;
    }
}
