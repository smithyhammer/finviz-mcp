using FinvizMcpServer.Finviz;
using FinvizMcpServer.Http;
using FinvizMcpServer.Tools;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ModelContextProtocol.AspNetCore;

// MCP_TRANSPORT selects how clients connect:
//   stdio (default) - the client launches the server and talks over stdin/stdout.
//   http            - Streamable HTTP at http://MCP_HOST:MCP_PORT/mcp.
//   sse             - http plus the legacy HTTP+SSE endpoints (/mcp/sse, /mcp/message) for older clients.
var transport = (Environment.GetEnvironmentVariable("MCP_TRANSPORT") ?? "stdio").Trim().ToLowerInvariant();

switch (transport)
{
    case "stdio":
    {
        var builder = Host.CreateApplicationBuilder(args);
        AddFinvizServices(builder).WithStdioServerTransport();
        await builder.Build().RunAsync();
        return 0;
    }

    case "http" or "sse":
    {
        var builder = WebApplication.CreateBuilder(args);
        var config = builder.Configuration;
        var host = config["MCP_HOST"] ?? "127.0.0.1";
        var port = config["MCP_PORT"] ?? "8000";

        // Reject requests whose Host header isn't local, which blocks DNS-rebinding attacks from web pages.
        // Set MCP_ALLOWED_HOSTS (semicolon-separated, or "*") when clients reach the server by another name.
        builder.Configuration["AllowedHosts"] = config["MCP_ALLOWED_HOSTS"] ?? "localhost;127.0.0.1;[::1]";

        AddFinvizServices(builder).WithHttpTransport(o =>
        {
            if (transport != "sse") return;

            // Legacy SSE is opt-in for older clients; the SDK flags it as obsolete. It pairs the GET /sse stream
            // with POST /message through in-memory sessions, so it can't run in the default stateless mode.
            o.SessionMode = HttpServerSessionMode.StatefulForInitializeClients;
#pragma warning disable MCP9004
            o.EnableLegacySse = true;
#pragma warning restore MCP9004
        });

        var app = builder.Build();
        app.Urls.Add($"http://{host}:{port}");
        // Lets clients POST without listing both application/json and text/event-stream in Accept.
        app.UseWhen(
            ctx => ctx.Request.Path.Equals("/mcp", StringComparison.OrdinalIgnoreCase) ||
                   ctx.Request.Path.Equals("/mcp/", StringComparison.OrdinalIgnoreCase),
            branch => branch.UseMiddleware<McpAcceptHeaderMiddleware>());
        app.MapMcp("/mcp");
        await app.RunAsync();
        return 0;
    }

    default:
        Console.Error.WriteLine($"Unknown MCP_TRANSPORT '{transport}'. Use 'stdio', 'http' or 'sse'.");
        return 1;
}

static IMcpServerBuilder AddFinvizServices(IHostApplicationBuilder builder)
{
    // Configure all logs to go to stderr (stdout is used for the MCP protocol messages in stdio mode).
    builder.Logging.AddConsole(o => o.LogToStandardErrorThreshold = LogLevel.Trace);

    // HttpClient logging would write full request URIs, which contain the Finviz API token (auth=...).
    builder.Logging.AddFilter("System.Net.Http.HttpClient", LogLevel.None);

    builder.Services.Configure<FinvizOptions>(o =>
    {
        var config = builder.Configuration;
        o.ApiKey = config["FINVIZ_API_KEY"] ?? config["Finviz:ApiKey"];
        o.BaseUrl = config["FINVIZ_BASE_URL"] ?? config["Finviz:BaseUrl"] ?? o.BaseUrl;
    });

    // One limiter for the whole process: Finviz's rate limit is per account, across all endpoints.
    builder.Services.AddSingleton<FinvizRateLimiter>();

    builder.Services.AddHttpClient<FinvizClient>((sp, http) =>
    {
        var options = sp.GetRequiredService<IOptions<FinvizOptions>>().Value;
        http.BaseAddress = new Uri(options.BaseUrl);
        http.Timeout = TimeSpan.FromSeconds(30);
        // Finviz rejects requests without a User-Agent.
        http.DefaultRequestHeaders.UserAgent.ParseAdd("FinvizMcpServer/0.1 (+https://modelcontextprotocol.io)");
    });

    return builder.Services
        .AddMcpServer()
        .WithTools<FinvizTools>();
}
