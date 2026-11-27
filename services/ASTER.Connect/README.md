# ASTER Connect (v0.7)

ASP.NET Core service for official GitHub App installation and future repository tools.

## Implemented in this stage

- `GET /health/live` — liveness endpoint.
- `GET /health/ready` — reports whether the GitHub App slug is configured.
- `GET /api/github/status` — reports installation-flow and repository-access status separately.
- `GET /api/github/connect` — redirects to the official GitHub App installation page.
- `GET /api/github/callback` — validates the one-time anti-forgery state cookie and GitHub installation callback parameters.

The callback does not yet exchange an installation token, persist an installation, or expose repositories. It confirms only that GitHub returned a valid installation callback with a matching short-lived protected state. The API continues to report `not_connected` until the next stage is complete.

## Server configuration

Set the following environment variables in the hosting provider:

- `GitHubApp__Slug` — slug of the ASTER-owned GitHub App.
- `PublicBaseUrl` — public HTTPS origin of this service (for example, the Railway-provided domain).

Configure the GitHub App setup URL/callback to:
`https://YOUR-ASTER-CONNECT-DOMAIN/api/github/callback`

The GitHub App should initially request only read-only repository metadata and contents permissions. Do not request write permissions for the first test.

## Security notes

- The installation redirect uses a cryptographically random one-time state value.
- The state is protected with ASP.NET Core Data Protection and placed in a short-lived HttpOnly cookie.
- Callback state comparison uses a fixed-time comparison.
- The state cookie is deleted after callback validation.
- No Personal Access Token is requested or accepted.
- No private key, client secret, or installation token is included in the desktop app or source control.
- The callback deliberately does not claim that a repository connection is complete.

For production multi-instance hosting, persist and protect the ASP.NET Core Data Protection key ring using a durable private store before enabling OAuth/token exchange. Do not scale this service horizontally until that is configured.

## Deployment

Set Railway service root directory to `services/ASTER.Connect`. Railway builds the included Dockerfile. Health-check path: `/health/live`.

No paid hosting resource should be provisioned without explicit owner approval.
