using System.Net;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ASTER.Workstation.App;

/// <summary>
/// MCP Streamable HTTP client for the 2025-11-25 protocol revision.
/// Supports JSON and request-scoped SSE responses, negotiated session IDs,
/// and explicit protocol-version headers.
/// </summary>
internal sealed class McpStreamableHttpClient : IAsyncDisposable
{
    private const string ProtocolVersion = "2025-11-25";
    private readonly HttpClient _http;
    private readonly Uri _endpoint;
    private string? _sessionId;
    private string _negotiatedProtocolVersion = ProtocolVersion;
    private int _nextId;
    private bool _initialized;

    public McpStreamableHttpClient(string endpoint)
    {
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
            throw new InvalidOperationException("Для Streamable HTTP укажи корректный адрес http:// или https://.");

        if (uri.Scheme == Uri.UriSchemeHttp && !IPAddress.TryParse(uri.Host, out _) &&
            !string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Незащищённый HTTP разрешён только для localhost или IP-адреса. Для удалённых серверов используй HTTPS.");

        _endpoint = uri;
        _http = new HttpClient(new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
        })
        {
            Timeout = TimeSpan.FromMinutes(3)
        };
    }

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        var result = await SendRequestAsync("initialize", new JsonObject
        {
            ["protocolVersion"] = ProtocolVersion,
            ["capabilities"] = new JsonObject(),
            ["clientInfo"] = new JsonObject { ["name"] = "ASTER", ["version"] = "0.6" }
        }, includeSession: false, cancellationToken);

        var negotiated = result["protocolVersion"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(negotiated))
            throw new InvalidOperationException("MCP-сервер не вернул согласованную версию протокола.");
        _negotiatedProtocolVersion = negotiated;

        await SendNotificationAsync("notifications/initialized", new JsonObject(), cancellationToken);
        _initialized = true;
    }

    public async Task<JsonObject> RequestAsync(string method, JsonObject parameters, CancellationToken cancellationToken)
    {
        if (!_initialized && method != "initialize")
            throw new InvalidOperationException("MCP HTTP-сессия ещё не инициализирована.");
        return await SendRequestAsync(method, parameters, includeSession: _initialized, cancellationToken);
    }

    private async Task<JsonObject> SendRequestAsync(string method, JsonObject parameters, bool includeSession, CancellationToken cancellationToken)
    {
        var id = Interlocked.Increment(ref _nextId);
        var payload = new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id,
            ["method"] = method,
            ["params"] = parameters.DeepClone()
        };
        using var request = CreatePost(payload, includeSession);
        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var errorText = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException($"MCP HTTP вернул {(int)response.StatusCode} ({response.ReasonPhrase}): {Trim(errorText)}");
        }

        if (response.Headers.TryGetValues("Mcp-Session-Id", out var sessionValues))
            _sessionId = sessionValues.FirstOrDefault();

        var mediaType = response.Content.Headers.ContentType?.MediaType ?? "";
        JsonObject? message;
        if (mediaType.Equals("application/json", StringComparison.OrdinalIgnoreCase))
        {
            var text = await response.Content.ReadAsStringAsync(cancellationToken);
            message = JsonNode.Parse(text) as JsonObject;
        }
        else if (mediaType.Equals("text/event-stream", StringComparison.OrdinalIgnoreCase))
        {
            message = await ReadSseResponseAsync(response, id, cancellationToken);
        }
        else
        {
            var text = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException($"MCP HTTP вернул неподдерживаемый Content-Type '{mediaType}'. Ответ: {Trim(text)}");
        }

        if (message is null) throw new InvalidOperationException("MCP HTTP не вернул JSON-RPC ответ.");
        if (message["error"] is JsonObject error)
            throw new InvalidOperationException(error["message"]?.GetValue<string>() ?? "Ошибка JSON-RPC MCP.");
        if (message["id"]?.ToJsonString() != JsonValue.Create(id)!.ToJsonString())
            throw new InvalidOperationException("MCP HTTP вернул ответ с неожиданным идентификатором запроса.");
        return message["result"] as JsonObject ?? new JsonObject();
    }

    private async Task<JsonObject?> ReadSseResponseAsync(HttpResponseMessage response, int expectedId, CancellationToken cancellationToken)
    {
        using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        var data = new StringBuilder();
        while (!reader.EndOfStream)
        {
            var line = await reader.ReadLineAsync(cancellationToken);
            if (line is null) break;
            if (line.Length == 0)
            {
                if (data.Length == 0) continue;
                var payload = data.ToString().TrimEnd('\n');
                data.Clear();
                JsonObject? item;
                try { item = JsonNode.Parse(payload) as JsonObject; }
                catch (JsonException) { continue; }
                if (item?["id"]?.ToJsonString() == JsonValue.Create(expectedId)!.ToJsonString())
                    return item;
                continue;
            }
            if (line.StartsWith(":", StringComparison.Ordinal)) continue;
            if (line.StartsWith("data:", StringComparison.Ordinal))
            {
                var value = line.Length > 5 && line[5] == ' ' ? line[6..] : line[5..];
                data.Append(value).Append('\n');
            }
        }
        if (data.Length > 0)
        {
            try
            {
                var item = JsonNode.Parse(data.ToString()) as JsonObject;
                if (item?["id"]?.ToJsonString() == JsonValue.Create(expectedId)!.ToJsonString()) return item;
            }
            catch (JsonException) { }
        }
        throw new InvalidOperationException("Поток MCP SSE завершился до получения ответа на запрос.");
    }

    private async Task SendNotificationAsync(string method, JsonObject parameters, CancellationToken cancellationToken)
    {
        var payload = new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["method"] = method,
            ["params"] = parameters.DeepClone()
        };
        using var request = CreatePost(payload, includeSession: true);
        using var response = await _http.SendAsync(request, cancellationToken);
        if (response.StatusCode != HttpStatusCode.Accepted && !response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException($"MCP не принял уведомление: HTTP {(int)response.StatusCode}. {Trim(body)}");
        }
    }

    private HttpRequestMessage CreatePost(JsonObject payload, bool includeSession)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, _endpoint);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        request.Headers.TryAddWithoutValidation("MCP-Protocol-Version", _negotiatedProtocolVersion);
        if (includeSession && !string.IsNullOrWhiteSpace(_sessionId))
            request.Headers.TryAddWithoutValidation("Mcp-Session-Id", _sessionId);
        request.Content = new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json");
        return request;
    }

    public async ValueTask DisposeAsync()
    {
        if (!string.IsNullOrWhiteSpace(_sessionId))
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Delete, _endpoint);
                request.Headers.TryAddWithoutValidation("Mcp-Session-Id", _sessionId);
                using var response = await _http.SendAsync(request);
            }
            catch { /* Session cleanup is best-effort. */ }
        }
        _http.Dispose();
    }

    private static string Trim(string value) => value.Length <= 1200 ? value : value[..1200] + "…";
}
