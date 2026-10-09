using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using ModelContextProtocol;

namespace FinvizMcpServer.Finviz;

/// <summary>
/// Thin client over the Finviz Elite CSV export endpoints.
/// The API token is appended as the "auth" query parameter and is never logged or returned to callers.
/// </summary>
public sealed partial class FinvizClient
{
    private readonly HttpClient _http;
    private readonly FinvizOptions _options;

    public FinvizClient(HttpClient http, IOptions<FinvizOptions> options)
    {
        _http = http;
        _options = options.Value;
    }

    /// <summary>Screener export (export.ashx).</summary>
    public Task<string> ScreenerAsync(
        string? filters, string view, string? order, string? columns, string? tickers, string? signal,
        int limit, CancellationToken ct) =>
        GetCsvAsync("export.ashx", new()
        {
            ["v"] = view,
            ["f"] = filters,
            ["s"] = signal,
            ["t"] = tickers,
            ["o"] = order,
            ["c"] = columns,
        }, limit, ct);

    /// <summary>Historical / intraday OHLCV candles (quote_export.ashx).</summary>
    public Task<string> QuoteHistoryAsync(string ticker, string timeframe, int limit, CancellationToken ct) =>
        GetCsvAsync("quote_export.ashx", new() { ["t"] = ticker, ["p"] = timeframe }, limit, ct, keepLast: true);

    /// <summary>News export (news_export.ashx).</summary>
    public Task<string> NewsAsync(string view, string? tickers, int limit, CancellationToken ct) =>
        GetCsvAsync("news_export.ashx", new() { ["v"] = view, ["t"] = tickers }, limit, ct);

    /// <summary>Sector / industry / country / capitalization group performance (grp_export.ashx).</summary>
    public Task<string> GroupsAsync(string group, string view, string? order, int limit, CancellationToken ct) =>
        GetCsvAsync("grp_export.ashx", new() { ["g"] = group, ["v"] = view, ["o"] = order }, limit, ct);

    /// <summary>Portfolio export (portfolio_export.ashx).</summary>
    public Task<string> PortfolioAsync(string portfolioId, int limit, CancellationToken ct) =>
        GetCsvAsync("portfolio_export.ashx", new() { ["pid"] = portfolioId }, limit, ct);

    private async Task<string> GetCsvAsync(
        string path, Dictionary<string, string?> query, int limit, CancellationToken ct, bool keepLast = false)
    {
        if (string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            throw new McpException(
                "Finviz API key is not configured. Set the FINVIZ_API_KEY environment variable to your Finviz Elite API token.");
        }

        var url = BuildUrl(path, query, _options.ApiKey);

        string body;
        try
        {
            using var response = await _http.GetAsync(url, ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                throw new McpException(response.StatusCode switch
                {
                    System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden =>
                        "Finviz rejected the API key (HTTP " + (int)response.StatusCode + "). An active Finviz Elite subscription is required.",
                    System.Net.HttpStatusCode.TooManyRequests =>
                        "Finviz rate limit exceeded (HTTP 429). Try again shortly.",
                    _ => $"Finviz request to {path} failed with HTTP {(int)response.StatusCode}.",
                });
            }

            body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            // Do not include ex.ToString(): it can contain the request URI (and therefore the token).
            throw new McpException($"Finviz request to {path} failed: {ex.Message}");
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new McpException($"Finviz request to {path} timed out.");
        }

        body = body.TrimStart('\uFEFF');

        // Finviz returns an HTML login/error page (HTTP 200) for bad tokens or invalid parameters.
        if (body.TrimStart().StartsWith('<'))
        {
            throw new McpException(
                $"Finviz returned an HTML page instead of CSV for {path}. Check the API key and request parameters.");
        }

        // Finviz returns an empty/whitespace 200 response for unsupported view codes or parameters.
        if (string.IsNullOrWhiteSpace(body))
        {
            throw new McpException(
                $"Finviz returned an empty response for {path}. The parameters (e.g. view code, filters, ticker) may be invalid or matched nothing.");
        }

        return LimitRows(body, limit, keepLast);
    }

    internal static string BuildUrl(string path, Dictionary<string, string?> query, string apiKey)
    {
        var sb = new StringBuilder(path).Append('?');
        foreach (var (key, value) in query)
        {
            if (string.IsNullOrWhiteSpace(value)) continue;
            // Finviz expects literal commas in list parameters (f=, c=, t=).
            sb.Append(key).Append('=')
              .Append(Uri.EscapeDataString(value.Trim()).Replace("%2C", ",", StringComparison.Ordinal))
              .Append('&');
        }
        sb.Append("auth=").Append(Uri.EscapeDataString(apiKey));
        return sb.ToString();
    }

    /// <summary>
    /// Caps the number of data rows returned so large exports don't flood the model context.
    /// Keeps the header row. When <paramref name="keepLast"/> is set the most recent (last) rows are kept.
    /// </summary>
    internal static string LimitRows(string csv, int limit, bool keepLast)
    {
        var lines = csv.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                       .Select(l => l.TrimEnd('\r'))
                       .ToList();
        if (lines.Count <= 1 || limit <= 0 || lines.Count - 1 <= limit)
        {
            return string.Join('\n', lines);
        }

        var data = lines.Skip(1);
        var kept = keepLast ? data.TakeLast(limit) : data.Take(limit);
        var total = lines.Count - 1;
        return string.Join('\n', new[] { lines[0] }.Concat(kept))
               + $"\n# Showing {(keepLast ? "last " : "first ")}{limit} of {total} rows. Increase 'limit' to see more.";
    }

    [GeneratedRegex("^[A-Za-z0-9.\\-^=]{1,15}$")]
    private static partial Regex TickerRegex();

    [GeneratedRegex("^[A-Za-z0-9.\\-^=]{1,15}(,[A-Za-z0-9.\\-^=]{1,15})*$")]
    private static partial Regex TickerListRegex();

    [GeneratedRegex("^[A-Za-z0-9_,.\\-|:]+$")]
    private static partial Regex TokenListRegex();

    internal static string RequireTicker(string value) =>
        TickerRegex().IsMatch(value ?? "") ? value! : throw new McpException($"Invalid ticker '{value}'.");

    internal static string? OptionalTickerList(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null
        : TickerListRegex().IsMatch(value.Trim()) ? value.Trim()
        : throw new McpException($"Invalid ticker list '{value}'. Use comma-separated symbols, e.g. 'AAPL,MSFT'.");

    /// <summary>Validates Finviz filter/column/order tokens (letters, digits and a few separators only).</summary>
    internal static string? OptionalToken(string? value, string name) =>
        string.IsNullOrWhiteSpace(value) ? null
        : TokenListRegex().IsMatch(value.Trim()) ? value.Trim()
        : throw new McpException($"Invalid characters in '{name}'.");
}
