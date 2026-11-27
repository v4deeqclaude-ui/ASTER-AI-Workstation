using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
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

    private static BitmapImage CreateHeroImage()
    {
        var bytes = Convert.FromBase64String(HeroImageBase64);
        using var stream = new MemoryStream(bytes);
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.DecodePixelWidth = 720;
        image.DecodePixelHeight = 1080;
        image.StreamSource = stream;
        image.EndInit();
        image.Freeze();
        return image;
    }

    private const string HeroImageBase64 = "/9j/4AAQSkZJRgABAQAAAQABAAD/2wBDAA4KCw0LCQ4NDA0QDw4RFiQXFhQUFiwgIRokNC43NjMuMjI6QVNGOj1OPjIySGJJTlZYXV5dOEVmbWVabFNbXVn/2wBDAQ8QEBYTFioXFypZOzI7WVlZWVlZWVlZWVlZWVlZWVlZWVlZWVlZWVlZWVlZWVlZWVlZWVlZWVlZWVlZWVlZWVlZWVn/wAARCACPAGsDASIAAhEBAxEB/8QAGgAAAwEBAQEAAAAAAAAAAAAAAwQFBgIBAP/EADwQAAIBAwMCBAMGBAMJAQAAAAECAwAEEQUSITFBEyJRYXGBkQYUMkKhsSNiwfBy0eEVJDRSY3OCorLx/8QAGQEAAwEBAQAAAAAAAAAAAAAAAgMEAQAF/8QAIhEAAgICAgMAAwEAAAAAAAAAAAECEQMhEjEiQVEEE2Fx/9oADAMBAAIRAxEAPwDJCAkjI4pu1smZ1RVJY9AB1q4+nrMiyxYYADJFV9KsBbwmZgBIwwhPYV7DjGCs8aOV5NIkyaZBpkAe4CyXTDcFPKxj1PqazN7deLIWlcu/YE8KKuagl9d6pMyMyIp2gmlH0xFXc8YY9GIH61LNykXY4pEJi7gFfNnueAKGYfMASSx/5RVh7NkyEAXHagwq0UpKOsMmDhiM0njvY5ukImzdeHUpnjzcVx91I5IIHxqrJLbjb4KNJNzveU5PyFdjT7qfLbGAPJZuB9TTY4uXQmWXj3okhFUgSK+31FdS24Cb4j4iemeR8R/Wn7jTpIXKpLDLwPwuKTe3lg2TFGjO7hiMfH9xQyxyiHHJGXTEiB6GvQF7g/Wn2s3kj8dNm1jgAkDnuAKROdx9aBqg07OWAxwDn41o7F8WUI/kFZ0896u2zYtoh/KKLGdI0f2bdnuFiycHqfbvWpYJO8irjw4lGfj6Vk/s4wjgnm53bQB8M1T0+5kOkyzgEmWZuh7Diqsit2edjXFv+nEi+JcMi8KDzS1xGoBXtjBpi2I8zSHGTnmp91qVu1x4NsGuJPROn1pbf0uiq6I9wXhlO7p0OaTuJMjbnB9x1qnfJOw/iW4HwYHip6lZB4Ug7cE0p/BlCBDLJu6H1pq41KaWJEkkJVBgDPFJXcbQMQC2O3elQzyumB379KyORx0gJY1J2x5XWVJXDDEYBOT15xx9aBJK7oBvYqOACegroW6xkq3Le2OKHNBJDkhSB1IIo220Ckkw9nFJcqYUySeQCeFIry6tZYkSSQYz5T7EVV0K2jJFxMcRry2Dz7U9r1qqrcIH8pUTKCO/f9zWyxeKYMcy5uBkcVdt0/3eL/AP2qGa0cCH7tD/ANtf2FDANsp6IT/s+49wFX5ZP9RT9rdQw6dBaFwJW3OV9Mk1IspjHbQqPzBmP1x/Sl7i9T+EkUQ8dRhiBycd/pVE9ImxJN7KurozW4RX2g9feoEk0lqyxWqHLdx1NXLqWSS0tklTbKE8w+fH6Yruy09ZHjk5R1BAOfWp3G2Wp0tECSa/jdRKN27oAabazZY1lcAZ6itC2mwxsZAMt6mpV62CVHQVlJMJW1sh6hAXj8hwRyMmj6RpomO55I0EanAZgOQM/uf0ri6I8CQNzxxU63nkJYb2JPP1GDWWkxck2V1s/AtI5I2V7idsDn8P+vT60eTT7u0uo7W886TgY3HO0noRUu2ldofCZgJVO5Ce/t/fpTqXkzXEV1escW+NqseWI6DFVwacURTUlJiQvJrZ2tk4VG4AAp24v1v45DKxMqx9AfxDac/qB9aiXE/iXckqkDJruMJFC8plV3cFVVTyB71PKbapFUYRUlJrYvWp2ZSMxZ8Mxptz/hFZhhn0ywyK2Vkimxtj/wBJP/kV0GDk0SbefCx5/JkfI/8A6aqaZZxy3ZnkCYiU8k4znp/fxqHbnBBPSruhRrc3IgdiqujDIPfrVL3EQqUlZ7PKxlaRhuGetVtPmRoFI6GpV1oz21wSZSST2YkfSqFoiwQqnpU0bZ6Dr0Gupzg4NQ7g5JJqrOwIqbIuWrq2a5aJZgLbnY5Zu3oKlTRPaTb8eXPB/p8K0TrgH2qJrLAJGMeZjWSjUbE8t0CjuYMsXTxCy8ZbG00nJNycHLdvQV9tDHeVAz0AHAoLKyOdww3celJcnQajs6UebJPWvYTh9uQFbgkjge9cqQXBbgfvTIQOSwXamDgDrQ6qzabdHkQ3TeZsqCRkVrbYsLSADoIk/YVm4oxFEQw855+daqyRfuVvnr4a/sKoxqifMyCuBAhx+Y8/IVU0p2S5idDhkOaTVQ1pGuDnzEY+IqlosJknUfKrIUSZpNRs61NL261KUh9sKnh2bAHAOAKHZxXrFfEuvIDngda0NzaxX7iEvsKglTSf3WO2UjxN+O9SOFM9HHmU4JewErcUoxo0zr2qVeX6QgjOWPTFb0cxrxljkBZA6jqG6Gs9rdzHfagBbptiQbfn3NAu72WVjkkJ2wMZpnQ7A6hdiPnnk7ep9hQynz8EL4qL/Yxe5QQyLGGVtvcHjpQmhJEhJJI6Z60XVI1ivHjiUogOME88UezxJCC2WcYBJpfHycQ1K4qRMVQRjB3Z71cs1iTzTZD4wPQf50NYo4ZVbZux2rxWaPcvBU8c1sYcXsJytHMrAM2G3/zEVrGRFWIRghPCTAJ/lFZQpvU4Uc85rUbwyRMOAY04/wDEVRBEf5F6ogg7WhBwPKx/etXo0fhQrMByw4zWTuJF+9IpAjCLtO0Z7c9fia0egzu2lHc2TGxVSew60yL7QrNFtRZ3LcslwZAc4zSct4hJ3s+0egri4m3SOAMn271KkcsXPQelHNJ9hYnKKdDtxfxeCUjiO5ujMf6VnJnk+9gyjkHj0x7VSYHjOfajaiVuNMikESh7UBSw7qSevzx9anyRtf4PhKnTIt4h8VCRtUjcM8Zqhp922mRrcRNibt8SKn3LGZogDz0xRblCJo488BBUyflaHtapi0srSztLJ5z1PPWtHEkNzpEF1GgV4l2SBRj5/wB+lZ+9WMXTfdx/CJwvckUewvbixclG8jfiQ8g/LpTIS4yakKyRcopxKTLlctjk5FAETyHyjOOSfSurO4admYhd44GOn0o+GC7T60+uW0DdOmAUhVZR1Hc1RimKwxjBOEX9qmzDsvaiKSqKCT0HesujpLl2BvEJvJduceIRkfGqOjXRS0mg3nduzj0Hc1JlYmUsQSWJNN6cUjMjscdhWQfnYM43Ch64PlZsfiGFNKJG24EjI689DR7sZlwBkYHyoqRp92cs2WzhV9Ke9sGC1on4Ziz44z26CjwnEMkTn+HNhD7E5wfriu/CG3Iz70K6DJp08gwANoB99w6UDVJtm3bSQhY2rO7uykJCec9uv+VKy77iXyjduNV53aHTki8sUl3ulckfhXt9cf8AtQdP0S51Bh91hdIiRl36dP1qVw2PU7R7Fpn3bJlYEk8Ht8q+uLJRGZZSIogcbm7/AAFa5dHS1hEl3MqxxL5nfkgAVE09YftJq7K0TiytjuXJ6j39z+gGKKVLSOjdXInWOj3N2qywq0MB/OwyzfAVVbR/AUEGR2Hd2J/0rdWNnD1Y4GMAdqU1YRQxPIR5FUnp6VqpOgbb2YxTGilbmBWQdGAwR9KSuREJ2EWfD4xnr0rjVpCh8SdjubO2MdqDHlo0bBGVBx8qNt9MFJPaDzQiMLKHBZwcIvVfjXMMQ8J2ckHjAx1rnbk8UaNc1q7ObHoSWZZHQgMPKfXFULTS57qM+EhYE5zQdPtZLmeNCTgdT6CtJd6gba3hsbNVWWc7UHfHdqY5NLQp710RrqytrC0eS5kL7ONsYzz6ZqbYafNrksazhbW23jyewGcD3Pf4CrVy7Q3S6eiobREDSbhksc8D68160FyX+8c/h2Kh8uB6/OskuStsVCdS4pC2pW2nW93uRDczjgFvwIB2ApmLUpGhFvbxbpDxx0HxPavE01c75yHJ/IOg+PrXc8sdjZzPGqqI0LAKMDgVjaSHKLlXox32l1OVpjp6SFo42zIc/jfv8h0rQ/YxYoNKkkXGZJSMgegH+tYaFGurhQDvkkPmJ9TWu+yc2yxltnIEkUpBHxqaD5S2UyVRNS12VOAeKHeSC6sZIzg8ZpGSQHBFcLPg4PSnNUAtmS+03/HpxhfD4/WvIWzBHkc7R+1UdetVlGGOMYKEc4qZxGAnXaAP0rHqTf0GK8a+BAMmjwqVNB37Y8hRnPrTyAMmQp+tEkcyrpVx4fiAnG5cfrT2mB5GudReMh2/hw7uyDv8zUvSLGS8uDv4hX8WOvwrR3kuNltDgORknHCqKL3RPketA1SNbhptpknfpnt2/s0ZweCxyf2r2JVjTgkserHqaWmuQFyDgfDrXdvQMVx7PJpQnGeaia1dKmnXILfiQqPnxQr/AFBy+2LJ9c0teKXs3LDO7ArJK1SKYOtsg6fMtnFJKFDOyYBPbvmp7SNJLuyc+tHy/hbMcjiuERopQ+3I96ja6RSmW9J1h1QQXDlhngt1Hz71aMncHistawCaKV84ZT5faqOmO72zpvIMbFRnnjtTot0gHVlS5xJF5qhTMplbHTtijalNKLdo2G3P5geDSezAA9hXXbMaP//Z";


    public ChatWindow(
        SqliteDatabase database,
        string workspaceKey = "CHAT",
        string sectionTitle = "ИИ-чат",
        string systemPrompt = "")
    {
        InitializeComponent();
        HeroImage.Source = CreateHeroImage();
        var silhouette = Geometry.Parse("M 132,24 L 121,31 120,34 108,45 89,81 91,85 89,102 91,104 91,113 93,114 92,129 95,132 97,140 100,145 100,154 107,155 109,157 114,173 125,192 126,198 131,203 150,202 151,205 152,201 161,201 166,207 168,214 172,219 177,239 173,244 173,270 170,272 168,277 160,285 152,289 146,295 141,295 132,299 117,311 109,327 109,339 107,347 88,361 82,368 79,368 57,392 56,409 62,428 71,436 72,439 81,448 84,449 78,467 76,479 319,479 319,249 301,240 286,239 275,241 263,234 258,233 254,228 251,227 251,222 241,207 241,195 239,194 238,190 241,183 255,183 264,181 270,172 277,166 280,166 282,161 286,159 287,154 293,143 291,139 291,131 293,130 293,126 291,123 292,119 289,115 283,112 285,102 282,86 280,83 280,78 270,63 268,57 259,56 258,52 253,45 242,37 229,31 208,28 199,24 181,20 173,16 156,16 Z" ).Clone();
        silhouette.Transform = new System.Windows.Media.ScaleTransform(260d / 320d, 390d / 480d);
        silhouette.Freeze();
        HeroImage.Clip = silhouette;
        _database = database;
        _workspaceKey = workspaceKey;
        _sectionTitle = sectionTitle;
        _systemPrompt = systemPrompt;
        CloseWorkspaceButton.Visibility = workspaceKey == "HOME" ? Visibility.Collapsed : Visibility.Visible;
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