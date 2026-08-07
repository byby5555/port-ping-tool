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
            var json = await Http.GetStringAsync(url, ct).ConfigureAwait(false);
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
    /// pconline returns JSONP-ish text. Format:
    ///   var ipJson={"ip":"47.100.94.83","pro":"浙江省杭州市","proCode":"330000","city":"杭州市","cityCode":"330100","region":"华东","regionCode":"300000","addr":"中国 华东 浙江省 杭州市 阿里巴巴","regionNames":"","isp":"阿里巴巴","ispId":"","status":"ok","src":"dns"}
    /// We strip the var declaration and parse the JSON.
    /// </summary>
    private static async Task<PublicIpInfo> QueryPconlineAsync(CancellationToken ct)
    {
        try
        {
            // pconline sometimes blocks on HTTPS, use HTTP
            var text = await Http.GetStringAsync("http://whois.pconline.com.cn/ipJson.jsp", ct).ConfigureAwait(false);
            // Strip "var ipJson=" prefix and any trailing ";"
            text = text.Trim();
            if (text.StartsWith("var ")) text = text.Substring(text.IndexOf('=') + 1).TrimEnd(';', ' ');
            var doc = JsonDocument.Parse(text);
            var r = doc.RootElement;
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
}
