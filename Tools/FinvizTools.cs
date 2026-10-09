using System.ComponentModel;
using FinvizMcpServer.Finviz;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace FinvizMcpServer.Tools;

/// <summary>MCP tools backed by the Finviz Elite export API. All tools return CSV text.</summary>
[McpServerToolType]
public sealed class FinvizTools(FinvizClient finviz)
{
    private static readonly HashSet<string> Timeframes = new(StringComparer.OrdinalIgnoreCase)
        { "i1", "i3", "i5", "i15", "i30", "h", "d", "w", "m" };

    private static readonly HashSet<string> GroupNames = new(StringComparer.OrdinalIgnoreCase)
        { "sector", "industry", "country", "capitalization" };

    // Finviz only accepts these preset values for the sh_price_o* and sh_avgvol_o* screener filters.
    private static readonly int[] MinPricePresets = [1, 2, 3, 4, 5, 7, 10, 15, 20, 30, 40, 50, 60, 70, 80, 90, 100];
    private static readonly int[] MinAvgVolumePresets = [50, 100, 200, 300, 400, 500, 750, 1000, 2000];

    [McpServerTool(Name = "finviz_screen_stocks", ReadOnly = true, Idempotent = true)]
    [Description(
        "Run the Finviz stock screener and return the matching stocks as CSV. " +
        "Filters use Finviz codes joined by commas, e.g. 'cap_largeover,fa_div_o3,ta_rsi_os30,sh_avgvol_o500'. " +
        "Discover codes in the Finviz screener URL (the f= parameter).")]
    public Task<string> ScreenStocks(
        [Description("Comma-separated Finviz filter codes (the f= URL parameter), e.g. 'sec_technology,cap_midover,fa_pe_u25'. Empty returns the whole universe.")]
        string? filters = null,
        [Description("Screener view code (the v= URL parameter): 111 Overview, 121 Valuation, 131 Ownership, 141 Performance, 161 Financial, 171 Technical, 152 Custom (uses 'columns'). Default 111.")]
        string view = "111",
        [Description("Sort order (the o= URL parameter), e.g. 'marketcap', '-change' (prefix '-' for descending).")]
        string? order = null,
        [Description("Comma-separated column numbers for the Custom view (the c= URL parameter), e.g. '1,2,6,7,65,66'. Use with view=152.")]
        string? columns = null,
        [Description("Optional comma-separated tickers to restrict the screen to, e.g. 'AAPL,MSFT'.")]
        string? tickers = null,
        [Description("Optional Finviz signal code (the s= URL parameter), e.g. 'ta_topgainers', 'ta_newhigh'.")]
        string? signal = null,
        [Description("Maximum number of rows to return (default 50).")]
        int limit = 50,
        CancellationToken cancellationToken = default) =>
        finviz.ScreenerAsync(
            FinvizClient.OptionalToken(filters, nameof(filters)),
            FinvizClient.OptionalToken(view, nameof(view)) ?? "111",
            FinvizClient.OptionalToken(order, nameof(order)),
            FinvizClient.OptionalToken(columns, nameof(columns)),
            FinvizClient.OptionalTickerList(tickers),
            FinvizClient.OptionalToken(signal, nameof(signal)),
            ClampLimit(limit), cancellationToken);

    [McpServerTool(Name = "finviz_get_premarket_movers", ReadOnly = true, Idempotent = true)]
    [Description(
        "List the biggest pre-market (extended-hours) movers as CSV, ranked by percent change from the previous close. " +
        "Finviz Elite reports extended-hours prices in the Change/Price/Volume columns outside regular trading hours " +
        "(pre-market 4:00-9:30 AM ET, after-hours 4:00-8:00 PM ET); during regular hours the same call returns the intraday movers. " +
        "Liquidity filters exclude illiquid names that print huge percentage moves on a handful of shares.")]
    public Task<string> GetPremarketMovers(
        [Description("'losers' for the biggest decliners (default) or 'gainers' for the biggest advancers.")]
        string direction = "losers",
        [Description("Number of stocks to return (default 10).")]
        int limit = 10,
        [Description("Minimum share price in USD. Must be a Finviz preset: 1, 2, 3, 4, 5, 7, 10, 15, 20, 30, 40, 50, 60, 70, 80, 90 or 100. Default 1.")]
        int minPrice = 1,
        [Description("Minimum average daily volume in thousands of shares. Must be a Finviz preset: 50, 100, 200, 300, 400, 500, 750, 1000 or 2000. Default 200 (= 200K shares).")]
        int minAvgVolumeK = 200,
        [Description("Optional additional Finviz filter codes to AND with the liquidity filters, e.g. 'cap_midover,sec_technology,geo_usa'.")]
        string? extraFilters = null,
        [Description("Screener view code (default 111 Overview: Company, Sector, Industry, Country, Market Cap, P/E, Price, Change, Volume).")]
        string view = "111",
        CancellationToken cancellationToken = default)
    {
        var filters = BuildPremarketFilters(minPrice, minAvgVolumeK, extraFilters);
        var order = ParseMoverDirection(direction);

        return finviz.ScreenerAsync(
            filters,
            FinvizClient.OptionalToken(view, nameof(view)) ?? "111",
            order,
            columns: null, tickers: null, signal: null,
            ClampLimit(limit), cancellationToken);
    }

    /// <summary>Maps 'losers'/'gainers' to the Finviz sort order on the Change column.</summary>
    internal static string ParseMoverDirection(string direction) =>
        (direction ?? "").Trim().ToLowerInvariant() switch
        {
            "losers" or "decliners" or "down" => "change",   // ascending: most negative first
            "gainers" or "advancers" or "up" => "-change",   // descending: most positive first
            _ => throw new McpException($"Invalid direction '{direction}'. Use 'losers' or 'gainers'."),
        };

    /// <summary>Builds the screener filter string for the pre-market movers tool.</summary>
    internal static string BuildPremarketFilters(int minPrice, int minAvgVolumeK, string? extraFilters)
    {
        if (!MinPricePresets.Contains(minPrice))
        {
            throw new McpException($"Invalid minPrice {minPrice}. Valid presets: {string.Join(", ", MinPricePresets)}.");
        }

        if (!MinAvgVolumePresets.Contains(minAvgVolumeK))
        {
            throw new McpException($"Invalid minAvgVolumeK {minAvgVolumeK}. Valid presets: {string.Join(", ", MinAvgVolumePresets)}.");
        }

        var filters = $"sh_price_o{minPrice},sh_avgvol_o{minAvgVolumeK}";
        var extra = FinvizClient.OptionalToken(extraFilters, nameof(extraFilters));
        return extra is null ? filters : $"{filters},{extra}";
    }

    [McpServerTool(Name = "finviz_get_price_history", ReadOnly = true, Idempotent = true)]
    [Description("Get OHLCV candles for a ticker as CSV (Date, Open, High, Low, Close, Volume). Returns the most recent rows.")]
    public Task<string> GetPriceHistory(
        [Description("Ticker symbol, e.g. 'AAPL'.")] string ticker,
        [Description("Timeframe: i1, i3, i5, i15, i30 (intraday minutes), h (hourly), d (daily), w (weekly), m (monthly). Default d.")]
        string timeframe = "d",
        [Description("Maximum number of most-recent candles to return (default 100).")]
        int limit = 100,
        CancellationToken cancellationToken = default)
    {
        if (!Timeframes.Contains(timeframe))
        {
            throw new McpException($"Invalid timeframe '{timeframe}'. Valid: {string.Join(", ", Timeframes)}.");
        }

        return finviz.QuoteHistoryAsync(
            FinvizClient.RequireTicker(ticker), timeframe.ToLowerInvariant(), ClampLimit(limit), cancellationToken);
    }

    [McpServerTool(Name = "finviz_get_news", ReadOnly = true, Idempotent = true)]
    [Description("Get Finviz market news headlines as CSV (title, source, date, url, category), optionally for specific tickers.")]
    public Task<string> GetNews(
        [Description("News view code (the v= parameter of finviz.com/news.ashx). Default 1.")]
        string view = "1",
        [Description("Optional comma-separated tickers to restrict news to, e.g. 'NVDA,AMD'.")]
        string? tickers = null,
        [Description("Maximum number of rows to return (default 50).")]
        int limit = 50,
        CancellationToken cancellationToken = default) =>
        finviz.NewsAsync(
            FinvizClient.OptionalToken(view, nameof(view)) ?? "1",
            FinvizClient.OptionalTickerList(tickers),
            ClampLimit(limit), cancellationToken);

    [McpServerTool(Name = "finviz_get_group_performance", ReadOnly = true, Idempotent = true)]
    [Description("Get performance/valuation statistics for market groups (sectors, industries, countries, capitalization buckets) as CSV.")]
    public Task<string> GetGroupPerformance(
        [Description("Group type: sector, industry, country or capitalization. Default sector.")]
        string group = "sector",
        [Description("Groups view code (the v= URL parameter): 110 Overview, 120 Valuation, 140 Performance, 150 Custom, 160 Financial. Default 140.")]
        string view = "140",
        [Description("Sort order (the o= URL parameter), e.g. '-perf1w'. Prefix '-' for descending.")]
        string? order = null,
        [Description("Maximum number of rows to return (default 100).")]
        int limit = 100,
        CancellationToken cancellationToken = default)
    {
        if (!GroupNames.Contains(group))
        {
            throw new McpException($"Invalid group '{group}'. Valid: {string.Join(", ", GroupNames)}.");
        }

        return finviz.GroupsAsync(
            group.ToLowerInvariant(),
            FinvizClient.OptionalToken(view, nameof(view)) ?? "140",
            FinvizClient.OptionalToken(order, nameof(order)),
            ClampLimit(limit), cancellationToken);
    }

    [McpServerTool(Name = "finviz_get_portfolio", ReadOnly = true, Idempotent = true)]
    [Description("Export one of your Finviz Elite portfolios (positions, cost basis, P/L) as CSV.")]
    public Task<string> GetPortfolio(
        [Description("Numeric portfolio id (the pid= value in the portfolio page URL).")]
        string portfolioId,
        [Description("Maximum number of rows to return (default 200).")]
        int limit = 200,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(portfolioId) || !portfolioId.All(char.IsAsciiDigit))
        {
            throw new McpException("portfolioId must be numeric.");
        }

        return finviz.PortfolioAsync(portfolioId, ClampLimit(limit), cancellationToken);
    }

    private static int ClampLimit(int limit) => Math.Clamp(limit, 1, 1000);
}
