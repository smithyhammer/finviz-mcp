# Finviz MCP Server

A [Model Context Protocol](https://modelcontextprotocol.io) server (C# / .NET 10, stdio or HTTP transport) that exposes the
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
| `MCP_TRANSPORT` | Optional. `stdio` (default), `http` (Streamable HTTP), or `sse` (Streamable HTTP plus the legacy HTTP+SSE endpoints) |
| `MCP_HOST` | HTTP only. Interface to listen on. Defaults to `127.0.0.1` (`0.0.0.0` in the Docker image) |
| `MCP_PORT` | HTTP only. Defaults to `8000` |
| `MCP_ALLOWED_HOSTS` | HTTP only. Semicolon-separated `Host` header values to accept. Defaults to `localhost;127.0.0.1;[::1]` |

The token is sent as the `auth` query parameter. HttpClient logging is disabled so it never appears in logs.

## HTTP transport

Set `MCP_TRANSPORT=http` to run the server as a long-lived process that clients connect to over
[Streamable HTTP](https://modelcontextprotocol.io/specification/2025-11-25/basic/transports#streamable-http):

```powershell
$env:MCP_TRANSPORT = "http"
dotnet run
# MCP endpoint: http://127.0.0.1:8000/mcp
```

```json
{
  "mcpServers": {
    "finviz": { "type": "http", "url": "http://localhost:8000/mcp" }
  }
}
```

For clients that only support the older HTTP+SSE transport, use `MCP_TRANSPORT=sse`. This also serves
`GET /mcp/sse` and `POST /mcp/message`. The SDK marks legacy SSE as obsolete because it has no backpressure, so use it
only with trusted clients.

> **The HTTP endpoint has no authentication.** Anyone who can reach it can use your Finviz token and its shared rate
> limit. Keep it on localhost, which is the default. Requests whose `Host` header isn't local are rejected (HTTP 400),
> which blocks DNS-rebinding attacks from web pages. If you expose it beyond localhost, put an authenticating reverse
> proxy in front and set `MCP_ALLOWED_HOSTS` to the hostname clients use.

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

The image is a self-contained single-file build on a chiseled (distroless, non-root) Ubuntu base, about 170 MB.

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

To run it as a background HTTP service instead, publish the port on localhost only:

```powershell
$env:FINVIZ_API_KEY = "<your token>"
docker run -d --name finviz-mcp --restart unless-stopped -p 127.0.0.1:8000:8000 `
  -e MCP_TRANSPORT=http -e FINVIZ_API_KEY finviz-mcp-server
```

Clients then connect to `http://localhost:8000/mcp`. Binding to `127.0.0.1` on the host is important because the
endpoint has no authentication. `-p 8000:8000` would expose it to your whole network.

## Pack as a NuGet MCP server

```powershell
dotnet pack -c Release
```

Then publish the package to NuGet.org; update `.mcp/server.json` with your GitHub user/repo first.
