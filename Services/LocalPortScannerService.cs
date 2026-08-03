using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Text.RegularExpressions;

namespace PortPingTool.Services;

/// <summary>
/// Scans the local machine for listening TCP / UDP ports by parsing
/// the output of 'netstat -ano'. No admin rights required because
/// the query runs in user space and only lists sockets this user
/// can see.
///
/// Output rows: LocalAddress, RemoteAddress, State, Protocol, Pid.
/// </summary>
public static class LocalPortScannerService
{
    public static async Task<IReadOnlyList<LocalPortRow>> ScanAsync(CancellationToken ct = default)
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
        // On Windows, force netstat to use the OEM/UTF-8 output
        psi.StandardOutputEncoding = System.Text.Encoding.UTF8;

        try
        {
            using var proc = Process.Start(psi);
            if (proc is null) return Array.Empty<LocalPortRow>();

            // Read all output asynchronously so we can respect the cancellation token
            var outputTask = proc.StandardOutput.ReadToEndAsync(ct);
            await proc.WaitForExitAsync(ct).ConfigureAwait(false);
            var output = await outputTask.ConfigureAwait(false);

            return ParseNetstat(output);
        }
        catch (OperationCanceledException) { return Array.Empty<LocalPortRow>(); }
        catch (Exception)
        {
            // netstat is missing (e.g. non-Windows). Return empty.
            return Array.Empty<LocalPortRow>();
        }
    }

    private static List<LocalPortRow> ParseNetstat(string output)
    {
        // Example line:
        //   TCP    0.0.0.0:135       0.0.0.0:0       LISTENING       4
        //   UDP    0.0.0.0:5353      *:*                              1234
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

            // Keep only LISTENING for TCP, and ignore UDP wildcard (*:*)
            if (proto == "TCP" && state != "LISTENING") continue;

            rows.Add(new LocalPortRow
            {
                Protocol = proto,
                LocalAddress = local.address,
                LocalPort = local.port,
                RemoteAddress = remote.address,
                State = string.IsNullOrEmpty(state) ? "—" : state,
                Pid = pid,
            });
        }

        // Sort by port for stable display
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
        // Normalize "*" / "0.0.0.0" to a friendly label
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

    public string DisplayPort => $"{LocalPort}";
    public string DisplayAddress => LocalAddress == "0.0.0.0" ? "全部" : LocalAddress;
    public string DisplayProtocol => Protocol;
    public string DisplayState => State;
    public string DisplayPid => Pid > 0 ? Pid.ToString() : "—";
}
