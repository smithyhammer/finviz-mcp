namespace FinvizMcpServer.Finviz;

/// <summary>Configuration for the Finviz Elite export API.</summary>
public sealed class FinvizOptions
{
    /// <summary>Base address of the Finviz Elite export endpoints.</summary>
    public string BaseUrl { get; set; } = "https://elite.finviz.com/";

    /// <summary>
    /// Finviz Elite API token (shown on your Finviz account page).
    /// Read from the FINVIZ_API_KEY environment variable or the Finviz:ApiKey setting.
    /// </summary>
    public string? ApiKey { get; set; }
}
