import { createChatGPT } from "@siwc/local";
import { Dpapi, isPlatformSupported } from "@primno/dpapi";
import { mkdir } from "node:fs/promises";
import { dirname, join } from "node:path";
import { homedir } from "node:os";
import readline from "node:readline";
import { ProxyAgent, setGlobalDispatcher } from "undici";

const APP_ID = "aster-ai-workstation";
const dataRoot = process.env.ASTER_DATA_DIR || join(homedir(), "AppData", "Local", "ASTER", "Workstation");
const storageDir = join(dataRoot, "ChatGPT");
// ASTER uses Hiro/Xray local HTTP proxy by default; set ASTER_HTTP_PROXY=direct to opt out.
const proxySetting = process.env.ASTER_HTTP_PROXY ?? "http://127.0.0.1:10809";
if (proxySetting.toLowerCase() !== "direct") {
  const proxyUrl = new URL(proxySetting);
  if (proxyUrl.protocol !== "http:" || !proxyUrl.hostname || !proxyUrl.port) {
    throw new Error("ASTER_HTTP_PROXY must be an HTTP proxy URL or the value direct.");
  }
  setGlobalDispatcher(new ProxyAgent(proxyUrl.toString()));
}
let client;
let signInBusy = false;

function emit(value) {
  process.stdout.write(JSON.stringify(value) + "\n");
}
// Observe only OAuth/API transport metadata. Never log query strings, bodies, headers, cookies, or tokens.
const nativeFetch = globalThis.fetch.bind(globalThis);
globalThis.fetch = async (input, init) => {
  let target;
  try {
    target = new URL(typeof input === "string" ? input : input.url);
  } catch {
    return nativeFetch(input, init);
  }
  const monitoredHost = ["auth.openai.com", "api.openai.com"].includes(target.hostname);
  if (!monitoredHost) return nativeFetch(input, init);
  const method = String(init?.method || (typeof Request !== "undefined" && input instanceof Request ? input.method : "GET")).toUpperCase();
  const endpoint = target.hostname + target.pathname;
  let grantType = "";
  try {
    const body = init?.body;
    if (body instanceof URLSearchParams) grantType = body.get("grant_type") || "";
    else if (typeof body === "string") grantType = new URLSearchParams(body).get("grant_type") || "";
  } catch { /* Only an allowlisted grant type is logged. */ }
  const safeGrantType = ["authorization_code", "refresh_token"].includes(grantType) ? grantType : "";
  emit({ type: "diagnostic", message: `Сетевой запрос OAuth: ${method} ${endpoint}${safeGrantType ? " · grant_type: " + safeGrantType : ""}.` });
  try {
    const response = await nativeFetch(input, init);
    let oauthError = "";
    if (!response.ok && /token/i.test(target.pathname)) {
      try {
        const body = await response.clone().json();
        const parts = [];
        if (typeof body?.error === "string" && /^[a-z0-9_.-]{1,64}$/i.test(body.error)) parts.push("OAuth-код: " + body.error);
        if (typeof body?.error_reason === "string" && /^[a-z0-9_.-]{1,80}$/i.test(body.error_reason)) parts.push("причина: " + body.error_reason);
        const requestId = response.headers.get("x-request-id");
        if (requestId && /^[a-zA-Z0-9_-]{1,100}$/.test(requestId)) parts.push("request-id: " + requestId);
        if (parts.length) oauthError = "; " + parts.join("; ");
      } catch { /* Response body is intentionally not logged. */ }
    }
    emit({ type: "diagnostic", message: `Ответ OAuth: HTTP ${response.status} от ${target.hostname}${oauthError}.` });
    return response;
  } catch (error) {
    const causeCode = typeof error?.cause?.code === "string" ? error.cause.code : "";
    emit({ type: "diagnostic", message: `Сетевой запрос OAuth не выполнен: ${error?.name || "Error"}${causeCode ? " · " + causeCode : ""}.` });
    throw error;
  }
};

function safeDiagnosticText(value) {
  if (typeof value !== "string") return undefined;
  return value
    .replace(/https?:\/\/[^\s"'<>]+/gi, "[url hidden]")
    .replace(/(authorization|bearer|access[_ -]?token|refresh[_ -]?token|client[_ -]?secret|code|state)=?\s*[^\s,;]+/gi, "$1=[redacted]")
    .slice(0, 240);
}
function safeError(error) {
  const code = typeof error?.code === "string" ? error.code : "bridge_error";
  const message = safeDiagnosticText(error?.message) || "Unexpected ChatGPT connection error.";
  // Expose only sanitized error classifications; never URLs, request bodies, headers, or credentials.
  const cause = error?.cause;
  return {
    code,
    message,
    retryable: Boolean(error?.retryable),
    diagnostics: {
      errorType: typeof error?.name === "string" ? error.name.slice(0, 80) : "Unknown",
      causeType: typeof cause?.name === "string" ? cause.name.slice(0, 80) : undefined,
      causeCode: typeof cause?.code === "string" ? cause.code.slice(0, 100) : undefined,
      causeMessage: safeDiagnosticText(cause?.message)
    }
  };
}
function browserUrlAllowed(value) {
  try {
    const url = new URL(value);
    return url.protocol === "https:" && url.hostname === "auth.openai.com" && url.port === "" &&
      !url.username && !url.password;
  } catch { return false; }
}
function ensureClient() {
  if (client) return client;
  if (process.platform !== "win32" || !isPlatformSupported) {
    throw new Error("Защищённое хранилище DPAPI доступно только в поддерживаемой версии Windows.");
  }
  const encryption = {
    id: "windows-dpapi-current-user-v1",
    isAvailable: () => process.platform === "win32" && isPlatformSupported,
    encrypt: (plaintext) => Dpapi.protectData(Buffer.from(plaintext, "utf8"), null, "CurrentUser"),
    decrypt: (ciphertext) => Dpapi.unprotectData(Buffer.from(ciphertext), null, "CurrentUser").toString("utf8")
  };
  client = createChatGPT({
    appName: "ASTER AI Workstation",
    appId: APP_ID,
    redirectPort: 0,
    storageDir,
    credentialEncryption: encryption,
    openBrowser: (url) => {
      if (!browserUrlAllowed(url)) throw new Error("Blocked unexpected authorization URL.");
      emit({ type: "browser.open", url });
    }
  });
  client.subscribe((state) => {
    emit({ type: "session", state });
    emit({ type: "diagnostic", message: `Состояние SDK: ${state.status}; доступ к плану: ${state.sharing ? "да" : "нет"}.` });
  });
  return client;
}

async function handle(message) {
  const api = ensureClient();
  switch (message.command) {
    case "session.get":
      return await api.getSession();
    case "models.list":
      return await api.listModels();
    case "signin.start":
      if (signInBusy) throw Object.assign(new Error("Вход уже выполняется."), { code: "connection_busy" });
      signInBusy = true;
      try {
        emit({ type: "diagnostic", message: "Ожидание локального возврата авторизации от браузера." });
        const result = await api.signIn();
        emit({ type: "diagnostic", message: "SDK завершил OAuth, проверку личности и сохранение сессии." });
        return result;
      } catch (error) {
        const details = safeError(error);
        emit({ type: "diagnostic", message: `Процесс входа завершился ошибкой: ${details.code}.` });
        throw error;
      } finally { signInBusy = false; }
    case "signin.cancel":
      api.cancelSignIn();
      return { cancelled: true };
    case "session.disconnect":
      await api.disconnect();
      return await api.getSession();
    case "chat.send": {
      if (!Array.isArray(message.history) || message.history.length < 1 || message.history.length > 50) {
        throw Object.assign(new Error("Некорректная история диалога."), { code: "invalid_input" });
      }
      const history = message.history.map((item) => {
        if (!item || !["user", "assistant", "developer"].includes(item.role) ||
            typeof item.content !== "string" || item.content.length > 30000) {
          throw Object.assign(new Error("Некорректное сообщение в истории."), { code: "invalid_input" });
        }
        return { role: item.role, content: item.content };
      });
      if (typeof message.model !== "string" || message.model.length > 160) {
        throw Object.assign(new Error("Не выбрана допустимая модель."), { code: "invalid_input" });
      }
      return await api.streamResponse({ model: message.model, input: history });
    }
    default:
      throw Object.assign(new Error("Неизвестная команда ASTER bridge."), { code: "unknown_command" });
  }
}

if (process.argv.includes("--self-test")) {
  if (process.platform !== "win32" || !isPlatformSupported) {
    emit({ ok: false, test: "dpapi", error: "unsupported_platform" });
    process.exitCode = 1;
  } else {
    const probe = Buffer.from("ASTER-DPAPI-SELF-TEST", "utf8");
    const encrypted = Dpapi.protectData(probe, null, "CurrentUser");
    const decrypted = Dpapi.unprotectData(encrypted, null, "CurrentUser");
    const pass = decrypted.equals(probe) && !encrypted.equals(probe);
    emit({ ok: pass, test: "dpapi-current-user-roundtrip", browserAllowlist: browserUrlAllowed("https://auth.openai.com/authorize?state=test") && !browserUrlAllowed("https://evil.example/authorize") });
    if (!pass) process.exitCode = 1;
  }
} else {
  await mkdir(storageDir, { recursive: true });
  const input = readline.createInterface({ input: process.stdin, crlfDelay: Infinity });
  for await (const line of input) {
    if (!line.trim()) continue;
    let request;
    try {
      request = JSON.parse(line);
      if (!request || typeof request.requestId !== "string" || request.requestId.length > 100) throw new Error("Invalid request envelope.");
      const result = await handle(request);
      emit({ requestId: request.requestId, ok: true, result });
    } catch (error) {
      emit({ requestId: typeof request?.requestId === "string" ? request.requestId : "", ok: false, error: safeError(error) });
    }
  }
}
