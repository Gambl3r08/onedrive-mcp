using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ModelContextProtocol;
using OneDriveMcp.Auth;

namespace OneDriveMcp.Graph;

// Thin Microsoft Graph REST wrapper. Every failure surfaces as McpException so the
// message reaches the MCP client (is_error = true) instead of a generic error.
public sealed class GraphClient(HttpClient http, GraphTokenProvider tokens)
{
    public Task<JsonElement> GetAsync(string relative, CancellationToken ct) =>
        SendAsync(HttpMethod.Get, relative, null, ct);

    public async Task<JsonElement> SendAsync(HttpMethod method, string relative, HttpContent? content, CancellationToken ct)
    {
        using var request = await CreateRequestAsync(method, relative, ct);
        request.Content = content;
        using var response = await http.SendAsync(request, ct);
        await EnsureSuccessAsync(response, ct);
        return await response.Content.ReadFromJsonAsync<JsonElement>(ct);
    }

    public Task<JsonElement> SendJsonAsync(HttpMethod method, string relative, object body, CancellationToken ct) =>
        SendAsync(method, relative, JsonContent.Create(body), ct);

    // Reads at most maxBytes of a file's content. The /content endpoint answers with a
    // 302 to a pre-authenticated URL; HttpClient follows it and drops the auth header.
    public async Task<(byte[] Data, bool Truncated)> DownloadAsync(string relative, int maxBytes, CancellationToken ct)
    {
        using var request = await CreateRequestAsync(HttpMethod.Get, relative, ct);
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        await EnsureSuccessAsync(response, ct);

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        var buffer = new byte[maxBytes + 1];
        var read = 0;
        int n;
        while (read < buffer.Length && (n = await stream.ReadAsync(buffer.AsMemory(read), ct)) > 0)
            read += n;

        var truncated = read > maxBytes;
        return (buffer[..Math.Min(read, maxBytes)], truncated);
    }

    private async Task<HttpRequestMessage> CreateRequestAsync(HttpMethod method, string relative, CancellationToken ct)
    {
        string token;
        try
        {
            token = await tokens.GetAccessTokenAsync(ct);
        }
        catch (NotSignedInException ex)
        {
            throw new McpException(
                $"OneDrive is not signed in ({ex.Message}). An operator must run: docker compose run --rm onedrive-mcp login");
        }

        var request = new HttpRequestMessage(method, relative);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return request;
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode) return;

        var detail = response.ReasonPhrase ?? "error";
        try
        {
            var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
            if (body.TryGetProperty("error", out var error))
                detail = $"{error.GetProperty("code").GetString()}: {error.GetProperty("message").GetString()}";
        }
        catch (JsonException)
        {
            // Non-JSON error body; keep the reason phrase.
        }

        throw new McpException($"Microsoft Graph returned {(int)response.StatusCode} ({detail}).");
    }
}
