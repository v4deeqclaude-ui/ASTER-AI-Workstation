using System.Diagnostics;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ASTER.Workstation.App;

internal static class McpRuntimeService
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(3) };

    public static async Task<string> SendWithToolsAsync(
        string endpoint,
        string model,
        List<(string Role, string Content)> history,
        IReadOnlyList<McpServerEntry> configuredServers,
        Func<string, string, Task<bool>> approveToolCall,
        CancellationToken cancellationToken = default)
    {
        var servers = configuredServers.Where(x => x.Enabled).ToList();
        var tools = new List<(McpServerEntry Server, JsonObject Tool)>();
        foreach (var server in servers)
        {
            var listed = await ListToolsAsync(server, cancellationToken);
            foreach (var tool in listed)
            {
                var sourceName = tool["name"]?.GetValue<string>() ?? "tool";
                var exposed = (JsonObject)tool.DeepClone();
                exposed["name"] = SafeName(server.Name) + "__" + SafeName(sourceName);
                tools.Add((server, exposed));
            }
        }

        var messages = new JsonArray();
        foreach (var item in history)
            messages.Add(new JsonObject { ["role"] = item.Role, ["content"] = item.Content });

        var toolDefinitions = new JsonArray();
        foreach (var item in tools)
        {
            var parameters = item.Tool["inputSchema"]?.DeepClone() as JsonObject
                ?? new JsonObject { ["type"] = "object", ["properties"] = new JsonObject() };
            toolDefinitions.Add(new JsonObject
            {
                ["type"] = "function",
                ["function"] = new JsonObject
                {
                    ["name"] = item.Tool["name"]?.GetValue<string>(),
                    ["description"] = item.Tool["description"]?.GetValue<string>() ?? "MCP tool",
                    ["parameters"] = parameters
                }
            });
        }

        var api = endpoint.TrimEnd('/');
        if (!api.EndsWith("/api/chat", StringComparison.OrdinalIgnoreCase))
            api += api.EndsWith("/api", StringComparison.OrdinalIgnoreCase) ? "/chat" : "/api/chat";

        for (var turn = 0; turn < 5; turn++)
        {
            var payload = new JsonObject
            {
                ["model"] = model,
                ["messages"] = messages.DeepClone(),
                ["stream"] = false
            };
            if (toolDefinitions.Count > 0) payload["tools"] = toolDefinitions.DeepClone();

            using var response = await Http.PostAsync(api,
                new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json"), cancellationToken);
            var responseText = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException($"Ollama вернула HTTP {(int)response.StatusCode}: {responseText}");
            var root = JsonNode.Parse(responseText) ?? throw new InvalidOperationException("Пустой ответ Ollama.");
            var message = root["message"] as JsonObject ?? throw new InvalidOperationException("Ollama не вернула объект message.");
            var calls = message["tool_calls"] as JsonArray;
            if (calls is null || calls.Count == 0)
                return message["content"]?.GetValue<string>() ?? "Модель вернула пустой ответ.";

            messages.Add(message.DeepClone());
            foreach (var call in calls)
            {
                var function = call?["function"] as JsonObject;
                var name = function?["name"]?.GetValue<string>() ?? "";
                var arguments = function?["arguments"]?.DeepClone() as JsonObject ?? new JsonObject();
                var match = tools.FirstOrDefault(x => string.Equals(x.Tool["name"]?.GetValue<string>(), name, StringComparison.Ordinal));
                if (match.Server is null)
                {
                    messages.Add(new JsonObject { ["role"] = "tool", ["content"] = "Ошибка: неизвестный инструмент." });
                    continue;
                }

                var display = match.Server.Name + " → " + (match.Tool["name"]?.GetValue<string>() ?? name);
                var argsText = arguments.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
                if (!await approveToolCall(display, argsText))
                {
                    messages.Add(new JsonObject { ["role"] = "tool", ["content"] = "Пользователь отклонил выполнение инструмента." });
                    continue;
                }

                try
                {
                    var originalToolName = match.Tool["name"]?.GetValue<string>() ?? "";
                    var prefix = SafeName(match.Server.Name) + "__";
                    if (originalToolName.StartsWith(prefix, StringComparison.Ordinal))
                        originalToolName = originalToolName[prefix.Length..];
                    var result = await CallToolAsync(match.Server, originalToolName, arguments, cancellationToken);
                    messages.Add(new JsonObject { ["role"] = "tool", ["content"] = result });
                }
                catch (Exception ex)
                {
                    messages.Add(new JsonObject { ["role"] = "tool", ["content"] = "Ошибка MCP: " + ex.Message });
                }
            }
        }

        throw new InvalidOperationException("Превышен лимит последовательных вызовов инструментов (5).");
    }

    public static async Task<IReadOnlyList<JsonObject>> ListToolsAsync(McpServerEntry server, CancellationToken cancellationToken = default)
    {
        await using var session = await McpSession.StartAsync(server, cancellationToken);
        var result = await session.RequestAsync("tools/list", new JsonObject(), cancellationToken);
        var list = result["tools"] as JsonArray ?? new JsonArray();
        return list.OfType<JsonObject>().Select(x => (JsonObject)x.DeepClone()).ToList();
    }

    private static async Task<string> CallToolAsync(McpServerEntry server, string toolName, JsonObject arguments, CancellationToken cancellationToken)
    {
        await using var session = await McpSession.StartAsync(server, cancellationToken);
        var result = await session.RequestAsync("tools/call", new JsonObject
        {
            ["name"] = toolName,
            ["arguments"] = arguments.DeepClone()
        }, cancellationToken);
        return result.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    private static string SafeName(string value)
    {
        var cleaned = new string(value.ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : '_').ToArray());
        return string.IsNullOrWhiteSpace(cleaned) ? "mcp" : cleaned;
    }

    private sealed class McpSession : IAsyncDisposable
    {
        private readonly Process _process;
        private int _id;

        private McpSession(Process process) => _process = process;

        public static async Task<McpSession> StartAsync(McpServerEntry server, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(server.Command))
                throw new InvalidOperationException("Не указана команда запуска MCP-сервера.");

            var start = new ProcessStartInfo
            {
                FileName = server.Command,
                WorkingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            foreach (var arg in SplitArguments(server.Arguments)) start.ArgumentList.Add(arg);
            foreach (var line in (server.EnvironmentVariables ?? "").Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var separator = line.IndexOf('=');
                if (separator > 0)
                    start.Environment[line[..separator].Trim()] = line[(separator + 1)..].Trim();
            }

            var process = new Process { StartInfo = start };
            if (!process.Start()) throw new InvalidOperationException("Не удалось запустить MCP-процесс.");
            var session = new McpSession(process);
            try
            {
                await session.RequestAsync("initialize", new JsonObject
                {
                    ["protocolVersion"] = "2024-11-05",
                    ["capabilities"] = new JsonObject(),
                    ["clientInfo"] = new JsonObject { ["name"] = "ASTER", ["version"] = "0.5" }
                }, cancellationToken);
                await session.NotifyAsync("notifications/initialized", new JsonObject(), cancellationToken);
                return session;
            }
            catch
            {
                await session.DisposeAsync();
                throw;
            }
        }

        public async Task<JsonObject> RequestAsync(string method, JsonObject parameters, CancellationToken cancellationToken)
        {
            var id = Interlocked.Increment(ref _id);
            var request = new JsonObject
            {
                ["jsonrpc"] = "2.0",
                ["id"] = id,
                ["method"] = method,
                ["params"] = parameters
            };
            await _process.StandardInput.WriteLineAsync(request.ToJsonString().AsMemory(), cancellationToken);
            await _process.StandardInput.FlushAsync(cancellationToken);

            while (true)
            {
                var line = await _process.StandardOutput.ReadLineAsync(cancellationToken)
                    ?? throw new InvalidOperationException("MCP-сервер завершил работу без ответа.");
                if (string.IsNullOrWhiteSpace(line)) continue;
                JsonObject? response;
                try { response = JsonNode.Parse(line) as JsonObject; }
                catch { continue; }
                if (response?["id"]?.GetValue<int>() != id) continue;
                if (response["error"] is JsonObject error)
                    throw new InvalidOperationException(error["message"]?.GetValue<string>() ?? "Неизвестная ошибка MCP.");
                return response["result"] as JsonObject ?? new JsonObject();
            }
        }

        private async Task NotifyAsync(string method, JsonObject parameters, CancellationToken cancellationToken)
        {
            var notification = new JsonObject
            {
                ["jsonrpc"] = "2.0",
                ["method"] = method,
                ["params"] = parameters
            };
            await _process.StandardInput.WriteLineAsync(notification.ToJsonString().AsMemory(), cancellationToken);
            await _process.StandardInput.FlushAsync(cancellationToken);
        }

        public ValueTask DisposeAsync()
        {
            try
            {
                if (!_process.HasExited) _process.Kill(entireProcessTree: true);
            }
            catch { }
            _process.Dispose();
            return ValueTask.CompletedTask;
        }

        private static IEnumerable<string> SplitArguments(string commandLine)
        {
            var current = new StringBuilder();
            var quoted = false;
            foreach (var c in commandLine ?? "")
            {
                if (c == '"') { quoted = !quoted; continue; }
                if (char.IsWhiteSpace(c) && !quoted)
                {
                    if (current.Length > 0) { yield return current.ToString(); current.Clear(); }
                }
                else current.Append(c);
            }
            if (current.Length > 0) yield return current.ToString();
        }
    }
}
