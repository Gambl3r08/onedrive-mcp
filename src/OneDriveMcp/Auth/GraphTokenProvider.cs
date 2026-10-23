using Microsoft.Extensions.Options;
using Microsoft.Identity.Client;

namespace OneDriveMcp.Auth;

public sealed class NotSignedInException(string message, Exception? inner = null) : Exception(message, inner);

// Delegated Graph tokens for a single signed-in user. The interactive part (device
// code) only runs from the `login` CLI command; the server itself only does silent
// acquisition from the persisted cache, refreshing via the refresh token.
public sealed class GraphTokenProvider
{
    private readonly IPublicClientApplication _app;
    private readonly string[] _scopes;

    public GraphTokenProvider(IOptions<OneDriveOptions> options)
    {
        var o = options.Value;
        o.Validate();
        _scopes = o.Scopes;
        _app = PublicClientApplicationBuilder.Create(o.ClientId)
            .WithAuthority(AzureCloudInstance.AzurePublic, o.TenantId)
            .WithDefaultRedirectUri()
            .Build();
        FileTokenCache.Bind(_app.UserTokenCache, o.CachePath);
    }

    public async Task<string> GetAccessTokenAsync(CancellationToken ct)
    {
        var account = (await _app.GetAccountsAsync()).FirstOrDefault()
            ?? throw new NotSignedInException("No account is signed in.");
        try
        {
            var result = await _app.AcquireTokenSilent(_scopes, account).ExecuteAsync(ct);
            return result.AccessToken;
        }
        catch (MsalUiRequiredException ex)
        {
            throw new NotSignedInException("The stored sign-in expired or needs consent.", ex);
        }
    }

    public async Task<string?> GetSignedInUserAsync() =>
        (await _app.GetAccountsAsync()).FirstOrDefault()?.Username;

    public async Task<AuthenticationResult> LoginWithDeviceCodeAsync(
        Func<DeviceCodeResult, Task> onCode, CancellationToken ct) =>
        await _app.AcquireTokenWithDeviceCode(_scopes, onCode).ExecuteAsync(ct);

    public async Task LogoutAsync()
    {
        foreach (var account in await _app.GetAccountsAsync())
            await _app.RemoveAsync(account);
    }
}
