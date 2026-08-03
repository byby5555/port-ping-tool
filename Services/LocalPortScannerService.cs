using System.Diagnostics;
using System.Text.RegularExpressions;

namespace PortPingTool.Services;

/// <summary>
/// Scans the local machine for LISTENING TCP ports by parsing
/// the output of 'netstat -ano'.
///
/// The set of ports to enumerate is supplied by the caller — we
/// filter netstat's output down to that set so the UI can show only
/// what the user asked for (and avoid dumping 60+ entries when they
/// only care about port 80).
///
/// If 'portFilter' is empty, ALL listening ports are returned.
/// </summary>
public static class LocalPortScannerService
{
    public static async Task<IReadOnlyList<LocalPortRow>> ScanAsync(
        IReadOnlyCollection<int>? portFilter = null,
        CancellationToken ct = default)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "netstat",
            Arguments = "-ano",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        psi.StandardOutputEncoding = System.Text.Encoding.UTF8;

        try
        {
            using var proc = Process.Start(psi);
            if (proc is null) return Array.Empty<LocalPortRow>();

            var outputTask = proc.StandardOutput.ReadToEndAsync(ct);
            await proc.WaitForExitAsync(ct).ConfigureAwait(false);
            var output = await outputTask.ConfigureAwait(false);

            return ParseNetstat(output, portFilter);
        }
        catch (OperationCanceledException) { return Array.Empty<LocalPortRow>(); }
        catch
        {
            return Array.Empty<LocalPortRow>();
        }
    }

    private static List<LocalPortRow> ParseNetstat(
        string output, IReadOnlyCollection<int>? portFilter)
    {
        var rows = new List<LocalPortRow>();
        var regex = new Regex(
            @"^\s*(TCP|UDP)\s+(\S+)\s+(\S+)\s+(\S*)\s*(\d+)\s*$",
            RegexOptions.Compiled | RegexOptions.Multiline);

        foreach (Match m in regex.Matches(output))
        {
            var proto = m.Groups[1].Value;
            var local = SplitAddress(m.Groups[2].Value);
            var remote = SplitAddress(m.Groups[3].Value);
            var state = m.Groups[4].Value;
            var pid = int.TryParse(m.Groups[5].Value, out var p) ? p : 0;

            // Only TCP LISTENING. UDP has no LISTEN state.
            if (proto == "TCP" && state != "LISTENING") continue;

            if (portFilter is { Count: > 0 } && !portFilter.Contains(local.port))
                continue;

            rows.Add(new LocalPortRow
            {
                Protocol = proto,
                LocalAddress = local.address,
                LocalPort = local.port,
                RemoteAddress = remote.address,
                State = string.IsNullOrEmpty(state) ? "OPEN" : state,
                Pid = pid,
                IsOpen = true,
                Source = "本机",
            });
        }
        return rows.OrderBy(r => r.LocalPort).ToList();
    }

    private static (string address, int port) SplitAddress(string s)
    {
        if (string.IsNullOrEmpty(s)) return ("—", 0);
        var idx = s.LastIndexOf(':');
        if (idx < 0) return (s, 0);
        var addr = s.Substring(0, idx);
        var portStr = s.Substring(idx + 1);
        var port = int.TryParse(portStr, out var p) ? p : 0;
        if (addr == "*" || addr == "0.0.0.0") addr = "0.0.0.0";
        return (addr, port);
    }
}

public sealed class LocalPortRow
{
    public string Protocol { get; init; } = "TCP";
    public string LocalAddress { get; init; } = "—";
    public int LocalPort { get; init; }
    public string RemoteAddress { get; init; } = "—";
    public string State { get; init; } = "—";
    public int Pid { get; init; }
    public bool IsOpen { get; init; }
    /// <summary>"本机" or "远端" — which scanner found this row.</summary>
    public string Source { get; init; } = "本机";

    public string DisplayPort => LocalPort.ToString();
    public string DisplayAddress =>
        LocalAddress == "0.0.0.0" ? "全部" : LocalAddress;
    public string DisplayState => State;
    public string DisplayPid => Pid > 0 ? Pid.ToString() : "—";
    public string DisplaySource => Source;
}
