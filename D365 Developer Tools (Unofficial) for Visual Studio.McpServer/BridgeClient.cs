using System.Net.Http.Headers;
using System.Text.Json;

namespace D365DeveloperTools.McpServer;

internal sealed record Credentials(string Token, string EnvironmentUrl);

/// <summary>
/// Reads the bridge state (port + nonce) the Visual Studio extension writes while it holds an active
/// Dataverse connection, and asks it for a fresh access token on every call. This process never stores
/// or manages credentials itself — it always uses whatever session is currently live in Visual Studio.
/// </summary>
internal sealed class BridgeClient
{
    private static readonly string BridgeFilePath = Environment.GetEnvironmentVariable("D365_BRIDGE_FILE")
        ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "D365DeveloperTools",
            "mcp-bridge.json");

    private static readonly HttpClient Http = new();
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public async Task<Credentials> GetCredentialsAsync(CancellationToken cancellationToken)
    {
        BridgeState state;
        try
        {
            var json = await File.ReadAllTextAsync(BridgeFilePath, cancellationToken).ConfigureAwait(false);
            state = JsonSerializer.Deserialize<BridgeState>(json, JsonOptions)
                ?? throw new InvalidOperationException("Bridge file is empty.");
        }
        catch (Exception ex) when (ex is IOException or JsonException or InvalidOperationException)
        {
            throw new InvalidOperationException(
                "D365 extension is not connected. Open Visual Studio, connect to a Dataverse environment " +
                $"via D365 Developer Tools, then retry.\n(Bridge file expected at: {BridgeFilePath})");
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, $"http://127.0.0.1:{state.Port}/token");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", state.Nonce);

        using var response = await Http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Bridge returned {(int)response.StatusCode}: {body}");
        }

        var payload = JsonSerializer.Deserialize<TokenResponse>(body, JsonOptions)
            ?? throw new InvalidOperationException("Bridge returned an empty token response.");
        return new Credentials(payload.Token, payload.EnvironmentUrl);
    }

    private sealed class BridgeState
    {
        public int Port { get; set; }
        public string Nonce { get; set; } = string.Empty;
    }

    private sealed class TokenResponse
    {
        public string Token { get; set; } = string.Empty;
        public string EnvironmentUrl { get; set; } = string.Empty;
    }
}
