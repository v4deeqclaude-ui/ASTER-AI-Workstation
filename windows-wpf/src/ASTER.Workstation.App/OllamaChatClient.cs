using System.Text;
using System.Text.Json;

namespace ASTER.Workstation.App;

public static class OllamaChatClient
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(5) };

    public static async Task<List<string>> GetInstalledModelsAsync(string endpoint, CancellationToken cancellationToken = default)
    {
        var baseUri = NormalizeEndpoint(endpoint);
        using var response = await Http.GetAsync(new Uri(baseUri, "api/tags"), cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Ollama ({(int)response.StatusCode}): {body}");

        using var document = JsonDocument.Parse(body);
        var models = new List<string>();
        if (document.RootElement.TryGetProperty("models", out var array) && array.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in array.EnumerateArray())
                if (item.TryGetProperty("name", out var name) && name.ValueKind == JsonValueKind.String)
                    models.Add(name.GetString() ?? "");
        }
        return models.Where(x => !string.IsNullOrWhiteSpace(x)).ToList();
    }

    public static async Task<string> SendAsync(
        string endpoint,
        string model,
        IReadOnlyList<(string Role, string Content)> history,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(model))
            throw new InvalidOperationException("Не указана локальная модель Ollama.");

        var baseUri = NormalizeEndpoint(endpoint);
        var messages = history.Select(item => new
        {
            role = item.Role == "assistant" ? "assistant" : item.Role == "system" ? "system" : "user",
            content = item.Content
        }).ToArray();

        var payload = JsonSerializer.Serialize(new { model, messages, stream = false });
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(baseUri, "api/chat"))
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };
        using var response = await Http.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            string detail;
            try
            {
                using var error = JsonDocument.Parse(body);
                detail = error.RootElement.TryGetProperty("error", out var value) ? value.GetString() ?? body : body;
            }
            catch { detail = body; }
            throw new InvalidOperationException($"Ollama ({(int)response.StatusCode}): {detail}");
        }

        using var document = JsonDocument.Parse(body);
        if (document.RootElement.TryGetProperty("message", out var message) &&
            message.TryGetProperty("content", out var content) &&
            content.ValueKind == JsonValueKind.String)
        {
            var answer = content.GetString();
            if (!string.IsNullOrWhiteSpace(answer)) return answer.Trim();
        }
        throw new InvalidOperationException("Ollama вернул пустой ответ. Проверь выбранную модель.");
    }

    private static Uri NormalizeEndpoint(string endpoint)
    {
        if (!Uri.TryCreate(endpoint.Trim().TrimEnd('/') + "/", UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            throw new InvalidOperationException("Неверный адрес Ollama. Используй http://localhost:11434.");
        return uri;
    }
}
