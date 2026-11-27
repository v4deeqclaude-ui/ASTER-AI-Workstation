using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddDataProtection();

var app = builder.Build();
var stateProtector = app.Services.GetRequiredService<IDataProtectionProvider>()
    .CreateProtector("ASTER.Connect.GitHub.InstallationState.v1");

app.MapGet("/", () => Results.Ok(new
{
    service = "ASTER Connect",
    version = "0.7.0",
    status = "starting",
    health = "/health/live"
}));

app.MapGet("/health/live", () => Results.Ok(new
{
    status = "alive",
    service = "ASTER Connect"
}));

app.MapGet("/health/ready", (IConfiguration configuration) =>
{
    var appSlug = configuration["GitHubApp:Slug"];
    var publicBaseUrl = configuration["PublicBaseUrl"];
    var missing = new List<string>();
    if (string.IsNullOrWhiteSpace(appSlug)) missing.Add("GitHubApp:Slug");
    if (string.IsNullOrWhiteSpace(publicBaseUrl)) missing.Add("PublicBaseUrl");

    return Results.Json(new
    {
        status = missing.Count == 0 ? "configured_for_installation_flow" : "configuration_required",
        service = "ASTER Connect",
        githubAppConfigured = missing.Count == 0,
        missingConfiguration = missing,
        note = "Installation callback validation is enabled. Token exchange, repository access and persistent credential storage are not enabled yet."
    }, statusCode: missing.Count == 0 ? StatusCodes.Status200OK : StatusCodes.Status503ServiceUnavailable);
});

app.MapGet("/api/github/status", (IConfiguration configuration) =>
{
    var configured = !string.IsNullOrWhiteSpace(configuration["GitHubApp:Slug"]);
    return Results.Ok(new
    {
        provider = "github",
        status = "not_connected",
        appConfigured = configured,
        installationFlowAvailable = configured,
        repositoryAccessAvailable = false,
        reason = configured
            ? "GitHub App installation can be started. Repository access is not enabled until installation verification and encrypted token storage are implemented."
            : "ASTER Connect is waiting for GitHubApp:Slug.",
        requiredPermissions = new[] { "Metadata: read-only", "Contents: read-only" }
    });
});

app.MapGet("/api/github/connect", (HttpContext http, IConfiguration configuration) =>
{
    var slug = configuration["GitHubApp:Slug"];
    if (string.IsNullOrWhiteSpace(slug))
        return Results.Problem(
            title: "GitHub App is not configured",
            detail: "ASTER Connect needs the GitHub App slug before it can open the official installation page.",
            statusCode: StatusCodes.Status503ServiceUnavailable);

    if (slug.Length > 100 || slug.Any(ch => !(char.IsAsciiLetterOrDigit(ch) || ch == '-')))
        return Results.Problem(
            title: "Invalid GitHub App configuration",
            detail: "GitHubApp:Slug contains unsupported characters.",
            statusCode: StatusCodes.Status503ServiceUnavailable);

    var state = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
    var protectedState = stateProtector.Protect(state);
    http.Response.Cookies.Append("aster_github_install_state", protectedState, new CookieOptions
    {
        HttpOnly = true,
        Secure = http.Request.IsHttps,
        SameSite = SameSiteMode.Lax,
        IsEssential = true,
        MaxAge = TimeSpan.FromMinutes(10),
        Path = "/api/github"
    });

    var installUrl = $"https://github.com/apps/{Uri.EscapeDataString(slug)}/installations/new?state={Uri.EscapeDataString(state)}";
    return Results.Redirect(installUrl);
});

app.MapGet("/api/github/callback", (HttpContext http, string? installation_id, string? setup_action, string? state) =>
{
    if (string.IsNullOrWhiteSpace(state) || state.Length != 64 ||
        !state.All(Uri.IsHexDigit) ||
        !http.Request.Cookies.TryGetValue("aster_github_install_state", out var protectedCookie))
    {
        return Results.Problem(
            title: "GitHub installation could not be verified",
            detail: "The security state is missing or invalid. Return to ASTER and start the GitHub connection again.",
            statusCode: StatusCodes.Status400BadRequest);
    }

    string expectedState;
    try
    {
        expectedState = stateProtector.Unprotect(protectedCookie);
    }
    catch (CryptographicException)
    {
        return Results.Problem(
            title: "GitHub installation could not be verified",
            detail: "The security state expired or could not be verified. Start the connection again.",
            statusCode: StatusCodes.Status400BadRequest);
    }

    var expectedBytes = System.Text.Encoding.ASCII.GetBytes(expectedState);
    var actualBytes = System.Text.Encoding.ASCII.GetBytes(state);
    var validState = expectedBytes.Length == actualBytes.Length &&
                     CryptographicOperations.FixedTimeEquals(expectedBytes, actualBytes);
    http.Response.Cookies.Delete("aster_github_install_state", new CookieOptions { Path = "/api/github" });

    if (!validState || !string.Equals(setup_action, "install", StringComparison.OrdinalIgnoreCase) ||
        !long.TryParse(installation_id, out var parsedInstallationId) || parsedInstallationId <= 0)
    {
        return Results.Problem(
            title: "GitHub installation could not be verified",
            detail: "GitHub did not return a valid installation confirmation. No account or repository was connected.",
            statusCode: StatusCodes.Status400BadRequest);
    }

    return Results.Content($$"""
        <!doctype html>
        <html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>ASTER · GitHub</title></head>
        <body style="font-family:Segoe UI,Arial,sans-serif;background:#101923;color:#e8f3f7;padding:40px">
          <main style="max-width:620px;margin:8vh auto;padding:32px;border:1px solid #315565;border-radius:16px;background:#15232f">
            <h1>GitHub App installation received</h1>
            <p>GitHub confirmed the installation and the security state was validated.</p>
            <p><strong>Next:</strong> ASTER Connect still needs server-side installation-token exchange and encrypted storage before it can list repositories or provide tools to the AI.</p>
            <p>You may close this page and return to ASTER.</p>
          </main>
        </body></html>
        """, "text/html");
});

app.Run();
