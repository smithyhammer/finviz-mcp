using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Net.Http.Headers;

namespace FinvizMcpServer.Http;

/// <summary>
/// Relaxes the Streamable HTTP Accept requirement. The MCP SDK answers HTTP 406 to any POST whose Accept header
/// doesn't list both application/json and text/event-stream, which rejects simple HTTP clients (curl, scripts,
/// some MCP clients). This middleware fills in the missing media types before the SDK sees the request:
/// <list type="bullet">
/// <item>Clients that accept text/event-stream get the SDK's SSE response unchanged.</item>
/// <item>Other clients (application/json only, */*, or no Accept header) get the JSON-RPC response as a plain
/// application/json body, converted from the SDK's buffered SSE stream.</item>
/// </list>
/// </summary>
public sealed class McpAcceptHeaderMiddleware(RequestDelegate next)
{
    private const string Json = "application/json";
    private const string EventStream = "text/event-stream";
    private const string BothMediaTypes = Json + ", " + EventStream;

    public async Task InvokeAsync(HttpContext context)
    {
        if (!HttpMethods.IsPost(context.Request.Method))
        {
            await next(context);
            return;
        }

        var accept = context.Request.GetTypedHeaders().Accept;
        if (accept.Any(m => m.MediaType.Equals(EventStream, StringComparison.OrdinalIgnoreCase)))
        {
            context.Request.Headers.Accept = BothMediaTypes;
            await next(context);
            return;
        }

        context.Request.Headers.Accept = BothMediaTypes;
        var originalBody = context.Response.Body;
        using var buffer = new MemoryStream();
        context.Response.Body = buffer;
        try
        {
            await next(context);
        }
        finally
        {
            context.Response.Body = originalBody;
        }

        buffer.Position = 0;
        var isSse = context.Response.ContentType?.StartsWith(EventStream, StringComparison.OrdinalIgnoreCase) == true;
        if (!isSse)
        {
            context.Response.ContentLength = buffer.Length;
            await buffer.CopyToAsync(originalBody, context.RequestAborted);
            return;
        }

        var json = SelectResponse(ParseSseData(Encoding.UTF8.GetString(buffer.ToArray())));
        if (json is null)
        {
            // The stream carried no messages; reply the way the spec does for notifications.
            context.Response.StatusCode = StatusCodes.Status202Accepted;
            context.Response.ContentType = null;
            context.Response.ContentLength = 0;
            return;
        }

        var bytes = Encoding.UTF8.GetBytes(json);
        context.Response.ContentType = Json;
        context.Response.ContentLength = bytes.Length;
        await originalBody.WriteAsync(bytes, context.RequestAborted);
    }

    /// <summary>Returns the data payload of each event in an SSE stream (multi-line data joined with '\n').</summary>
    internal static List<string> ParseSseData(string stream)
    {
        var events = new List<string>();
        var data = new StringBuilder();
        foreach (var rawLine in stream.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');
            if (line.Length == 0)
            {
                if (data.Length > 0)
                {
                    events.Add(data.ToString());
                    data.Clear();
                }
            }
            else if (line.StartsWith("data:", StringComparison.Ordinal))
            {
                if (data.Length > 0) data.Append('\n');
                data.Append(line.AsSpan(5).TrimStart(' '));
            }
        }

        if (data.Length > 0) events.Add(data.ToString());
        return events;
    }

    /// <summary>
    /// Picks the JSON-RPC response (the message with "result" or "error") from the stream. Notifications sent before
    /// it, such as log messages, can't be delivered in a single JSON body and are dropped.
    /// </summary>
    internal static string? SelectResponse(List<string> messages)
    {
        foreach (var message in messages)
        {
            try
            {
                using var doc = JsonDocument.Parse(message);
                if (doc.RootElement.ValueKind == JsonValueKind.Object &&
                    (doc.RootElement.TryGetProperty("result", out _) || doc.RootElement.TryGetProperty("error", out _)))
                {
                    return message;
                }
            }
            catch (JsonException)
            {
                // Not JSON; skip it.
            }
        }

        return messages.LastOrDefault();
    }
}
