var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

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
    var clientId = configuration["GitHubApp:ClientId"];
    var baseUrl = configuration["PublicBaseUrl"];

    var missing = new List<string>();
    if (string.IsNullOrWhiteSpace(appSlug)) missing.Add("GitHubApp:Slug");
    if (string.IsNullOrWhiteSpace(clientId)) missing.Add("GitHubApp:ClientId");
    if (string.IsNullOrWhiteSpace(baseUrl)) missing.Add("PublicBaseUrl");

    return Results.Json(new
    {
        status = missing.Count == 0 ? "configured_for_next_stage" : "configuration_required",
        service = "ASTER Connect",
        githubAppConfigured = missing.Count == 0,
        missingConfiguration = missing,
        note = "Token storage and GitHub authorization are not enabled in this foundation stage."
    }, statusCode: missing.Count == 0 ? StatusCodes.Status200OK : StatusCodes.Status503ServiceUnavailable);
});

app.MapGet("/api/github/status", (IConfiguration configuration) =>
{
    var configured =
        !string.IsNullOrWhiteSpace(configuration["GitHubApp:Slug"]) &&
        !string.IsNullOrWhiteSpace(configuration["GitHubApp:ClientId"]) &&
        !string.IsNullOrWhiteSpace(configuration["PublicBaseUrl"]);

    return Results.Ok(new
    {
        provider = "github",
        status = "not_connected",
        appConfigured = configured,
        authorizationAvailable = false,
        reason = configured
            ? "Authorization will be enabled after secure server-side state and token storage are implemented."
            : "ASTER Connect is waiting for GitHub App configuration.",
        requiredPermissions = new[] { "Metadata: read-only", "Contents: read-only" }
    });
});

app.MapGet("/api/github/connect", () => Results.Problem(
    title: "GitHub connection is not enabled yet",
    detail: "The official GitHub App installation flow will be enabled after the server-side state and encrypted token store are ready. No personal access token is required.",
    statusCode: StatusCodes.Status503ServiceUnavailable));

app.MapGet("/api/github/callback", () => Results.Problem(
    title: "GitHub callback is not enabled yet",
    detail: "This endpoint is reserved for the validated GitHub App installation callback. It will not accept or store tokens in this foundation stage.",
    statusCode: StatusCodes.Status503ServiceUnavailable));

app.Run();
