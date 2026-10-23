using System.Security.Cryptography;
using System.Text;
using Microsoft.Identity.Client;
using OneDriveMcp;
using OneDriveMcp.Auth;
using OneDriveMcp.Graph;
using OneDriveMcp.Tools;

// Usage:
//   OneDriveMcp            -> run the MCP server (Streamable HTTP on /mcp)
//   OneDriveMcp login      -> device-code sign-in, stores the token cache and exits
//   OneDriveMcp logout     -> clears the token cache
//   OneDriveMcp status     -> prints the signed-in account
string[] commands = ["login", "logout", "status"];
var command = args.Length > 0 && commands.Contains(args[0]) ? args[0] : null;

var builder = WebApplication.CreateBuilder(command is null ? args : args[1..]);

builder.Services.Configure<OneDriveOptions>(builder.Configuration.GetSection("OneDrive"));
var options = builder.Configuration.GetSection("OneDrive").Get<OneDriveOptions>() ?? new OneDriveOptions();

builder.Services.AddSingleton<GraphTokenProvider>();
builder.Services.AddHttpClient<GraphClient>(c =>
{
    c.BaseAddress = new Uri("https://graph.microsoft.com/v1.0/");
    c.Timeout = TimeSpan.FromSeconds(60);
});

var mcp = builder.Services
    .AddMcpServer(o => o.ServerInfo = new() { Name = "onedrive-mcp", Version = "0.1.0" })
    .WithHttpTransport(o => o.Stateless = true)
    .WithTools<OneDriveReadTools>();
if (!options.ReadOnly)
    mcp.WithTools<OneDriveWriteTools>();

var app = builder.Build();

if (command is not null)
    return await RunCommandAsync(command, app.Services.GetRequiredService<GraphTokenProvider>());

if (string.IsNullOrWhiteSpace(options.ApiKey) || options.ApiKey.Length < 24)
    throw new InvalidOperationException("OneDrive__ApiKey must be set (at least 24 characters).");

var expectedKey = Encoding.UTF8.GetBytes(options.ApiKey);

// Only /mcp is protected; /health stays open for container health checks.
app.Use(async (ctx, next) =>
{
    if (ctx.Request.Path.StartsWithSegments("/mcp"))
    {
        var provided = ctx.Request.Headers["X-API-Key"].ToString();
        var authorization = ctx.Request.Headers.Authorization.ToString();
        if (provided.Length == 0 && authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            provided = authorization["Bearer ".Length..].Trim();

        if (!CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(provided), expectedKey))
        {
            ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }
    }
    await next();
});

app.MapGet("/health", async (GraphTokenProvider tokens) =>
    Results.Ok(new { status = "ok", signedIn = await tokens.GetSignedInUserAsync() is not null, readOnly = options.ReadOnly }));

app.MapMcp("/mcp");

app.Run();
return 0;

static async Task<int> RunCommandAsync(string command, GraphTokenProvider tokens)
{
    switch (command)
    {
        case "login":
            try
            {
                var result = await tokens.LoginWithDeviceCodeAsync(code =>
                {
                    Console.WriteLine();
                    Console.WriteLine(code.Message);
                    Console.WriteLine();
                    return Task.CompletedTask;
                }, CancellationToken.None);
                Console.WriteLine($"Signed in as {result.Account.Username}. Token cache saved.");
                return 0;
            }
            catch (MsalServiceException ex)
            {
                Console.Error.WriteLine($"Sign-in failed ({ex.ErrorCode}): {ex.Message}");
                if (ex.Message.Contains("AADSTS7000218"))
                    Console.Error.WriteLine("Fix: in the app registration, Authentication -> Allow public client flows = Yes.");
                return 1;
            }
        case "logout":
            await tokens.LogoutAsync();
            Console.WriteLine("Signed out; token cache cleared.");
            return 0;
        default:
            var user = await tokens.GetSignedInUserAsync();
            Console.WriteLine(user is null ? "Not signed in." : $"Signed in as {user}.");
            return user is null ? 1 : 0;
    }
}
