# ASTER 0.2 — Sign in with ChatGPT integration

This implementation is intended for the user's personal, non-commercial Windows installation. OpenAI's Sign in with ChatGPT DevKit is pinned to commit `0a36fefeb913055c8c7a1a29b63d82b96b2e841a` and distributed under its Noncommercial License v1.0. Review the included license before any redistribution; commercial use is not permitted by that license.

## Implemented in this branch

- WPF settings expose separate **Continue with ChatGPT** and **OpenAI API key** methods.
- The official `@siwc/local` SDK runs in a separate local Node.js process.
- WPF and the helper communicate over newline-delimited JSON. Tokens and raw OAuth payloads are never sent to WPF.
- The SDK credential store uses Windows DPAPI `CurrentUser`; unsupported platforms fail closed, with no plaintext fallback.
- Browser authorization is restricted to HTTPS `auth.openai.com` authorization endpoints and loopback callback handling remains inside the official SDK.
- ASTER requests identity-only scopes (`openid profile email`) by default, so signing in does not require permission to use ChatGPT plan limits. This confirms the account identity only; it does not provide inference credentials.
- AI chat uses the user's separately configured OpenAI API key. The API key is stored in Windows Credential Manager and requests use the configured HTTP proxy. API billing is separate from a ChatGPT subscription.
- The chat history remains in the existing local SQLite database.

## Build gates

GitHub Actions checks the pinned SDK build, DPAPI round-trip, browser URL allowlist, WPF publish, and packages Node.js with the helper. A successful build does not prove that identity sign-in works for every workspace. The final browser sign-in must be tested by the user. ChatGPT plan-based inference is intentionally not requested in this identity-only build; a separate API key is required for AI chat.

## Explicit non-goals

- No API-key or plan-sharing bypass.
- No credential extraction from ChatGPT Desktop or the browser.
- No remote Unity connection or remote-control bridge.
- No changes to the Electron prototype or the stable WPF 0.1 artifact.
