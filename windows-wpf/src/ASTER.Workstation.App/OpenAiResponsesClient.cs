using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using ASTER.Workstation.Core.Services;

namespace ASTER.Workstation.App;

public static class OpenAiResponsesClient
{
    private static readonly HttpClient Http = CreateHttpClient();

    private static HttpClient CreateHttpClient()
    {
        var proxyPath = Path.Combine(AppPaths.Root, "chatgpt-proxy.txt");
        var proxySetting = File.Exists(proxyPath) ? File.ReadAllText(proxyPath).Trim() : "http://127.0.0.1:10809";
        HttpMessageHandler handler = string.Equals(proxySetting, "direct", StringComparison.OrdinalIgnoreCase)
            ? new HttpClientHandler()
            : new HttpClientHandler { Proxy = new WebProxy(proxySetting), UseProxy = true };
        return new HttpClient(handler) { Timeout = TimeSpan.FromMinutes(2) };
    }

    public static async Task<string> SendAsync(
        string apiKey,
        string model,
        IReadOnlyList<(string Role, string Content)> history,
        CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/responses");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        var input = history.Select(item => new
        {
            role = item.Role,
            content = item.Content
        }).ToArray();
        request.Content = new StringContent(
            JsonSerializer.Serialize(new { model, input }),
            Encoding.UTF8,
            "application/json");

        using var response = await Http.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            string detail;
            try
            {
                using var error = JsonDocument.Parse(body);
                detail = error.RootElement.GetProperty("error").GetProperty("message").GetString() ?? body;
            }
            catch { detail = body; }
            throw new InvalidOperationException($"OpenAI API ({(int)response.StatusCode}): {detail}");
        }

        using var document = JsonDocument.Parse(body);
        if (document.RootElement.TryGetProperty("output_text", out var outputText))
        {
            var text = outputText.GetString();
            if (!string.IsNullOrWhiteSpace(text)) return text;
        }

        var parts = new List<string>();
        if (document.RootElement.TryGetProperty("output", out var output) && output.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in output.EnumerateArray())
            {
                if (!item.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Array) continue;
                foreach (var part in content.EnumerateArray())
                    if (part.TryGetProperty("text", out var value) && value.ValueKind == JsonValueKind.String)
                        parts.Add(value.GetString() ?? "");
            }
        }
        var answer = string.Join(Environment.NewLine, parts).Trim();
        return string.IsNullOrWhiteSpace(answer) ? "Модель вернула пустой ответ." : answer;
    }
}