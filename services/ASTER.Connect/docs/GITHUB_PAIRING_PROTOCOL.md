# ASTER GitHub pairing protocol

## Purpose

This document defines the desktop-to-browser handoff required after a GitHub App installation. The GitHub installation callback is not, by itself, proof that a particular ASTER desktop session initiated the flow.

## Required flow

1. ASTER creates a cryptographically random pairing verifier (at least 256 bits) and keeps it only in the local process.
2. ASTER sends the verifier to ASTER Connect over HTTPS. The service stores only a keyed hash of it, together with a short-lived pairing identifier, creation time, expiry, and pending status.
3. The service returns a pairing identifier and an authorization URL. The desktop opens that URL in the system browser.
4. The user completes the official GitHub App installation flow. The callback validates the protected, one-time OAuth state cookie and the GitHub installation callback parameters.
5. The callback associates the installation ID with the pending pairing record and marks it as awaiting desktop confirmation. It must not place an installation token, private key, or long-lived credential in a browser URL.
6. ASTER polls the pairing status over HTTPS, proving possession of the original verifier. The service compares a keyed hash in constant time and returns a short-lived, single-use completion code.
7. ASTER redeems the completion code once. The service consumes it atomically and associates the GitHub installation with the authenticated ASTER account/device identity.
8. Only after that association is established may the service mint a short-lived GitHub App installation access token and list repositories available to that installation.

## Security requirements

- Pairing verifier: 256-bit or stronger cryptographically random value; never log it; store only a keyed hash.
- Pairing records and completion codes: short expiry (10 minutes maximum), single use, atomic consume, replay rejection.
- State: unpredictable, protected, cookie-bound, one use, fixed-time comparison.
- Callback: validate setup_action, positive installation ID, state, expiry, and matching pending pairing.
- Tokens: server-side only; encrypt at rest with a managed secret/key store; never return GitHub tokens to WPF or put them in URLs.
- Tenant isolation: every repository/tool request must be scoped to an authenticated ASTER principal and the installation linked to that principal. Installation IDs supplied by clients are not authorization.
- GitHub permissions: begin with Metadata read-only and Contents read-only. Add other permissions only when a concrete feature requires them.
- Rate limits, expiry cleanup, audit events without secrets, and revocation/disconnect handling are required.

## Storage and deployment gates

The current service has Data Protection key-ring persistence but does not yet have a durable transactional database or an ASTER user identity provider. An in-memory pairing store is suitable only for local development and must never be treated as production-ready. Production readiness must remain false until a durable store, authenticated principal model, encryption key management, and atomic one-time redemption are configured and tested.

## Explicit non-goals for this stage

- No personal access token (PAT) collection.
- No GitHub token in the desktop app.
- No repository access before pairing is redeemed and ownership/tenant scope is checked.
- No automatic repository cloning or indexing.
- No write access to repositories.
