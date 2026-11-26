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
