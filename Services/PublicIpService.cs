using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PortPingTool.Services;

/// <summary>
/// One record per public-IP lookup, fed by an external API.
/// </summary>
public sealed class PublicIpInfo
{
    public string Ip          { get; set; } = "";
    public string Country     { get; set; } = "";
    public string Region      { get; set; } = "";
    public string City        { get; set; } = "";
    public string Isp         { get; set; } = "";   // e.g. "China Telecom"
    public string Org         { get; set; } = "";
    public string Asn         { get; set; } = "";   // e.g. "AS4134"
    public string Timezone    { get; set; } = "";
    public bool   IsProxy     { get; set; }          // flagged by API as proxy/VPN
    public string Source      { get; set; } = "";   // which endpoint we used
    public string Error       { get; set; } = "";   // populated on failure

    public string Operator =>
        string.IsNullOrEmpty(Isp) ? "未知" : Isp;

    public string Location { get; set; } = "";

    public string ComputeLocation()
    {
        if (string.IsNullOrEmpty(Country)) return "";
        if (string.IsNullOrEmpty(City)) return Country;
        return $"{Country} {Region} {City}";
    }

    public string Display =>
        string.IsNullOrEmpty(Error)
            ? $"{Ip}  ({Location} · {Operator})"
            : $"查询失败: {Error}";
}

/// <summary>
/// Looks up the machine's public IP, geo, and ISP via ip-api.com (free, no
/// key, 45 req/min). Also does a second lookup via a Chinese-specific
/// service so we can detect whether traffic is exiting through a proxy
/// (the two endpoints will return different IPs if so).
/// </summary>
public sealed class PublicIpService
{
    private static readonly HttpClient Http = new()
    {
        Timeout = TimeSpan.FromSeconds(8),
    };

    /// <summary>The "outside" view — uses an international endpoint.</summary>
    public async Task<PublicIpInfo> LookupAsync(CancellationToken ct = default)
    {
        return await QueryIpApiAsync("http://ip-api.com/json/?lang=zh-CN", ct).ConfigureAwait(false);
    }

    /// <summary>The "China" view — uses a Chinese endpoint. Used to detect proxy.</summary>
    public async Task<PublicIpInfo> LookupChinaAsync(CancellationToken ct = default)
    {
        return await QueryPconlineAsync(ct).ConfigureAwait(false);
    }

    private static async Task<PublicIpInfo> QueryIpApiAsync(string url, CancellationToken ct)
    {
        try
        {
            var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.Add("User-Agent",
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 " +
                "(KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36");
            req.Headers.Add("Accept", "application/json");
            var resp = await Http.SendAsync(req, ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
                return new PublicIpInfo { Source = "ip-api.com", Error = $"HTTP {(int)resp.StatusCode}" };
            var json = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            var doc = JsonDocument.Parse(json);
            var r = doc.RootElement;
            if (r.GetProperty("status").GetString() != "success")
                return new PublicIpInfo { Source = "ip-api.com", Error = "API returned failure" };

            var info = new PublicIpInfo
            {
                Ip       = r.TryGetProperty("query", out var q) ? q.GetString() ?? "" : "",
                Country  = r.GetProperty("country").GetString() ?? "",
                Region   = r.GetProperty("regionName").GetString() ?? "",
                City     = r.GetProperty("city").GetString() ?? "",
                Isp      = r.GetProperty("isp").GetString() ?? "",
                Org      = r.GetProperty("org").GetString() ?? "",
                Asn      = r.GetProperty("as").GetString() ?? "",
                Timezone = r.TryGetProperty("timezone", out var tz) ? tz.GetString() ?? "" : "",
                IsProxy  = r.TryGetProperty("proxy", out var pr) && pr.GetBoolean(),
                Source   = "ip-api.com",
            };
            info.Location = info.ComputeLocation();
            return info;
        }
        catch (Exception ex)
        {
            return new PublicIpInfo { Source = "ip-api.com", Error = ex.Message };
        }
    }

    /// <summary>
    /// "China-side" lookup. Tries three endpoints in order:
    ///   1. pconline (best Chinese geo + Chinese ISP name)
    ///   2. ipwho.is (global, has ASN/connection info)
    ///   3. ipinfo.io (last-resort fallback)
    /// Each step returns Error populated on failure so the next step
    /// can run.
    /// </summary>
    private async Task<PublicIpInfo> QueryPconlineAsync(CancellationToken ct)
    {
        var p = await TryPconlineAsync(ct).ConfigureAwait(false);
        if (string.IsNullOrEmpty(p.Error)) return p;
        var w = await TryIpWhoIsAsync(ct).ConfigureAwait(false);
        if (string.IsNullOrEmpty(w.Error)) return w;
        return await TryIpInfoIoAsync(ct).ConfigureAwait(false);
    }

    /// <summary>
    /// pconline returns JSONP-ish text:
    ///   var ipJson={"ip":"47.100.94.83","pro":"浙江省杭州市","city":"杭州市","isp":"阿里巴巴","status":"ok",...}
    /// As of 2026 the endpoint is gated behind a hotload check that 403s
    /// .NET's default HttpClient (User-Agent is too obviously non-browser).
    /// We send a real browser UA + Referer.
    /// </summary>
    private static async Task<PublicIpInfo> TryPconlineAsync(CancellationToken ct)
    {
        try
        {
            var req = new HttpRequestMessage(HttpMethod.Get, "http://whois.pconline.com.cn/ipJson.jsp");
            req.Headers.Add("User-Agent",
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 " +
                "(KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36");
            req.Headers.Add("Referer", "http://whois.pconline.com.cn/");
            req.Headers.Add("Accept", "*/*");
            req.Headers.Add("Accept-Language", "zh-CN,zh;q=0.9");
            var resp = await Http.SendAsync(req, ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
                return new PublicIpInfo { Source = "pconline", Error = $"HTTP {(int)resp.StatusCode}" };
            var text = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            text = text.Trim();
            if (text.StartsWith("var ")) text = text.Substring(text.IndexOf('=') + 1).TrimEnd(';', ' ');
            if (text.StartsWith("<")) // pconline sometimes returns an HTML 200 page
                return new PublicIpInfo { Source = "pconline", Error = "non-JSON response" };
            var doc = JsonDocument.Parse(text);
            var r = doc.RootElement;
            if (r.TryGetProperty("status", out var st) && st.GetString() != "ok")
                return new PublicIpInfo { Source = "pconline", Error = "API status≠ok" };
            var info = new PublicIpInfo
            {
                Ip      = r.TryGetProperty("ip", out var ip) ? ip.GetString() ?? "" : "",
                City    = r.TryGetProperty("city", out var c) ? c.GetString() ?? "" : "",
                Region  = r.TryGetProperty("pro", out var p) ? p.GetString() ?? "" : "",
                Isp     = r.TryGetProperty("isp", out var isp) ? isp.GetString() ?? "" : "",
                Source  = "pconline",
                Country = "中国",
            };
            info.Location = string.IsNullOrEmpty(info.City) ? "中国" : $"{info.Region} {info.City}";
            return info;
        }
        catch (Exception ex)
        {
            return new PublicIpInfo { Source = "pconline", Error = ex.Message };
        }
    }

    /// <summary>
    /// ipwho.is — global free IP geo service. Has ASN/connection.org field.
    /// Used as the primary fallback for the China-side lookup.
    /// </summary>
    private static async Task<PublicIpInfo> TryIpWhoIsAsync(CancellationToken ct)
    {
        try
        {
            var json = await Http.GetStringAsync("https://ipwho.is/", ct).ConfigureAwait(false);
            var doc = JsonDocument.Parse(json);
            var r = doc.RootElement;
            if (r.TryGetProperty("success", out var s) && !s.GetBoolean())
                return new PublicIpInfo { Source = "ipwho.is", Error = "success=false" };
            var info = new PublicIpInfo
            {
                Ip      = r.TryGetProperty("ip", out var ip) ? ip.GetString() ?? "" : "",
                Country = r.TryGetProperty("country", out var c) ? c.GetString() ?? "" : "",
                Region  = r.TryGetProperty("region", out var re) ? re.GetString() ?? "" : "",
                City    = r.TryGetProperty("city", out var ci) ? ci.GetString() ?? "" : "",
                Source  = "ipwho.is",
            };
            if (r.TryGetProperty("connection", out var conn) && conn.ValueKind == JsonValueKind.Object)
            {
                if (conn.TryGetProperty("org", out var org))  info.Org  = org.GetString()  ?? "";
                if (conn.TryGetProperty("isp", out var isp))  info.Isp  = isp.GetString()  ?? "";
                if (conn.TryGetProperty("asn", out var asn))  info.Asn  = asn.ToString();
            }
            info.Location = info.ComputeLocation();
            return info;
        }
        catch (Exception ex)
        {
            return new PublicIpInfo { Source = "ipwho.is", Error = ex.Message };
        }
    }

    /// <summary>
    /// ipinfo.io — last-resort fallback. Reliable, well-cached.
    /// </summary>
    private static async Task<PublicIpInfo> TryIpInfoIoAsync(CancellationToken ct)
    {
        try
        {
            var json = await Http.GetStringAsync("https://ipinfo.io/json", ct).ConfigureAwait(false);
            var doc = JsonDocument.Parse(json);
            var r = doc.RootElement;
            var info = new PublicIpInfo
            {
                Ip      = r.TryGetProperty("ip", out var ipEl) ? ipEl.GetString() ?? "" : "",
                Country = r.TryGetProperty("country", out var cEl) ? cEl.GetString() ?? "" : "",
                Region  = r.TryGetProperty("region", out var reEl) ? reEl.GetString() ?? "" : "",
                City    = r.TryGetProperty("city", out var ciEl) ? ciEl.GetString() ?? "" : "",
                Org     = r.TryGetProperty("org", out var orgEl) ? orgEl.GetString() ?? "" : "",
                Source  = "ipinfo.io",
            };
            // ipinfo's "org" is "AS37963 Hangzhou Alibaba Advertising Co.,Ltd." —
            // strip the ASN prefix to use the rest as ISP.
            var orgStr = info.Org ?? "";
            var sp     = orgStr.IndexOf(' ');
            if (sp > 0 && orgStr.StartsWith("AS") && int.TryParse(orgStr.Substring(2, sp - 2), out _))
                info.Isp = orgStr.Substring(sp + 1);
            else
                info.Isp = orgStr;
            info.Location = info.ComputeLocation();
            return info;
        }
        catch (Exception ex)
        {
            return new PublicIpInfo { Source = "ipinfo.io", Error = ex.Message };
        }
    }
}
