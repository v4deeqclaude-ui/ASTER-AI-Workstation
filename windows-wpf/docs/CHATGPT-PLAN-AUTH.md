# ASTER 0.2 — Sign in with ChatGPT integration

This implementation is intended for the user's personal, non-commercial Windows installation. OpenAI's Sign in with ChatGPT DevKit is pinned to commit `0a36fefeb913055c8c7a1a29b63d82b96b2e841a` and distributed under its Noncommercial License v1.0. Review the included license before any redistribution; commercial use is not permitted by that license.

## Implemented in this branch

- WPF settings expose separate **Continue with ChatGPT** and **OpenAI API key** methods.
- The official `@siwc/local` SDK runs in a separate local Node.js process.
- WPF and the helper communicate over newline-delimited JSON. Tokens and raw OAuth payloads are never sent to WPF.
- The SDK credential store uses Windows DPAPI `CurrentUser`; unsupported platforms fail closed, with no plaintext fallback.
- Browser authorization is restricted to HTTPS `auth.openai.com` authorization endpoints and loopback callback handling remains inside the official SDK.
- ChatGPT plan sharing is checked independently from sign-in. A connected account without `chatgpt.tokens.use.direct` cannot be used for plan-backed chat.
- API-key chat remains a separate explicit fallback only when there is no connected ChatGPT profile.
- The chat history remains in the existing local SQLite database.

## Build gates

GitHub Actions checks the pinned SDK build, DPAPI round-trip, browser URL allowlist, WPF publish, and packages Node.js with the helper. A successful build does not prove that the user's account is eligible for plan sharing. The final sign-in and a real chat request must be tested by the user.

## Explicit non-goals

- No API-key or plan-sharing bypass.
- No credential extraction from ChatGPT Desktop or the browser.
- No remote Unity connection or remote-control bridge.
- No changes to the Electron prototype or the stable WPF 0.1 artifact.
