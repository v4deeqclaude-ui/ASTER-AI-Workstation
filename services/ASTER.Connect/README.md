# ASTER Connect (v0.7 foundation)

ASP.NET Core service that will broker official GitHub App authorization for ASTER.

## Current stage

This is the deployment and health-check foundation only. GitHub authorization, callback processing, repository access, and token persistence are deliberately disabled until the secure state store and encrypted credential store are implemented. The service does not accept personal access tokens and does not store credentials.

## Endpoints

- `GET /` — service identity
- `GET /health/live` — process liveness (Railway health check)
- `GET /health/ready` — configuration readiness
- `GET /api/github/status` — honest connection/configuration status
- `GET /api/github/connect` — reserved; returns 503 until secure OAuth flow is ready
- `GET /api/github/callback` — reserved; returns 503 until callback validation is ready

## Configuration

Set these as server environment variables only after creating the GitHub App:

- `GitHubApp__Slug`
- `GitHubApp__ClientId`
- `PublicBaseUrl`

Never place a GitHub App private key, client secret, user token, or installation token in source control or the desktop application.

## Railway

Set the Railway service root directory to `services/ASTER.Connect`. Railway will build the included Dockerfile. The health-check path is `/health/live`.

No paid service should be provisioned until the owner reviews the provider's current plan and explicitly approves deployment.
