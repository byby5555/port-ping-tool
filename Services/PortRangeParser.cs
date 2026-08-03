using System.Collections.Generic;

namespace PortPingTool.Services;

/// <summary>
/// Parses a port spec into an ordered list of unique ports.
///
/// Accepted syntax:
///   "80"           -> [80]
///   "80,443,8080"  -> [80, 443, 8080]
///   "1-100"        -> [1..100]
///   "80,443,8000-8100"  -> [80, 443, 8000..8100]
///   "all" or "1-65535"  -> [1..65535]
///
/// Whitespace is ignored. Out-of-range or malformed tokens are skipped
/// silently (the UI will report the count).
/// </summary>
public static class PortRangeParser
{
    public const int MinPort = 1;
    public const int MaxPort = 65535;

    public static IReadOnlyList<int> Parse(string spec)
    {
        if (string.IsNullOrWhiteSpace(spec))
            return Array.Empty<int>();

        var trimmed = spec.Trim();
        if (string.Equals(trimmed, "all", StringComparison.OrdinalIgnoreCase))
            return EnumerateRange(1, MaxPort).ToArray();

        var seen = new HashSet<int>();
        var result = new List<int>();
        foreach (var raw in trimmed.Split(new[] { ',', ' ', '\n', '\r', '\t' },
                                          StringSplitOptions.RemoveEmptyEntries))
        {
            var token = raw.Trim();
            if (token.Length == 0) continue;

            // Range "1-100" or "1~100"
            var dash = token.IndexOf('-');
            if (dash > 0 && dash < token.Length - 1)
            {
                var a = token[..dash];
                var b = token[(dash + 1)..];
                if (int.TryParse(a, out var lo) && int.TryParse(b, out var hi)
                    && InRange(lo) && InRange(hi))
                {
                    if (lo > hi) (lo, hi) = (hi, lo);
                    foreach (var p in EnumerateRange(lo, hi))
                        if (seen.Add(p)) result.Add(p);
                }
                continue;
            }

            // Single port
            if (int.TryParse(token, out var single) && InRange(single) && seen.Add(single))
                result.Add(single);
        }
        return result;
    }

    private static bool InRange(int p) => p >= MinPort && p <= MaxPort;

    private static IEnumerable<int> EnumerateRange(int lo, int hi)
    {
        for (int p = lo; p <= hi; p++) yield return p;
    }
}
