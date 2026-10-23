namespace OneDriveMcp;

// Bound from the "OneDrive" configuration section (env: OneDrive__TenantId, ...).
public sealed class OneDriveOptions
{
    // Entra ID tenant (GUID or domain) of the OneDrive for Business account.
    public string TenantId { get; set; } = string.Empty;

    // Client id of a *public client* app registration ("Allow public client flows" = Yes).
    public string ClientId { get; set; } = string.Empty;

    // Shared secret MCP clients (Flow Weaver) must send as X-API-Key or Bearer.
    public string ApiKey { get; set; } = string.Empty;

    // When true only read tools are exposed and the login requests Files.Read.
    public bool ReadOnly { get; set; }

    // Sharing links default to the organization; anonymous links need an explicit opt-in.
    public bool AllowAnonymousLinks { get; set; }

    // MSAL token cache (holds the refresh token). Lives on the /data volume.
    public string CachePath { get; set; } = "/data/msal_cache.bin";

    public int MaxReadBytes { get; set; } = 1024 * 1024;

    // Graph "simple upload" path; larger files would need an upload session.
    public int MaxUploadBytes { get; set; } = 4 * 1024 * 1024;

    public string[] Scopes => ReadOnly
        ? ["User.Read", "Files.Read"]
        : ["User.Read", "Files.ReadWrite"];

    public void Validate()
    {
        var missing = new List<string>();
        if (string.IsNullOrWhiteSpace(TenantId)) missing.Add("OneDrive__TenantId");
        if (string.IsNullOrWhiteSpace(ClientId)) missing.Add("OneDrive__ClientId");
        if (missing.Count > 0)
            throw new InvalidOperationException($"Missing configuration: {string.Join(", ", missing)}");
    }
}
