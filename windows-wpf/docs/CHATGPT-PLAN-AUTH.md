# ASTER 0.2 — Sign in with ChatGPT integration

## Scope

This integration is for the user's personal, non-commercial Windows installation of ASTER. The OpenAI Sign in with ChatGPT DevKit is distributed under its Noncommercial License v1.0. Do not use or redistribute OpenAI-authored DevKit code as part of a commercial product without obtaining separate written permission or replacing that component.

## Architecture decision

- Keep the existing WPF application and its SQLite chat history.
- Integrate the official `@siwc/local` SDK in a separate, local Node.js helper process. The SDK is a Node.js package and must not be loaded into the WPF renderer/UI process.
- Communicate with the helper through a strict newline-delimited JSON protocol over standard input/output. Never send access tokens, refresh tokens, ID tokens, or raw OAuth responses to WPF.
- Use the Windows DPAPI `CurrentUser` scope for credential encryption at rest. Refuse to save credentials if OS-backed encryption is unavailable; never fall back to plaintext.
- Open only the verified official authorization origin in the system browser. OAuth state, PKCE, nonce, callback validation, token refresh, identity verification, and ChatGPT plan sharing remain the responsibility of the official SDK.
- Keep the existing OpenAI API-key connection as a separate alternative. Do not silently switch providers.
- Keep chat history in the existing local SQLite database. Do not upload conversation history to GitHub or include it in diagnostics.
- Do not log secrets or include credentials in exceptions, telemetry, command-line arguments, or build artifacts.

## UI behavior

The AI settings page will expose two separate connection methods:
1. **Continue with ChatGPT** — starts the official browser-based sign-in flow.
2. **OpenAI API key** — retains the existing API-key method.

After sign-in, ASTER should display only safe account/session metadata, available models, and a link to the official usage page. A signed-in account is not necessarily approved for ChatGPT plan sharing; the UI must distinguish those states and show the SDK's exact safe error state.

## Build and validation gates

1. Pin the DevKit to a reviewed upstream commit and retain its license and third-party notices.
2. Build the local SDK and Windows helper in GitHub Actions; package a compatible Windows x64 Node runtime with the application so the user does not need to install Node.js.
3. Test DPAPI encrypt/decrypt round-trips on the Windows runner.
4. Test helper protocol validation, cancellation, browser-origin allowlisting, startup failure, and absence of credentials in standard output.
5. Publish an artifact only after the WPF app and helper both build successfully.
6. Test actual ChatGPT sign-in and plan-sharing eligibility on the user's own account. A successful build cannot establish account eligibility.

## Explicit non-goals

- No API-key bypass or sharing of another user's subscription.
- No credential extraction from the ChatGPT desktop app or browser.
- No remote Unity connection or remote-control bridge.
- No changes to the Electron prototype or the stable WPF 0.1 artifact.
