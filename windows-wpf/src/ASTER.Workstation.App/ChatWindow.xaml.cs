using System.Collections.ObjectModel;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ASTER.Workstation.Core.Services;
using ASTER.Workstation.Data.Database;
using Microsoft.Data.Sqlite;

namespace ASTER.Workstation.App;

public sealed class ChatMessageView
{
    public string Author { get; init; } = "";
    public string Content { get; init; } = "";
    public string BubbleBrush { get; init; } = "#202630";
}

public partial class ChatWindow : UserControl
{
    private readonly SqliteDatabase _database;
    private readonly ObservableCollection<ChatMessageView> _messages = new();
    private bool _sending;

    public ChatWindow(SqliteDatabase database)
    {
        InitializeComponent();
        _database = database;
        MessagesList.ItemsSource = _messages;
        Loaded += async (_, _) => await LoadMessagesAsync();
    }

    private async Task LoadMessagesAsync()
    {
        await using var connection = _database.CreateConnection();
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT Role, Content FROM AiMessages ORDER BY Id;";
        await using var reader = await command.ExecuteReaderAsync();
        var loadedCount = 0;
        while (await reader.ReadAsync()) { AddMessage(reader.GetString(0), reader.GetString(1)); loadedCount++; }
        WorkspaceActivityLog.Write("CHAT", $"Загружена история AI-чата: {loadedCount} сообщений.");
        ScrollToEnd();
    }

    private async void Send_Click(object sender, RoutedEventArgs e) => await SendMessageAsync();

    private async void MessageInput_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && (Keyboard.Modifiers & ModifierKeys.Shift) == 0)
        {
            e.Handled = true;
            await SendMessageAsync();
        }
    }

    private async Task SendMessageAsync()
    {
        var text = MessageInput.Text.Trim();
        if (_sending || string.IsNullOrWhiteSpace(text)) return;

        var providerSettings = AiProviderSettings.Load();
        var provider = providerSettings.Provider;
        var apiKey = provider == "openai" ? WindowsCredentialStore.ReadApiKey() : null;
        var sharing = false;

        if (provider == "openai" && string.IsNullOrWhiteSpace(apiKey))
        {
            StatusText.Text = "Для OpenAI API сначала сохрани ключ в настройках ASTER.";
            MessageBox.Show("Выбран OpenAI API, но ключ не сохранён. Открой настройки ASTER и добавь API-ключ. Использование API оплачивается отдельно.",
                "ASTER — требуется API-ключ", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (provider == "chatgpt")
        {
            try
            {
                var session = await ChatGPTBridgeService.Shared.GetSessionAsync();
                sharing = session.ValueKind == JsonValueKind.Object &&
                    session.TryGetProperty("status", out var status) && status.GetString() == "connected" &&
                    session.TryGetProperty("sharing", out var share) && share.GetBoolean();
            }
            catch { sharing = false; }

            if (!sharing)
            {
                StatusText.Text = "ChatGPT plan sharing не подключён. Выбери Ollama или OpenAI API в настройках.";
                return;
            }
        }

        _sending = true;
        SendButton.IsEnabled = false;
        MessageInput.IsEnabled = false;
        MessageInput.Clear();
        WorkspaceActivityLog.Write("CHAT", $"Отправлено сообщение ассистенту (символов: {text.Length}). Содержимое не записывается в системный журнал.");
        AddMessage("user", text);
        await SaveMessageAsync("user", text);
        StatusText.Text = provider switch
        {
            "ollama" => $"ASTER обращается к локальной модели {providerSettings.OllamaModel}…",
            "chatgpt" => "ASTER использует подключение ChatGPT…",
            _ => "ASTER отправляет запрос через OpenAI API…"
        };

        try
        {
            var history = await ReadRecentMessagesAsync(24);
            string answer;
            if (provider == "ollama")
            {
                answer = await OllamaChatClient.SendAsync(
                    providerSettings.OllamaEndpoint, providerSettings.OllamaModel, history);
            }
            else if (provider == "chatgpt")
            {
                var modelsElement = await ChatGPTBridgeService.Shared.RequestModelsAsync();
                var model = modelsElement.EnumerateArray().Select(x => x.TryGetProperty("slug", out var slug) ? slug.GetString() : null)
                    .FirstOrDefault(x => !string.IsNullOrWhiteSpace(x));
                if (string.IsNullOrWhiteSpace(model)) throw new InvalidOperationException("ChatGPT не вернул доступные модели.");
                answer = await ChatGPTBridgeService.Shared.SendAsync(model, history);
            }
            else
            {
                answer = await OpenAiResponsesClient.SendAsync(apiKey!, "gpt-4.1-mini", history);
            }

            AddMessage("assistant", answer);
            await SaveMessageAsync("assistant", answer);
            StatusText.Text = provider switch
            {
                "ollama" => "Ответ получен от локальной модели. История сохранена на компьютере.",
                "chatgpt" => "Ответ получен через ChatGPT. История сохранена локально.",
                _ => "Ответ получен через OpenAI API. История сохранена локально."
            };
            WorkspaceActivityLog.Write("CHAT", $"Получен ответ от провайдера {provider}; история сохранена.");
        }
        catch (Exception ex)
        {
            StatusText.Text = "Не удалось получить ответ.";
            AddMessage("system", "Ошибка: " + ex.Message);
            WorkspaceActivityLog.Write("ERROR", "Ошибка AI-запроса: " + ex.Message);
        }
        finally
        {
            _sending = false;
            SendButton.IsEnabled = true;
            MessageInput.IsEnabled = true;
            MessageInput.Focus();
        }
    }

    private async Task<List<(string Role, string Content)>> ReadRecentMessagesAsync(int limit)
    {
        var items = new List<(string Role, string Content)>();
        await using var connection = _database.CreateConnection();
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT Role, Content FROM AiMessages ORDER BY Id DESC LIMIT $limit;";
        command.Parameters.AddWithValue("$limit", limit);
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync()) items.Add((reader.GetString(0), reader.GetString(1)));
        items.Reverse();
        return items;
    }

    private async Task SaveMessageAsync(string role, string content)
    {
        await using var connection = _database.CreateConnection();
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO AiMessages (Role, Content, CreatedUtc) VALUES ($role, $content, $created);";
        command.Parameters.AddWithValue("$role", role);
        command.Parameters.AddWithValue("$content", content);
        command.Parameters.AddWithValue("$created", DateTime.UtcNow.ToString("O"));
        await command.ExecuteNonQueryAsync();
    }

    private void AddMessage(string role, string content)
    {
        var isUser = role == "user";
        _messages.Add(new ChatMessageView
        {
            Author = role switch { "user" => "ТЫ", "assistant" => "ASTER AI", _ => "СИСТЕМА" },
            Content = content,
            BubbleBrush = isUser ? "#29313D" : "#202A27"
        });
        ScrollToEnd();
    }

    private void ScrollToEnd()
    {
        if (_messages.Count > 0) MessagesList.ScrollIntoView(_messages[^1]);
    }
}
