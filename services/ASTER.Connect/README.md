# ASTER Connect (v0.7)

ASP.NET Core service for official GitHub App installation and future repository tools.

## Implemented in this stage

- `GET /health/live` — liveness endpoint.
- `GET /health/ready` — reports whether the GitHub App slug is configured.
- `GET /api/github/status` — reports installation-flow and repository-access status separately.
- `GET /api/github/connect` — redirects to the official GitHub App installation page.
- `GET /api/github/callback` — validates the one-time anti-forgery state cookie and GitHub installation callback parameters.
- Encrypted pairing-store infrastructure — persists short-lived pairing records using ASP.NET Core Data Protection, with atomic file replacement and a process-local concurrency gate. Pairing HTTP endpoints and desktop account binding are not implemented yet.

The callback does not yet exchange an installation token, persist an installation, or expose repositories. The encrypted pairing store is infrastructure only; it is not yet used by a completed desktop pairing flow. It confirms only that GitHub returned a valid installation callback with a matching short-lived protected state. The API continues to report `not_connected` until the next stage is complete.

## Server configuration

Set the following environment variables in the hosting provider:

- `GitHubApp__Slug` — slug of the ASTER-owned GitHub App.
- `PublicBaseUrl` — public HTTPS origin of this service (for example, the Railway-provided domain).
- `DataProtection__KeyRingPath` — path on a persistent private volume for ASP.NET Core Data Protection keys (for example, `/data/aster-key-ring`).
- `PairingStore__FilePath` — file path on persistent private storage for the encrypted pairing store (for example, `/data/aster-pairings/pairings.dat`). The service refuses readiness and the GitHub installation flow unless this is configured.

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

The GitHub installation flow requires a persistent private volume for the Data Protection key ring. Mount a durable private volume at `/data`, set `DataProtection__KeyRingPath=/data/aster-key-ring` and `PairingStore__FilePath=/data/aster-pairings/pairings.dat`. Without this, `/health/ready` remains unavailable and `/api/github/connect` refuses to start. The current file store is single-instance only: it uses a process-local lock and is not safe for multiple service replicas or concurrent hosts sharing the same file. Do not scale horizontally. Before production or multi-instance deployment, replace it with a transactional database-backed store supporting atomic one-time redemption, tenant isolation, expiry cleanup and encryption/key management.

## Deployment

Set Railway service root directory to `services/ASTER.Connect`. Railway builds the included Dockerfile. Health-check path: `/health/live`.

No paid hosting resource should be provisioned without explicit owner approval.
