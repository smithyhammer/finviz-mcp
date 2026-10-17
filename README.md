# Finviz MCP Server

A [Model Context Protocol](https://modelcontextprotocol.io) server (C# / .NET 10, stdio transport) that exposes the
[Finviz Elite](https://finviz.com/elite) CSV export API as MCP tools.

> Requires a **Finviz Elite** subscription. Your API token is on your Finviz account page.

## Tools

| Tool | Finviz endpoint | Description |
| --- | --- | --- |
| `finviz_screen_stocks` | `/export` | Run the screener with Finviz filter codes (`f=`), view (`v=`), sort (`o=`), custom columns (`c=`), signal (`s=`), tickers (`t=`) |
| `finviz_get_premarket_movers` | `/export` | Biggest pre-market / after-hours gainers or losers by % change, with liquidity filters (min price, min average volume) |
| `finviz_get_price_history` | `/quote_export` | OHLCV candles; timeframes `i1,i3,i5,i15,i30,h,d,w,m` |
| `finviz_get_news` | `/news_export` | News headlines, optionally per ticker |
| `finviz_get_group_performance` | `/grp_export` | Sector / industry / country / capitalization stats |
| `finviz_get_portfolio` | `/portfolio_export` | Export one of your portfolios |

All tools return CSV text (header row + data rows) and take a `limit` argument to cap rows.

The legacy `*.ashx` paths (e.g. `export.ashx`) still work but now 301-redirect to these extensionless paths. Finviz's
**API and Exports** dashboard (account menu → *API and Exports*) generates request URLs for each product and shows your usage.

## Rate limits

Finviz Elite allows **10 requests per rolling 5-second window**, shared across all Export/API endpoints on the account
(including other scripts or apps that use the same token). When you exceed it, Finviz returns HTTP 429 with
`Retry-After: 5`. Rejected requests don't count against the limit.

The server throttles requests on its side to stay within the budget. If Finviz still returns 429, for example because
another app is using the same token, the server waits for the `Retry-After` delay and tries again, up to 3 attempts in
total, before returning an error.

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

## Run in Docker

The image is a self-contained single-file build on a chiseled (distroless, non-root) Ubuntu base, about 130 MB.

```powershell
docker build -t finviz-mcp-server .
# Multi-arch: docker buildx build --platform linux/amd64,linux/arm64 -t <registry>/finviz-mcp-server --push .
```

The server talks MCP over stdio, so the client launches the container itself. Pass `-i` and leave out `-t`, which would corrupt the stream:

```json
{
  "mcpServers": {
    "finviz": {
      "command": "docker",
      "args": ["run", "-i", "--rm", "-e", "FINVIZ_API_KEY", "finviz-mcp-server"],
      "env": { "FINVIZ_API_KEY": "<your token>" }
    }
  }
}
```

`-e FINVIZ_API_KEY` with no value forwards the variable from the client's environment, so the token never appears in the
`docker run` command line.

## Pack as a NuGet MCP server

```powershell
dotnet pack -c Release
```

Then publish the package to NuGet.org; update `.mcp/server.json` with your GitHub user/repo first.
