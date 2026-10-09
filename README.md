# Finviz MCP Server

A [Model Context Protocol](https://modelcontextprotocol.io) server (C# / .NET 10, stdio transport) that exposes the
[Finviz Elite](https://finviz.com/elite) CSV export API as MCP tools.

> Requires a **Finviz Elite** subscription. Your API token is on your Finviz account page.

## Tools

| Tool | Finviz endpoint | Description |
| --- | --- | --- |
| `finviz_screen_stocks` | `export.ashx` | Run the screener with Finviz filter codes (`f=`), view (`v=`), sort (`o=`), custom columns (`c=`), signal (`s=`), tickers (`t=`) |
| `finviz_get_premarket_movers` | `export.ashx` | Biggest pre-market / after-hours gainers or losers by % change, with liquidity filters (min price, min average volume) |
| `finviz_get_price_history` | `quote_export.ashx` | OHLCV candles; timeframes `i1,i3,i5,i15,i30,h,d,w,m` |
| `finviz_get_news` | `news_export.ashx` | News headlines, optionally per ticker |
| `finviz_get_group_performance` | `grp_export.ashx` | Sector / industry / country / capitalization stats |
| `finviz_get_portfolio` | `portfolio_export.ashx` | Export one of your portfolios |

All tools return CSV text (header row + data rows) and take a `limit` argument to cap rows.

## Configuration

| Setting | Description |
| --- | --- |
| `FINVIZ_API_KEY` | **Required.** Finviz Elite API token (also accepted as `Finviz:ApiKey`) |
| `FINVIZ_BASE_URL` | Optional. Defaults to `https://elite.finviz.com/` |

The token is sent as the `auth` query parameter. HttpClient logging is disabled so it never appears in logs.

## Build & run

```powershell
dotnet build
$env:FINVIZ_API_KEY = "<your token>"
dotnet run
```

## Use from an MCP client

```json
{
  "mcpServers": {
    "finviz": {
      "command": "dotnet",
      "args": ["run", "--project", "C:\\Code\\finviz-mcp-server", "--no-build"],
      "env": { "FINVIZ_API_KEY": "<your token>" }
    }
  }
}
```

Or publish a self-contained single-file executable and point `command` at it:

```powershell
dotnet publish -c Release -r win-x64 -o publish
```

## Pack as a NuGet MCP server

```powershell
dotnet pack -c Release
```

Then publish the package to NuGet.org; update `.mcp/server.json` with your GitHub user/repo first.
