using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ASTER.Workstation.Core.Services;
using ASTER.Workstation.Data.Database;
using Microsoft.Win32;

namespace ASTER.Workstation.App;

public sealed class ChatMessageView
{
    public string Author { get; init; } = "";
    public string Content { get; init; } = "";
}

public partial class ChatWindow : UserControl
{
    private readonly SqliteDatabase _database;
    private readonly ObservableCollection<ChatMessageView> _messages = new();
    private readonly string _workspaceKey;
    private readonly string _sectionTitle;
    private readonly string _systemPrompt;
    private bool _sending;
    private readonly List<string> _attachments = new();

    public ChatWindow(
        SqliteDatabase database,
        string workspaceKey = "CHAT",
        string sectionTitle = "ИИ-чат",
        string systemPrompt = "")
    {
        InitializeComponent();
        _database = database;
        _workspaceKey = workspaceKey;
        _sectionTitle = sectionTitle;
        _systemPrompt = systemPrompt;
        SectionTitleText.Text = sectionTitle;
        CloseWorkspaceButton.Visibility = workspaceKey == "HOME" ? Visibility.Collapsed : Visibility.Visible;
        SectionSubtitleText.Text = workspaceKey == "CHAT"
            ? "Локальный ИИ Ollama · переписка хранится на этом компьютере"
            : "Отдельная рабочая переписка · инструкции ASTER применяются автоматически";
        MessagesList.ItemsSource = _messages;
        Loaded += async (_, _) => await LoadMessagesAsync();
    }

    private async Task LoadMessagesAsync()
    {
        await using var connection = _database.CreateConnection();
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT Role, Content FROM AiMessages WHERE Workspace = $workspace ORDER BY Id;";
        command.Parameters.AddWithValue("$workspace", _workspaceKey);
        await using var reader = await command.ExecuteReaderAsync();
        var loadedCount = 0;
        while (await reader.ReadAsync())
        {
            AddMessage(reader.GetString(0), reader.GetString(1));
            loadedCount++;
        }
        WorkspaceActivityLog.Write("CHAT", $"Загружена история раздела «{_sectionTitle}»: {loadedCount} сообщений.");
        ScrollToEnd();
    }

    private void Attach_Click(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu
        {
            Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(15, 32, 46)),
            Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(234, 247, 252)),
            BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(36, 81, 106)),
            PlacementTarget = AttachButton
        };
        menu.Items.Add(CreateAttachmentItem("📎  Файлы", () => AddFiles("Все файлы (*.*)|*.*")));
        menu.Items.Add(CreateAttachmentItem("🖼  Изображения", () => AddFiles("Изображения|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.webp")));
        menu.Items.Add(CreateAttachmentItem("💻  Исходный код", () => AddFiles("Файлы кода|*.cs;*.xaml;*.json;*.xml;*.py;*.js;*.ts;*.html;*.css;*.sql;*.md;*.txt")));
        menu.Items.Add(CreateAttachmentItem("📁  Папка", AddFolder));
        menu.Items.Add(CreateAttachmentItem("📸  Скриншот из файла", () => AddFiles("Изображения|*.png;*.jpg;*.jpeg;*.bmp")));
        menu.Items.Add(new Separator());
        menu.Items.Add(CreateAttachmentItem("Очистить вложения", () => { _attachments.Clear(); UpdateAttachmentsLabel(); }));
        menu.IsOpen = true;
    }

    private MenuItem CreateAttachmentItem(string title, Action action)
    {
        var item = new MenuItem { Header = title, Padding = new Thickness(12, 8, 22, 8) };
        item.Click += (_, _) => { action(); UpdateAttachmentsLabel(); };
        return item;
    }

    private void AddFiles(string filter)
    {
        var dialog = new OpenFileDialog { Title = "Выбери материалы для ASTER", Filter = filter, Multiselect = true };
        if (dialog.ShowDialog(Window.GetWindow(this)) == true)
            foreach (var file in dialog.FileNames)
                if (!_attachments.Contains(file, StringComparer.OrdinalIgnoreCase)) _attachments.Add(file);
    }

    private void AddFolder()
    {
        var dialog = new OpenFolderDialog { Title = "Выбери папку с материалами", Multiselect = false };
        if (dialog.ShowDialog(Window.GetWindow(this)) == true && !_attachments.Contains(dialog.FolderName, StringComparer.OrdinalIgnoreCase))
            _attachments.Add(dialog.FolderName);
    }

    private void UpdateAttachmentsLabel()
    {
        AttachmentsText.Text = _attachments.Count == 0 ? "" : "Вложения: " + string.Join(", ", _attachments.Select(Path.GetFileName));
        AttachButton.ToolTip = _attachments.Count == 0 ? "Добавить файлы и папки" : "Вложения: " + _attachments.Count + " · правый клик — очистить";
    }

    private void CloseWorkspace_Click(object sender, RoutedEventArgs e)
    {
        (Window.GetWindow(this) as MainWindow)?.ReturnToHome();
    }

    private void AttachButton_Clear(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        _attachments.Clear();
        UpdateAttachmentsLabel();
    }

    private void QuickAction_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string prompt } && !string.IsNullOrWhiteSpace(prompt))
        {
            MessageInput.Text = prompt;
            MessageInput.Focus();
            MessageInput.CaretIndex = MessageInput.Text.Length;
        }
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
        if (_sending || (string.IsNullOrWhiteSpace(text) && _attachments.Count == 0)) return;

        var promptText = text;
        foreach (var path in _attachments.ToArray())
        {
            if (File.Exists(path))
            {
                var extension = Path.GetExtension(path).ToLowerInvariant();
                var readable = new[] { ".txt", ".md", ".cs", ".json", ".xml", ".yaml", ".yml", ".log", ".csv", ".xaml", ".py", ".js", ".ts", ".html", ".css", ".sql" };
                if (readable.Contains(extension))
                {
                    try
                    {
                        var content = await File.ReadAllTextAsync(path);
                        if (content.Length > 20000) content = content[..20000] + "\n[Файл сокращён до 20 000 символов]";
                        promptText += "\n\n[Содержимое файла: " + Path.GetFileName(path) + "]\n" + content;
                    }
                    catch (Exception ex) { promptText += "\n[Не удалось прочитать " + Path.GetFileName(path) + ": " + ex.Message + "]"; }
                }
                else promptText += "\n[Прикреплён файл " + Path.GetFileName(path) + ". Текущая локальная модель не умеет анализировать этот тип вложения напрямую.]";
            }
            else if (Directory.Exists(path))
            {
                var files = Directory.GetFiles(path, "*", SearchOption.TopDirectoryOnly).Take(40);
                promptText += "\n[Папка " + path + ". Файлы верхнего уровня: " + string.Join(", ", files.Select(Path.GetFileName)) + "]";
            }
        }

        var settings = AiProviderSettings.Load();
        _sending = true;
        SendButton.IsEnabled = false;
        MessageInput.IsEnabled = false;
        MessageInput.Clear();

        WorkspaceActivityLog.Write("CHAT", $"Отправлено сообщение в раздел «{_sectionTitle}» (символов: {promptText.Length}, вложений: {_attachments.Count}).");
        AddMessage("user", string.IsNullOrWhiteSpace(text) ? "Материалы: " + string.Join(", ", _attachments.Select(Path.GetFileName)) : text);
        await SaveMessageAsync("user", promptText);
        _attachments.Clear();
        UpdateAttachmentsLabel();
        StatusText.Text = $"ASTER обращается к локальной модели {settings.OllamaModel}…";

        try
        {
            var history = await ReadRecentMessagesAsync(24);
            if (!string.IsNullOrWhiteSpace(_systemPrompt))
                history.Insert(0, ("system", _systemPrompt));

            var answer = await OllamaChatClient.SendAsync(settings.OllamaEndpoint, settings.OllamaModel, history);
            AddMessage("assistant", answer);
            await SaveMessageAsync("assistant", answer);
            StatusText.Text = "Ответ получен от локальной модели. История раздела сохранена.";
            WorkspaceActivityLog.Write("CHAT", $"Получен ответ Ollama в разделе «{_sectionTitle}».");
        }
        catch (Exception ex)
        {
            StatusText.Text = "Не удалось получить ответ от Ollama.";
            AddMessage("system", "Ошибка: " + ex.Message);
            WorkspaceActivityLog.Write("ERROR", "Ошибка локального AI-запроса: " + ex.Message);
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
        command.CommandText = "SELECT Role, Content FROM AiMessages WHERE Workspace = $workspace ORDER BY Id DESC LIMIT $limit;";
        command.Parameters.AddWithValue("$workspace", _workspaceKey);
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
        command.CommandText = "INSERT INTO AiMessages (Role, Content, CreatedUtc, Workspace) VALUES ($role, $content, $created, $workspace);";
        command.Parameters.AddWithValue("$role", role);
        command.Parameters.AddWithValue("$content", content);
        command.Parameters.AddWithValue("$created", DateTime.UtcNow.ToString("O"));
        command.Parameters.AddWithValue("$workspace", _workspaceKey);
        await command.ExecuteNonQueryAsync();
    }

    private void AddMessage(string role, string content)
    {
        _messages.Add(new ChatMessageView
        {
            Author = role switch { "user" => "ТЫ", "assistant" => "ASTER AI", _ => "СИСТЕМА" },
            Content = content
        });
        ScrollToEnd();
    }

    private void ScrollToEnd()
    {
        if (_messages.Count > 0) MessagesList.ScrollIntoView(_messages[^1]);
    }
}