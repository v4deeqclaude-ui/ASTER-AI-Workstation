using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;
using ASTER.Connect.Storage;

var builder = WebApplication.CreateBuilder(args);
var keyRingPath = builder.Configuration["DataProtection:KeyRingPath"];
var dataProtection = builder.Services.AddDataProtection();
if (!string.IsNullOrWhiteSpace(keyRingPath))
{
    Directory.CreateDirectory(keyRingPath);
    dataProtection.PersistKeysToFileSystem(new DirectoryInfo(keyRingPath));
}

var pairingStorePath = builder.Configuration["PairingStore:FilePath"];
if (!string.IsNullOrWhiteSpace(pairingStorePath))
{
    builder.Services.AddSingleton<IPairingStore>(services =>
        new EncryptedFilePairingStore(
            pairingStorePath,
            services.GetRequiredService<IDataProtectionProvider>()));
}

var app = builder.Build();
var stateProtector = app.Services.GetRequiredService<IDataProtectionProvider>()
    .CreateProtector("ASTER.Connect.GitHub.InstallationState.v1")
    .ToTimeLimitedDataProtector();

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
    var keyRingPath = configuration["DataProtection:KeyRingPath"];
    var pairingStorePath = configuration["PairingStore:FilePath"];
    var missing = new List<string>();
    if (string.IsNullOrWhiteSpace(appSlug)) missing.Add("GitHubApp:Slug");
    if (!Uri.TryCreate(publicBaseUrl, UriKind.Absolute, out var publicUri) || publicUri.Scheme != Uri.UriSchemeHttps)
        missing.Add("PublicBaseUrl (must be an HTTPS URL)");
    if (string.IsNullOrWhiteSpace(keyRingPath)) missing.Add("DataProtection:KeyRingPath (persistent storage required)");
    if (string.IsNullOrWhiteSpace(pairingStorePath)) missing.Add("PairingStore:FilePath (encrypted pairing store path required)");

    return Results.Json(new
    {
        status = missing.Count == 0 ? "configured_for_installation_flow" : "configuration_required",
        service = "ASTER Connect",
        githubAppConfigured = missing.Count == 0,
        missingConfiguration = missing,
        note = "Encrypted single-instance pairing storage is available as infrastructure. Pairing endpoints, account binding, token exchange and repository access are not enabled yet."
    }, statusCode: missing.Count == 0 ? StatusCodes.Status200OK : StatusCodes.Status503ServiceUnavailable);
});

app.MapGet("/api/github/status", (IConfiguration configuration) =>
{
    var configured = !string.IsNullOrWhiteSpace(configuration["GitHubApp:Slug"]) &&
                     Uri.TryCreate(configuration["PublicBaseUrl"], UriKind.Absolute, out var publicUri) &&
                     publicUri.Scheme == Uri.UriSchemeHttps &&
                     !string.IsNullOrWhiteSpace(configuration["DataProtection:KeyRingPath"]) &&
                     !string.IsNullOrWhiteSpace(configuration["PairingStore:FilePath"]);
    return Results.Ok(new
    {
        provider = "github",
        status = "not_connected",
        appConfigured = configured,
        installationFlowAvailable = configured,
        repositoryAccessAvailable = false,
        reason = configured
            ? "GitHub App installation can be started. Repository access is not enabled until installation verification and encrypted token storage are implemented."
            : "ASTER Connect requires the GitHub App slug, a public HTTPS URL, and a persistent Data Protection key-ring path.",
        requiredPermissions = new[] { "Metadata: read-only", "Contents: read-only" }
    });
});


app.MapPost("/api/github/pairings", async (HttpContext http, IConfiguration configuration, IPairingStore store, PairingStartRequest request, CancellationToken ct) =>
{
    var slug = configuration["GitHubApp:Slug"];
    var publicUrl = configuration["PublicBaseUrl"];
    if (string.IsNullOrWhiteSpace(slug) || !Uri.TryCreate(publicUrl, UriKind.Absolute, out var publicUri) || publicUri.Scheme != Uri.UriSchemeHttps)
        return Results.Problem(title: "GitHub App is not configured", statusCode: 503);
    if (request.Verifier is null || request.Verifier.Length != 64 || !request.Verifier.All(Uri.IsHexDigit))
        return Results.Problem(title: "Invalid pairing verifier", statusCode: 400);

    var now = DateTimeOffset.UtcNow;
    var id = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
    var hash = Convert.ToHexString(SHA256.HashData(Convert.FromHexString(request.Verifier)));
    await store.SaveAsync(new PairingRecord(id, hash, now, now.AddMinutes(10), "pending", null), ct);

    var state = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
    var protectedState = stateProtector.Protect(id + ":" + state, TimeSpan.FromMinutes(10));
    http.Response.Cookies.Append("aster_github_install_state", protectedState, new CookieOptions
    {
        HttpOnly = true, Secure = true, SameSite = SameSiteMode.Lax,
        IsEssential = true, MaxAge = TimeSpan.FromMinutes(10), Path = "/api/github"
    });
    var url = "https://github.com/apps/" + Uri.EscapeDataString(slug) + "/installations/new?state=" + Uri.EscapeDataString(state);
    return Results.Created("/api/github/pairings/" + id, new { pairingId = id, authorizationUrl = url, expiresAtUtc = now.AddMinutes(10), status = "pending" });
});

app.MapPost("/api/github/pairings/{id}/status", async (string id, IPairingStore store, PairingStatusRequest request, CancellationToken ct) =>
{
    if (request.Verifier is null || request.Verifier.Length != 64 || !request.Verifier.All(Uri.IsHexDigit))
        return Results.Problem(title: "Pairing proof required", statusCode: 401);
    var record = await store.GetAsync(id, ct);
    if (record is null || record.ExpiresAtUtc <= DateTimeOffset.UtcNow)
        return Results.NotFound(new { status = "expired_or_unknown" });
    var supplied = SHA256.HashData(Convert.FromHexString(request.Verifier));
    var stored = Convert.FromHexString(record.VerifierHash);
    if (!CryptographicOperations.FixedTimeEquals(supplied, stored))
        return Results.Problem(title: "Pairing proof rejected", statusCode: 401);
    return Results.Ok(new { pairingId = record.Id, status = record.Status, expiresAtUtc = record.ExpiresAtUtc });
});

app.MapGet("/api/github/callback", async (HttpContext http, string? installation_id, string? setup_action, string? state) =>
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
        expectedState = stateProtector.Unprotect(protectedCookie, out _);
    }
    catch (CryptographicException)
    {
        return Results.Problem(
            title: "GitHub installation could not be verified",
            detail: "The security state expired or could not be verified. Start the connection again.",
            statusCode: StatusCodes.Status400BadRequest);
    }

    var stateParts = expectedState.Split(new[] { ':' }, 2);
    var expectedPairingId = stateParts.Length == 2 ? stateParts[0] : string.Empty;
    var expectedBytes = System.Text.Encoding.ASCII.GetBytes(stateParts.Length == 2 ? stateParts[1] : string.Empty);
    var actualBytes = System.Text.Encoding.ASCII.GetBytes(state);
    var validState = expectedBytes.Length == actualBytes.Length &&
                     CryptographicOperations.FixedTimeEquals(expectedBytes, actualBytes);
    http.Response.Cookies.Delete("aster_github_install_state", new CookieOptions { Path = "/api/github" });

    if (!validState || string.IsNullOrWhiteSpace(expectedPairingId) || !string.Equals(setup_action, "install", StringComparison.OrdinalIgnoreCase) ||
        !long.TryParse(installation_id, out var parsedInstallationId) || parsedInstallationId <= 0)
    {
        return Results.Problem(
            title: "GitHub installation could not be verified",
            detail: "GitHub did not return a valid installation confirmation. No account or repository was connected.",
            statusCode: StatusCodes.Status400BadRequest);
    }

    var pairingStore = http.RequestServices.GetRequiredService<IPairingStore>();
    var pairing = await pairingStore.GetAsync(expectedPairingId, http.RequestAborted);
    if (pairing is null || pairing.ExpiresAtUtc <= DateTimeOffset.UtcNow || pairing.Status != "pending")
        return Results.Problem(title: "Pairing request expired or already used", statusCode: 410);
    await pairingStore.SaveAsync(pairing with { Status = "installed", InstallationId = parsedInstallationId }, http.RequestAborted);

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

public sealed record PairingStartRequest(string Verifier);\npublic sealed record PairingStatusRequest(string Verifier);
