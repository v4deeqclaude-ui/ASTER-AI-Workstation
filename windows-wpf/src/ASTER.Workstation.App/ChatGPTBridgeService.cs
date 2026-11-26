using System.IO;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using ASTER.Workstation.Core.Services;

namespace ASTER.Workstation.App;

public sealed class ChatGPTBridgeService
{
    private static readonly Lazy<ChatGPTBridgeService> Instance = new(() => new ChatGPTBridgeService());
    public static ChatGPTBridgeService Shared => Instance.Value;

    private readonly object _gate = new();
    private readonly ConcurrentDictionary<string, TaskCompletionSource<JsonElement>> _pending = new();
    private Process? _process;
    private StreamWriter? _input;
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    private ChatGPTBridgeService() { }

    private async Task EnsureStartedAsync()
    {
        if (_process is { HasExited: false }) return;
        lock (_gate)
        {
            if (_process is { HasExited: false }) return;
            var bridgeDir = Path.Combine(AppContext.BaseDirectory, "ChatGPTBridge");
            var node = Path.Combine(bridgeDir, "node.exe");
            var script = Path.Combine(bridgeDir, "bridge.mjs");
            if (!File.Exists(node) || !File.Exists(script))
                throw new InvalidOperationException("Модуль входа ChatGPT не найден в папке приложения.");
            var start = new ProcessStartInfo(node)
            {
                WorkingDirectory = bridgeDir,
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            start.ArgumentList.Add(script);
            start.Environment["ASTER_DATA_DIR"] = AppPaths.Root;
            var process = new Process { StartInfo = start, EnableRaisingEvents = true };
            process.OutputDataReceived += (_, e) => { if (!string.IsNullOrEmpty(e.Data)) HandleLine(e.Data); };
            process.ErrorDataReceived += (_, _) => { }; // Never forward helper stderr to UI or logs.
            process.Exited += (_, _) =>
            {
                foreach (var item in _pending)
                    if (_pending.TryRemove(item.Key, out var pending))
                        pending.TrySetException(new InvalidOperationException("Модуль ChatGPT завершил работу. Перезапусти подключение."));
            };
            if (!process.Start()) throw new InvalidOperationException("Не удалось запустить модуль ChatGPT.");
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            _process = process;
            _input = process.StandardInput;
        }
        await Task.CompletedTask;
    }

    private void HandleLine(string line)
    {
        try
        {
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;
            if (root.TryGetProperty("type", out var type))
            {
                if (type.GetString() == "browser.open" && root.TryGetProperty("url", out var url))
                    OpenAuthorizationUrl(url.GetString());
                return;
            }
            if (!root.TryGetProperty("requestId", out var idElement)) return;
            var id = idElement.GetString();
            if (string.IsNullOrEmpty(id) || !_pending.TryRemove(id, out var completion)) return;
            if (root.TryGetProperty("ok", out var ok) && ok.GetBoolean() && root.TryGetProperty("result", out var result))
                completion.TrySetResult(result.Clone());
            else
            {
                var errorMessage = root.TryGetProperty("error", out var error) ? error : default;
                var message = errorMessage.ValueKind == JsonValueKind.Object && errorMessage.TryGetProperty("message", out var text)
                    ? text.GetString() : "Ошибка подключения ChatGPT.";
                var code = errorMessage.ValueKind == JsonValueKind.Object && errorMessage.TryGetProperty("code", out var codeElement)
                    ? codeElement.GetString() : null;
                var diagnostic = "";
                if (errorMessage.ValueKind == JsonValueKind.Object && errorMessage.TryGetProperty("diagnostics", out var details) &&
                    details.ValueKind == JsonValueKind.Object)
                {
                    var errorType = details.TryGetProperty("errorType", out var et) ? et.GetString() : null;
                    var causeType = details.TryGetProperty("causeType", out var ct) ? ct.GetString() : null;
                    var causeCode = details.TryGetProperty("causeCode", out var cc) ? cc.GetString() : null;
                    diagnostic = string.Join(" · ", new[] {
                        string.IsNullOrWhiteSpace(errorType) ? null : "тип: " + errorType,
                        string.IsNullOrWhiteSpace(causeType) ? null : "причина: " + causeType,
                        string.IsNullOrWhiteSpace(causeCode) ? null : "код: " + causeCode
                    }.Where(x => x is not null));
                }
                var detail = string.Join(" · ", new[] {
                    string.IsNullOrWhiteSpace(code) ? null : "Код: " + code,
                    string.IsNullOrWhiteSpace(diagnostic) ? null : diagnostic
                }.Where(x => x is not null));
                completion.TrySetException(new InvalidOperationException(
                    (message ?? "Ошибка подключения ChatGPT.") + (detail.Length == 0 ? "" : Environment.NewLine + detail)));
            }
        }
        catch { /* Malformed helper output is ignored; no raw output is logged. */ }
    }

    private static void OpenAuthorizationUrl(string? value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps ||
            !string.Equals(uri.Host, "auth.openai.com", StringComparison.OrdinalIgnoreCase) ||
            uri.Port != 443 || (uri.AbsolutePath != "/authorize" && uri.AbsolutePath != "/oauth/authorize"))
            throw new InvalidOperationException("ASTER заблокировал неизвестный адрес авторизации.");
        Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
    }

    private async Task<JsonElement> RequestAsync(object payload, CancellationToken cancellationToken = default)
    {
        await EnsureStartedAsync();
        var requestId = Guid.NewGuid().ToString("N");
        var envelope = JsonSerializer.SerializeToElement(payload);
        var json = new Dictionary<string, object?> { ["requestId"] = requestId };
        foreach (var property in envelope.EnumerateObject()) json[property.Name] = property.Value;
        var completion = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_pending.TryAdd(requestId, completion)) throw new InvalidOperationException("Не удалось создать запрос к ChatGPT.");
        try
        {
            await _writeLock.WaitAsync(cancellationToken);
            try
            {
                if (_input is null) throw new InvalidOperationException("Модуль ChatGPT не запущен.");
                await _input.WriteLineAsync(JsonSerializer.Serialize(json));
                await _input.FlushAsync();
            }
            finally { _writeLock.Release(); }
            return await completion.Task.WaitAsync(cancellationToken);
        }
        catch
        {
            _pending.TryRemove(requestId, out _);
            throw;
        }
    }

    public async Task<JsonElement> GetSessionAsync(CancellationToken cancellationToken = default) =>
        await RequestAsync(new { command = "session.get" }, cancellationToken);

    public async Task<JsonElement> SignInAsync(CancellationToken cancellationToken = default) =>
        await RequestAsync(new { command = "signin.start" }, cancellationToken);

    public async Task CancelSignInAsync() =>
        await RequestAsync(new { command = "signin.cancel" });

    public async Task DisconnectAsync() =>
        await RequestAsync(new { command = "session.disconnect" });

    public async Task<JsonElement> RequestModelsAsync() =>
        await RequestAsync(new { command = "models.list" });

    public async Task<string> SendAsync(string model, IReadOnlyList<(string Role, string Content)> history, CancellationToken cancellationToken = default)
    {
        var payload = new
        {
            command = "chat.send",
            model,
            history = history.Select(x => new { role = x.Role, content = x.Content }).ToArray()
        };
        var result = await RequestAsync(payload, cancellationToken);
        return result.TryGetProperty("text", out var text) ? text.GetString() ?? "" : "";
    }
}
