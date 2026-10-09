using FinvizMcpServer.Finviz;
using FinvizMcpServer.Tools;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

var builder = Host.CreateApplicationBuilder(args);

// Configure all logs to go to stderr (stdout is used for the MCP protocol messages).
builder.Logging.AddConsole(o => o.LogToStandardErrorThreshold = LogLevel.Trace);

// HttpClient logging would write full request URIs, which contain the Finviz API token (auth=...).
builder.Logging.AddFilter("System.Net.Http.HttpClient", LogLevel.None);

builder.Services.Configure<FinvizOptions>(o =>
{
    var config = builder.Configuration;
    o.ApiKey = config["FINVIZ_API_KEY"] ?? config["Finviz:ApiKey"];
    o.BaseUrl = config["FINVIZ_BASE_URL"] ?? config["Finviz:BaseUrl"] ?? o.BaseUrl;
});

builder.Services.AddHttpClient<FinvizClient>((sp, http) =>
{
    var options = sp.GetRequiredService<IOptions<FinvizOptions>>().Value;
    http.BaseAddress = new Uri(options.BaseUrl);
    http.Timeout = TimeSpan.FromSeconds(30);
    // Finviz rejects requests without a User-Agent.
    http.DefaultRequestHeaders.UserAgent.ParseAdd("FinvizMcpServer/0.1 (+https://modelcontextprotocol.io)");
});

// Add the MCP services: the transport to use (stdio) and the tools to register.
builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithTools<FinvizTools>();

await builder.Build().RunAsync();
