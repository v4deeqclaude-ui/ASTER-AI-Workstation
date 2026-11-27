using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace ASTER.Workstation.App;

public enum CatalogKind { Skill, Plugin }

public sealed class CatalogEntry
{
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string Details { get; set; } = "";
    public string EntryType { get; set; } = "";
}

public sealed class McpServerEntry
{
    public string Name { get; set; } = "";
    public string Command { get; set; } = "";
    public string Arguments { get; set; } = "";
    public string EnvironmentVariables { get; set; } = "";
    public string TransportType { get; set; } = "stdio";
    public string Endpoint { get; set; } = "";
    public bool Enabled { get; set; } = true;
}

public sealed class CatalogWorkspaceControl : UserControl
{
    private sealed record MarketplaceItem(string Name, string Category, string Description, string Command, string Arguments, string Id);

    private static readonly HttpClient RegistryHttp = new() { Timeout = TimeSpan.FromSeconds(25) };
    private const string RegistryBaseUrl = "https://registry.modelcontextprotocol.io/v0.1/servers";
    private sealed record RegistryServer(string Name, string Title, string Description, string Version, string Status, string RegistryType, string Identifier, string RuntimeHint, string Transport, string RepositoryUrl, string WebsiteUrl, bool HasRequiredEnvironment);
    private readonly List<RegistryServer> _registryItems = new();
    private TextBox? _registrySearchBox;
    private TextBlock? _registryStatus;
    private bool _registryLoading;
    private string _registryMessage = "Загружаем официальный MCP Registry…";

    private readonly CatalogKind _kind;
    private readonly StackPanel _items = new();
    private readonly TextBlock _count = new();
    private readonly string _filePath;
    private readonly string _mcpFilePath;
    private string _activePluginTab = "marketplace";
    private readonly string? _selectedIntegration;

    public CatalogWorkspaceControl(CatalogKind kind, string? selectedIntegration = null)
    {
        _kind = kind;
        _selectedIntegration = string.IsNullOrWhiteSpace(selectedIntegration) ? null : selectedIntegration.Trim();
        if (kind == CatalogKind.Plugin && _selectedIntegration is not null)
            _activePluginTab = "integrations";
        var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ASTER", "Catalogs");
        Directory.CreateDirectory(folder);
        _filePath = Path.Combine(folder, kind == CatalogKind.Skill ? "skills.json" : "plugins.json");
        _mcpFilePath = Path.Combine(folder, "mcp-servers.json");

        if (kind == CatalogKind.Plugin)
        {
            BuildPluginCenter();
            return;
        }

        BuildSkillLibrary();
    }

    private static System.Windows.Media.Brush Brush(string value) =>
        (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFromString(value)!;

    private Grid CreateRoot()
    {
        var root = new Grid { Margin = new Thickness(24) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        return root;
    }

    private void BuildSkillLibrary()
    {
        var root = CreateRoot();
        var heading = new StackPanel { Margin = new Thickness(0, 0, 0, 18) };
        heading.Children.Add(new TextBlock { Text = "SKILL LIBRARY", Foreground = Brush("#16C8F5"), FontSize = 10, FontWeight = FontWeights.SemiBold });
        heading.Children.Add(new TextBlock { Text = "Навыки ASTER", Foreground = Brush("#EAF7FC"), FontSize = 26, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 5, 0, 4) });
        heading.Children.Add(new TextBlock { Text = "Каталог инструкций и навыков. Здесь нет чата.", Foreground = Brush("#91A7B5"), FontSize = 12 });
        Grid.SetRow(heading, 0);
        root.Children.Add(heading);

        var actions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 14) };
        var add = PrimaryButton("＋  Добавить файлы");
        add.Click += (_, _) => AddSkillFiles();
        actions.Children.Add(add);
        var plus = SecondaryButton("+");
        plus.Width = 38;
        plus.Margin = new Thickness(8, 0, 0, 0);
        plus.ToolTip = "Создать навык вручную";
        plus.Click += (_, _) => AddEntry();
        actions.Children.Add(plus);
        ConfigureCount(actions);
        Grid.SetRow(actions, 1);
        root.Children.Add(actions);
        AddItemsArea(root);
        Content = root;
        Refresh();
    }

    private void BuildPluginCenter()
    {
        var root = CreateRoot();
        var heading = new StackPanel { Margin = new Thickness(0, 0, 0, 15) };
        heading.Children.Add(new TextBlock { Text = "ASTER · EXTENSIONS", Foreground = Brush("#16C8F5"), FontSize = 10, FontWeight = FontWeights.SemiBold });
        heading.Children.Add(new TextBlock { Text = "Центр плагинов", Foreground = Brush("#EAF7FC"), FontSize = 26, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 5, 0, 4) });
        heading.Children.Add(new TextBlock { Text = "Marketplace, установленные интеграции и MCP-серверы в одном месте.", Foreground = Brush("#91A7B5"), FontSize = 12 });
        Grid.SetRow(heading, 0);
        root.Children.Add(heading);

        var toolbar = new Grid { Margin = new Thickness(0, 0, 0, 14) };
        toolbar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        toolbar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        toolbar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        toolbar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        toolbar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        toolbar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        AddTab(toolbar, "Приложения", "integrations", 0);
        AddTab(toolbar, "Marketplace", "marketplace", 1);
        AddTab(toolbar, "Мои плагины", "installed", 2);
        AddTab(toolbar, "MCP-серверы", "mcp", 3);
        var plus = PrimaryButton("+");
        plus.Width = 42;
        plus.FontSize = 20;
        plus.ToolTip = "Добавить плагин или MCP-сервер";
        plus.Click += async (_, _) => { _activePluginTab = "marketplace"; RefreshPluginView(); await LoadRegistryServersAsync(""); };
        Grid.SetColumn(plus, 5);
        toolbar.Children.Add(plus);
        ConfigureCount(toolbar);
        Grid.SetRow(toolbar, 1);
        root.Children.Add(toolbar);
        AddItemsArea(root);
        Content = root;
        RefreshPluginView();
        _ = LoadRegistryServersAsync("");
    }

    private void AddTab(Grid toolbar, string title, string key, int column)
    {
        var button = SecondaryButton(title);
        button.Margin = new Thickness(0, 0, 8, 0);
        if (_activePluginTab == key)
        {
            button.Background = Brush("#16445B");
            button.BorderBrush = Brush("#16C8F5");
            button.Foreground = Brush("#A8F1FF");
        }
        button.Click += (_, _) => { _activePluginTab = key; RefreshPluginView(); };
        Grid.SetColumn(button, column);
        toolbar.Children.Add(button);
    }

    private void ConfigureCount(Panel actions)
    {
        _count.VerticalAlignment = VerticalAlignment.Center;
        _count.Foreground = Brush("#91A7B5");
        _count.Margin = new Thickness(14, 0, 0, 0);
        actions.Children.Add(_count);
        if (actions is Grid grid)
        {
            Grid.SetColumn(_count, 3);
            _count.HorizontalAlignment = HorizontalAlignment.Left;
        }
    }

    private void AddItemsArea(Grid root)
    {
        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        _items.Margin = new Thickness(0, 2, 8, 8);
        scroll.Content = _items;
        Grid.SetRow(scroll, 2);
        root.Children.Add(scroll);
    }

    private Button PrimaryButton(string text) => new()
    {
        Content = text, Padding = new Thickness(15, 8, 15, 8),
        Background = Brush("#16C8F5"), Foreground = Brush("#06131C"),
        BorderBrush = Brush("#16C8F5"), FontWeight = FontWeights.SemiBold
    };

    private Button SecondaryButton(string text) => new()
    {
        Content = text, Padding = new Thickness(12, 8, 12, 8),
        Background = Brush("#142A3A"), Foreground = Brush("#C8E5F0"),
        BorderBrush = Brush("#29495D")
    };

    private void ShowAddMenu(FrameworkElement target)
    {
        var menu = new ContextMenu
        {
            Background = Brush("#111D2A"), Foreground = Brush("#EAF7FC"),
            BorderBrush = Brush("#29495D"), PlacementTarget = target
        };
        menu.Items.Add(MenuItem("Открыть Marketplace", () => { _activePluginTab = "marketplace"; RefreshPluginView(); _ = LoadRegistryServersAsync(""); }));
        menu.Items.Add(MenuItem("Добавить MCP-сервер вручную", () => AddMcpServer()));
        menu.Items.Add(MenuItem("Добавить программу (.exe)", AddDesktopApp));
        menu.IsOpen = true;
    }

    private MenuItem MenuItem(string title, Action action)
    {
        var item = new MenuItem { Header = title, Padding = new Thickness(12, 8, 22, 8) };
        item.Click += (_, _) => action();
        return item;
    }

    private List<CatalogEntry> ReadEntries()
    {
        try { return File.Exists(_filePath) ? JsonSerializer.Deserialize<List<CatalogEntry>>(File.ReadAllText(_filePath)) ?? new() : new(); }
        catch { return new(); }
    }

    private void WriteEntries(List<CatalogEntry> entries) =>
        File.WriteAllText(_filePath, JsonSerializer.Serialize(entries, new JsonSerializerOptions { WriteIndented = true }));

    private List<McpServerEntry> ReadMcpServers()
    {
        try { return File.Exists(_mcpFilePath) ? JsonSerializer.Deserialize<List<McpServerEntry>>(File.ReadAllText(_mcpFilePath)) ?? new() : new(); }
        catch { return new(); }
    }

    private void WriteMcpServers(List<McpServerEntry> entries) =>
        File.WriteAllText(_mcpFilePath, JsonSerializer.Serialize(entries, new JsonSerializerOptions { WriteIndented = true }));

    private void AddEntry()
    {
        var dialog = new Window { Title = "Добавить навык", Width = 500, Height = 430, WindowStartupLocation = WindowStartupLocation.CenterOwner, ResizeMode = ResizeMode.NoResize, Background = Brush("#111D2A"), Foreground = Brush("#EAF7FC") };
        var panel = new StackPanel { Margin = new Thickness(20) };
        panel.Children.Add(new TextBlock { Text = "НАЗВАНИЕ", Foreground = Brush("#16C8F5"), FontSize = 10 });
        var name = new TextBox { Margin = new Thickness(0, 5, 0, 12), Padding = new Thickness(8) };
        panel.Children.Add(name);
        panel.Children.Add(new TextBlock { Text = "ОПИСАНИЕ", Foreground = Brush("#16C8F5"), FontSize = 10 });
        var description = new TextBox { Margin = new Thickness(0, 5, 0, 12), Padding = new Thickness(8) };
        panel.Children.Add(description);
        panel.Children.Add(new TextBlock { Text = "ИНСТРУКЦИЯ / СИСТЕМНЫЙ ПРОМТ", Foreground = Brush("#16C8F5"), FontSize = 10 });
        var details = new TextBox { Margin = new Thickness(0, 5, 0, 12), Padding = new Thickness(8), Height = 145, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        panel.Children.Add(details);
        var save = PrimaryButton("Сохранить в каталог");
        save.HorizontalAlignment = HorizontalAlignment.Right;
        save.Click += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(name.Text)) { MessageBox.Show(dialog, "Укажи название."); return; }
            var entries = ReadEntries();
            entries.Add(new CatalogEntry { Name = name.Text.Trim(), Description = description.Text.Trim(), Details = details.Text.Trim(), EntryType = "skill" });
            WriteEntries(entries);
            dialog.DialogResult = true;
            dialog.Close();
        };
        panel.Children.Add(save);
        dialog.Content = panel;
        dialog.Owner = Window.GetWindow(this);
        dialog.ShowDialog();
        Refresh();
    }

    private void AddSkillFiles()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Выбери файлы для каталога SKILL",
            Filter = "Поддерживаемые файлы|*.txt;*.md;*.pdf;*.doc;*.docx;*.json;*.xml;*.cs;*.py;*.js;*.xaml;*.exe|Все файлы (*.*)|*.*",
            Multiselect = true, CheckFileExists = true
        };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;
        var entries = ReadEntries();
        foreach (var path in dialog.FileNames)
        {
            if (entries.Any(x => string.Equals(x.Details, path, StringComparison.OrdinalIgnoreCase))) continue;
            entries.Add(new CatalogEntry { Name = Path.GetFileName(path), Description = "Файл навыка · " + Path.GetExtension(path).TrimStart('.').ToUpperInvariant(), Details = path, EntryType = "skill-file" });
        }
        WriteEntries(entries);
        Refresh();
        MessageBox.Show(Window.GetWindow(this), "Файлы добавлены в каталог SKILL. ASTER сохранила их названия и пути. Файлы .exe не запускаются автоматически.", "ASTER — SKILL", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void AddDesktopApp()
    {
        var dialog = new OpenFileDialog { Title = "Выбери программу на компьютере", Filter = "Приложения (*.exe)|*.exe", CheckFileExists = true };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;
        var entries = ReadEntries();
        entries.Add(new CatalogEntry { Name = Path.GetFileNameWithoutExtension(dialog.FileName), Description = "Зарегистрирована программа. Внутренние функции пока не подключены.", Details = dialog.FileName, EntryType = "desktop-app" });
        WriteEntries(entries);
        RefreshPluginView();
        MessageBox.Show(Window.GetWindow(this), "Программа добавлена в список. Сохранён только путь к .exe; это ещё не подключение её функций к ИИ.", "ASTER — PLUGIN", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private static void DeleteMcpSecrets(McpServerEntry server)
    {
        const string prefix = "@ASTER_CREDENTIAL:";
        foreach (var line in (server.EnvironmentVariables ?? "").Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = line.IndexOf('=');
            if (separator <= 0) continue;
            var value = line[(separator + 1)..].Trim();
            if (value.StartsWith(prefix, StringComparison.Ordinal))
                McpSecretStore.Delete(value[prefix.Length..]);
        }
    }

    private void AddMcpServer(MarketplaceItem? template = null)
    {
        var existing = template is null ? null : ReadMcpServers().FirstOrDefault(x => string.Equals(x.Name, template.Name, StringComparison.OrdinalIgnoreCase));
        var dialog = new Window
        {
            Title = template is null ? "Добавить MCP-сервер" : "Настроить " + template.Name,
            Width = 580, Height = 650, WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.CanResize, Background = Brush("#111D2A"), Foreground = Brush("#EAF7FC")
        };
        var panel = new StackPanel { Margin = new Thickness(20) };
        TextBox Field(string label, string value, bool multi = false)
        {
            panel.Children.Add(new TextBlock { Text = label, Foreground = Brush("#16C8F5"), FontSize = 10, Margin = new Thickness(0, 8, 0, 4) });
            var box = new TextBox { Text = value, Padding = new Thickness(8), AcceptsReturn = multi, TextWrapping = TextWrapping.Wrap, Height = multi ? 76 : 32, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            panel.Children.Add(box);
            return box;
        }
        var name = Field("НАЗВАНИЕ", existing?.Name ?? template?.Name ?? "");
        panel.Children.Add(new TextBlock { Text = "СПОСОБ ПОДКЛЮЧЕНИЯ", Foreground = Brush("#16C8F5"), FontSize = 10, Margin = new Thickness(0, 8, 0, 4) });
        var transport = new ComboBox { Height = 34, Padding = new Thickness(6), Background = Brush("#0B1723"), Foreground = Brush("#EAF7FC") };
        transport.Items.Add(new ComboBoxItem { Content = "Локальный процесс · STDIO", Tag = "stdio" });
        transport.Items.Add(new ComboBoxItem { Content = "Удалённый сервер · Streamable HTTP", Tag = "streamable-http" });
        var selectedTransport = existing?.TransportType ?? "stdio";
        transport.SelectedIndex = selectedTransport.Equals("streamable-http", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
        panel.Children.Add(transport);
        var endpoint = Field("АДРЕС MCP-СЕРВЕРА (для HTTP)", existing?.Endpoint ?? "");
        var command = Field("КОМАНДА ЗАПУСКА (для STDIO)", existing?.Command ?? template?.Command ?? "npx");
        var args = Field("АРГУМЕНТЫ КОМАНДЫ (для STDIO)", existing?.Arguments ?? template?.Arguments ?? "", true);
        var savedEnvironmentName = (existing?.EnvironmentVariables ?? "")
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Split('=', 2)[0].Trim())
            .FirstOrDefault();
        var envName = Field("ИМЯ СЕКРЕТНОЙ ПЕРЕМЕННОЙ (необязательно)", savedEnvironmentName ?? "", false);
        var secret = new PasswordBox { Padding = new Thickness(8), Height = 32 };
        panel.Children.Add(new TextBlock { Text = "СЕКРЕТ / ТОКЕН (необязательно)", Foreground = Brush("#16C8F5"), FontSize = 10, Margin = new Thickness(0, 8, 0, 4) });
        panel.Children.Add(secret);
        var secretNote = new TextBlock
        {
            Text = "Новый секрет будет сохранён в Windows Credential Manager текущего пользователя. В конфигурации ASTER останется только ссылка на него. Для GitHub укажи имя переменной, которую ожидает выбранный MCP-сервер.",
            Foreground = Brush("#91A7B5"), TextWrapping = TextWrapping.Wrap, FontSize = 10, Margin = new Thickness(0, 6, 0, 4)
        };
        panel.Children.Add(secretNote);
        var note = new TextBlock
        {
            Text = "STDIO запускает локальную программу. Streamable HTTP подключается к URL MCP-сервера и поддерживает JSON/SSE-ответы. Секрет STDIO можно сохранить отдельно в Windows Credential Manager. OAuth для удалённых HTTP-серверов пока не реализован. Не вставляй секреты в адрес или аргументы.",
            Foreground = Brush("#E6C47A"), TextWrapping = TextWrapping.Wrap, FontSize = 11, Margin = new Thickness(0, 12, 0, 12)
        };
        panel.Children.Add(note);
        var save = PrimaryButton("Сохранить конфигурацию");
        save.HorizontalAlignment = HorizontalAlignment.Right;
        save.Click += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(name.Text))
            {
                MessageBox.Show(dialog, "Укажи название подключения.");
                return;
            }
            var kind = (transport.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "stdio";
            if (kind == "streamable-http" && string.IsNullOrWhiteSpace(endpoint.Text))
            {
                MessageBox.Show(dialog, "Укажи адрес удалённого MCP-сервера.");
                return;
            }
            if (kind == "stdio" && string.IsNullOrWhiteSpace(command.Text))
            {
                MessageBox.Show(dialog, "Укажи команду запуска локального MCP-сервера.");
                return;
            }
            var environmentVariables = existing?.EnvironmentVariables ?? "";
            var environmentLines = environmentVariables.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).ToList();
            const string credentialPrefix = "@ASTER_CREDENTIAL:";
            for (var index = 0; index < environmentLines.Count; index++)
            {
                var separator = environmentLines[index].IndexOf('=');
                if (separator <= 0) continue;
                var variable = environmentLines[index][..separator].Trim();
                var value = environmentLines[index][(separator + 1)..].Trim();
                if (string.IsNullOrEmpty(value) || value.StartsWith(credentialPrefix, StringComparison.Ordinal)) continue;
                var target = McpSecretStore.Save(name.Text.Trim(), variable, value);
                environmentLines[index] = variable + "=" + credentialPrefix + target;
            }
            environmentVariables = string.Join(Environment.NewLine, environmentLines);
            if (!string.IsNullOrEmpty(secret.Password))
            {
                try
                {
                    var target = McpSecretStore.Save(name.Text.Trim(), envName.Text.Trim(), secret.Password);
                    var lines = environmentVariables.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                        .Where(line => !line.StartsWith(envName.Text.Trim() + "=", StringComparison.OrdinalIgnoreCase))
                        .ToList();
                    lines.Add(envName.Text.Trim() + "=@ASTER_CREDENTIAL:" + target);
                    environmentVariables = string.Join(Environment.NewLine, lines);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(dialog, "Не удалось безопасно сохранить секрет.\n\n" + ex.Message, "ASTER · Credential Manager", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
            }
            var servers = ReadMcpServers();
            servers.RemoveAll(x => string.Equals(x.Name, name.Text.Trim(), StringComparison.OrdinalIgnoreCase));
            servers.Add(new McpServerEntry
            {
                Name = name.Text.Trim(),
                Command = command.Text.Trim(),
                Arguments = args.Text.Trim(),
                Endpoint = endpoint.Text.Trim(),
                TransportType = kind,
                EnvironmentVariables = environmentVariables,
                Enabled = true
            });
            WriteMcpServers(servers);
            dialog.DialogResult = true;
            dialog.Close();
        };
        panel.Children.Add(save);
        dialog.Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        dialog.Owner = Window.GetWindow(this);
        dialog.ShowDialog();
        _activePluginTab = "mcp";
        RefreshPluginView();
    }

    private void InstallMarketplaceItem(MarketplaceItem item)
    {
        var servers = ReadMcpServers();
        if (servers.Any(x => string.Equals(x.Name, item.Name, StringComparison.OrdinalIgnoreCase)))
        {
            MessageBox.Show(Window.GetWindow(this), "Этот шаблон уже добавлен в список MCP-серверов.", "ASTER Marketplace", MessageBoxButton.OK, MessageBoxImage.Information);
            _activePluginTab = "mcp";
            RefreshPluginView();
            return;
        }
        AddMcpServer(item);
    }

    private void RefreshPluginView()
    {
        _items.Children.Clear();
        var plugins = ReadEntries();
        var servers = ReadMcpServers();
        if (_activePluginTab == "marketplace")
        {
            _count.Text = _registryLoading ? "Загрузка…" : $"Интеграций: {_registryItems.Count}";
            AddSectionTitle("OFFICIAL MCP REGISTRY", "Каталог MCP-серверов");
            _items.Children.Add(InfoCard("Официальный каталог", "Список загружается напрямую из MCP Registry. Каталог показывает опубликованные серверы; перед запуском проверь издателя, репозиторий и необходимые разрешения."));
            var searchPanel = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
            var searchButton = PrimaryButton("Найти");
            searchButton.Margin = new Thickness(8, 0, 0, 0);
            searchButton.Click += async (_, _) => await LoadRegistryServersAsync(_registrySearchBox?.Text ?? "");
            DockPanel.SetDock(searchButton, Dock.Right);
            searchPanel.Children.Add(searchButton);
            _registrySearchBox = new TextBox { Text = _registrySearchBox?.Text ?? "", Height = 36, Padding = new Thickness(10, 7, 10, 7), FontSize = 13, Background = Brush("#0B1723"), Foreground = Brush("#EAF7FC"), BorderBrush = Brush("#29495D"), VerticalContentAlignment = VerticalAlignment.Center };
            _registrySearchBox.KeyDown += async (_, e) => { if (e.Key == System.Windows.Input.Key.Enter) await LoadRegistryServersAsync(_registrySearchBox.Text); };
            searchPanel.Children.Add(_registrySearchBox);
            _items.Children.Add(searchPanel);
            _registryStatus = new TextBlock { Text = _registryMessage, Foreground = Brush("#91A7B5"), FontSize = 11, Margin = new Thickness(2, 0, 0, 10) };
            _items.Children.Add(_registryStatus);
            foreach (var item in _registryItems) AddRegistryCard(item);
            if (!_registryLoading && _registryItems.Count == 0)
                _items.Children.Add(InfoCard("Каталог пока пуст", "Введи поисковый запрос длиной от трёх символов или обнови список."));
        }
        else if (_activePluginTab == "integrations")
        {
            _count.Text = "Каталог приложений";
            AddSectionTitle("APP INTEGRATIONS", "Подключение приложений");
            _items.Children.Add(InfoCard("Реальный статус подключений", "Карточки показывают состояние интеграции. Конфигурация MCP сама по себе не означает успешное подключение. Статус изменится только после проверки сервера и получения инструментов."));
            AddBuiltInIntegrationCard("GitHub", "Репозитории, issues, pull requests и поиск по коду.", "Официальный GitHub MCP Server. Для доступа потребуется авторизация GitHub и защищённое хранение токена. Хранение секретов ещё не реализовано — не вставляй токен в аргументы команды или обычный JSON.");
            AddBuiltInIntegrationCard("Unity", "Работа с открытым Unity Editor: сцены, объекты, компоненты и редакторские задачи.", "Требуется установить совместимый Unity Editor MCP-плагин в проект и запустить локальный MCP-мост. ASTER должна подключаться к этому мосту через STDIO или Streamable HTTP. Само наличие Unity на компьютере не подключает редактор.");
            AddBuiltInIntegrationCard("Blender", "Работа с открытой Blender-сценой, объектами, материалами и операциями.", "Требуется установить Blender MCP add-on и запустить локальный мост. ASTER подключается к работающему мосту; установка Blender сама по себе не предоставляет ИИ доступ к сцене.");
            AddBuiltInIntegrationCard("Visual Studio Code", "Работа с файлами и проектами разработки.", "Для полноценного управления редактором понадобится отдельный проверенный MCP-сервер или расширение. Открытие VS Code как программы не предоставляет ИИ доступ к его инструментам.");
            AddBuiltInIntegrationCard("Google Drive", "Поиск и работа с файлами облачного хранилища.", "Потребуется OAuth-авторизация Google и отдельный коннектор с ограниченными разрешениями. Не вводи пароль Google в конфигурацию MCP.");
            AddBuiltInIntegrationCard("Notion", "Поиск и управление страницами и базами данных.", "Потребуется авторизация Notion и отдельный коннектор с явно выданными разрешениями.");
        }
        else if (_activePluginTab == "installed")
        {
            _count.Text = $"Плагинов: {plugins.Count}";
            AddSectionTitle("MY PLUGINS", "Добавленные плагины");
            if (plugins.Count == 0)
                _items.Children.Add(InfoCard("Здесь пока пусто", "Добавляй интеграции из Marketplace или регистрируй локальные программы через кнопку «+»."));
            foreach (var entry in plugins) AddCatalogCard(entry);
        }
        else
        {
            _count.Text = $"Серверов: {servers.Count}";
            AddSectionTitle("MODEL CONTEXT PROTOCOL", "Конфигурации MCP-серверов");
            _items.Children.Add(InfoCard("Статус подключения", "Сейчас ASTER сохраняет настройки серверов. Статус «Подключён» появится только после реализации и успешной проверки реального соединения."));
            var add = SecondaryButton("＋  Добавить MCP-сервер");
            add.HorizontalAlignment = HorizontalAlignment.Left;
            add.Margin = new Thickness(0, 0, 0, 12);
            add.Click += (_, _) => AddMcpServer();
            _items.Children.Add(add);
            foreach (var server in servers) AddMcpCard(server);
        }
    }

    private void AddBuiltInIntegrationCard(string name, string description, string requirements)
    {
        var configured = ReadMcpServers().Any(x => x.Enabled &&
            (x.Name.Contains(name, StringComparison.OrdinalIgnoreCase) ||
             (name == "GitHub" && x.Name.Contains("github", StringComparison.OrdinalIgnoreCase)) ||
             (name == "Unity" && x.Name.Contains("unity", StringComparison.OrdinalIgnoreCase)) ||
             (name == "Blender" && x.Name.Contains("blender", StringComparison.OrdinalIgnoreCase))));
        var body = new StackPanel();
        var header = new DockPanel();
        var title = new TextBlock { Text = name, Foreground = Brush("#EAF7FC"), FontSize = 17, FontWeight = FontWeights.SemiBold };
        DockPanel.SetDock(title, Dock.Left);
        header.Children.Add(title);
        var status = new TextBlock
        {
            Text = configured ? "КОНФИГУРАЦИЯ ЕСТЬ · НЕ ПРОВЕРЕНА" : "НЕ ПОДКЛЮЧЕНО",
            Foreground = Brush("#E6C47A"), FontSize = 9, VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        DockPanel.SetDock(status, Dock.Right);
        header.Children.Add(status);
        body.Children.Add(header);
        body.Children.Add(new TextBlock { Text = description, Foreground = Brush("#91A7B5"), FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) });
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0) };
        var details = SecondaryButton("Требования");
        details.Click += (_, _) => MessageBox.Show(Window.GetWindow(this), requirements, name + " · требования к подключению", MessageBoxButton.OK, MessageBoxImage.Information);
        buttons.Children.Add(details);
        var configure = PrimaryButton(configured ? "Открыть MCP-серверы" : "Добавить MCP-сервер");
        configure.Margin = new Thickness(8, 0, 0, 0);
        configure.Click += (_, _) =>
        {
            if (configured)
            {
                _activePluginTab = "mcp";
                RefreshPluginView();
            }
            else
            {
                AddMcpServer(new MarketplaceItem(name, "APP INTEGRATION", description, "", "", name));
            }
        };
        buttons.Children.Add(configure);
        body.Children.Add(buttons);
        _items.Children.Add(new Border
        {
            Background = Brush("#111D2A"),
            BorderBrush = string.Equals(_selectedIntegration, name, StringComparison.OrdinalIgnoreCase) ? Brush("#16C8F5") : Brush("#294052"),
            BorderThickness = new Thickness(string.Equals(_selectedIntegration, name, StringComparison.OrdinalIgnoreCase) ? 2 : 1),
            CornerRadius = new CornerRadius(9), Padding = new Thickness(14),
            Margin = new Thickness(0, 0, 0, 10), Child = body
        });
    }

    private void AddSectionTitle(string eyebrow, string title)
    {
        _items.Children.Add(new TextBlock { Text = eyebrow, Foreground = Brush("#16C8F5"), FontSize = 10, FontWeight = FontWeights.SemiBold, Margin = new Thickness(2, 4, 0, 3) });
        _items.Children.Add(new TextBlock { Text = title, Foreground = Brush("#EAF7FC"), FontSize = 19, FontWeight = FontWeights.SemiBold, Margin = new Thickness(2, 0, 0, 12) });
    }

    private Border InfoCard(string title, string description)
    {
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock { Text = title, Foreground = Brush("#A8F1FF"), FontSize = 13, FontWeight = FontWeights.SemiBold });
        panel.Children.Add(new TextBlock { Text = description, Foreground = Brush("#91A7B5"), FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 5, 0, 0) });
        return new Border { Background = Brush("#102235"), BorderBrush = Brush("#24516A"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(9), Padding = new Thickness(14), Margin = new Thickness(0, 0, 0, 12), Child = panel };
    }

    private async Task LoadRegistryServersAsync(string search)
    {
        search = (search ?? "").Trim();
        if (!string.IsNullOrEmpty(search) && search.Length < 3)
        {
            _registryMessage = "Для поиска в официальном реестре нужно минимум 3 символа.";
            _registryStatus?.SetCurrentValue(TextBlock.TextProperty, _registryMessage);
            return;
        }
        if (_registryLoading) return;
        _registryLoading = true;
        _registryMessage = "Подключаемся к официальному MCP Registry…";
        _registryItems.Clear();
        if (_activePluginTab == "marketplace") RefreshPluginView();
        try
        {
            var query = "?version=latest&limit=40";
            if (!string.IsNullOrEmpty(search)) query += "&search=" + Uri.EscapeDataString(search);
            using var response = await RegistryHttp.GetAsync(RegistryBaseUrl + query);
            response.EnsureSuccessStatusCode();
            var json = JsonNode.Parse(await response.Content.ReadAsStringAsync());
            var servers = json?["servers"] as JsonArray;
            if (servers is not null)
            {
                foreach (var record in servers)
                {
                    var server = record?["server"] ?? record;
                    if (server is null) continue;
                    var meta = record?["_meta"]?["io.modelcontextprotocol.registry/official"];
                    var status = meta?["status"]?.GetValue<string>() ?? "unknown";
                    if (!string.Equals(status, "active", StringComparison.OrdinalIgnoreCase) && status != "unknown") continue;
                    var name = server["name"]?.GetValue<string>() ?? "MCP server";
                    var title = server["title"]?.GetValue<string>() ?? name;
                    var description = server["description"]?.GetValue<string>() ?? "Описание не предоставлено.";
                    var version = server["version"]?.GetValue<string>() ?? "—";
                    var repository = server["repository"]?["url"]?.GetValue<string>() ?? "";
                    var website = server["websiteUrl"]?.GetValue<string>() ?? "";
                    var packages = server["packages"] as JsonArray;
                    var package = packages?.FirstOrDefault();
                    var registryType = package?["registryType"]?.GetValue<string>() ?? "";
                    var identifier = package?["identifier"]?.GetValue<string>() ?? "";
                    var runtime = package?["runtimeHint"]?.GetValue<string>() ?? "";
                    var transport = package?["transport"]?["type"]?.GetValue<string>() ?? "";
                    var requiredEnv = package?["environmentVariables"] is JsonArray env && env.Any(x => x?["isRequired"]?.GetValue<bool>() == true);
                    _registryItems.Add(new RegistryServer(name, title, description, version, status, registryType, identifier, runtime, transport, repository, website, requiredEnv));
                }
            }
            _registryMessage = $"Получено серверов: {_registryItems.Count}. Источник: registry.modelcontextprotocol.io";
            _registryStatus?.SetCurrentValue(TextBlock.TextProperty, _registryMessage);
        }
        catch (Exception ex)
        {
            _registryMessage = "Не удалось загрузить каталог: " + ex.Message;
            _registryStatus?.SetCurrentValue(TextBlock.TextProperty, _registryMessage);
        }
        finally
        {
            _registryLoading = false;
            if (_activePluginTab == "marketplace") RefreshPluginView();
        }
    }

    private void AddRegistryCard(RegistryServer item)
    {
        var body = new StackPanel();
        body.Children.Add(new TextBlock { Text = item.RegistryType.ToUpperInvariant() + " · " + item.Status.ToUpperInvariant(), Foreground = Brush("#16C8F5"), FontSize = 9, FontWeight = FontWeights.SemiBold });
        body.Children.Add(new TextBlock { Text = item.Title, Foreground = Brush("#EAF7FC"), FontSize = 16, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 4, 0, 3) });
        body.Children.Add(new TextBlock { Text = item.Name + " · v" + item.Version, Foreground = Brush("#A8C7D5"), FontSize = 10 });
        body.Children.Add(new TextBlock { Text = item.Description, Foreground = Brush("#91A7B5"), FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) });
        if (!string.IsNullOrWhiteSpace(item.RepositoryUrl))
            body.Children.Add(new TextBlock { Text = "Репозиторий: " + item.RepositoryUrl, Foreground = Brush("#A8C7D5"), FontSize = 10, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) });
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0) };
        var details = SecondaryButton("Подробнее");
        details.Click += (_, _) => MessageBox.Show(Window.GetWindow(this),
            $"{item.Title}\\n\\nИмя: {item.Name}\\nВерсия: {item.Version}\\nСтатус: {item.Status}\\nТип пакета: {item.RegistryType}\\nПакет: {item.Identifier}\\nТранспорт: {item.Transport}\\nRuntime: {item.RuntimeHint}\\nОбязательные переменные: {(item.HasRequiredEnvironment ? "да" : "нет")}\\nРепозиторий: {item.RepositoryUrl}\\nСайт: {item.WebsiteUrl}\\n\\nПеред запуском проверь источник и разрешения сервера.",
            "MCP Registry · сведения о сервере", MessageBoxButton.OK, MessageBoxImage.Information);
        buttons.Children.Add(details);
        if (string.Equals(item.RegistryType, "npm", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(item.Transport, "stdio", StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(item.Identifier) && !item.HasRequiredEnvironment)
        {
            var add = PrimaryButton("Добавить MCP");
            add.Margin = new Thickness(8, 0, 0, 0);
            add.Click += (_, _) =>
            {
                var runtime = string.IsNullOrWhiteSpace(item.RuntimeHint) ? "npx" : item.RuntimeHint;
                if (!string.Equals(runtime, "npx", StringComparison.OrdinalIgnoreCase))
                {
                    MessageBox.Show(Window.GetWindow(this), "Автоматическое создание конфигурации пока поддерживает npm-пакеты с runtime npx. Открой сведения сервера и настрой остальные варианты вручную.", "ASTER MCP", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }
                var package = item.Identifier + (string.IsNullOrWhiteSpace(item.Version) || item.Version == "—" ? "" : "@" + item.Version);
                AddMcpServer(new MarketplaceItem(item.Title, "MCP REGISTRY", item.Description, "npx", "-y " + package, item.Name));
            };
            buttons.Children.Add(add);
        }
        body.Children.Add(buttons);
        _items.Children.Add(new Border { Background = Brush("#111D2A"), BorderBrush = Brush("#294052"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(9), Padding = new Thickness(14), Margin = new Thickness(0, 0, 0, 10), Child = body });
    }

    private void AddMarketplaceCard(MarketplaceItem item)
    {
        var body = new StackPanel();
        body.Children.Add(new TextBlock { Text = item.Category, Foreground = Brush("#16C8F5"), FontSize = 9, FontWeight = FontWeights.SemiBold });
        body.Children.Add(new TextBlock { Text = item.Name, Foreground = Brush("#EAF7FC"), FontSize = 16, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 4, 0, 3) });
        body.Children.Add(new TextBlock { Text = item.Description, Foreground = Brush("#91A7B5"), FontSize = 11, TextWrapping = TextWrapping.Wrap });
        body.Children.Add(new TextBlock { Text = $"Команда: {item.Command} {item.Arguments}", Foreground = Brush("#A8C7D5"), FontSize = 10, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) });
        var button = PrimaryButton("Настроить");
        button.Margin = new Thickness(0, 10, 0, 0);
        button.HorizontalAlignment = HorizontalAlignment.Right;
        button.Click += (_, _) => InstallMarketplaceItem(item);
        body.Children.Add(button);
        _items.Children.Add(new Border { Background = Brush("#111D2A"), BorderBrush = Brush("#294052"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(9), Padding = new Thickness(14), Margin = new Thickness(0, 0, 0, 10), Child = body });
    }

    private void AddCatalogCard(CatalogEntry entry)
    {
        var body = new StackPanel();
        body.Children.Add(new TextBlock { Text = entry.Name, Foreground = Brush("#EAF7FC"), FontSize = 15, FontWeight = FontWeights.SemiBold });
        body.Children.Add(new TextBlock { Text = entry.Description, Foreground = Brush("#91A7B5"), FontSize = 11, Margin = new Thickness(0, 4, 0, 0), TextWrapping = TextWrapping.Wrap });
        if (!string.IsNullOrWhiteSpace(entry.Details))
            body.Children.Add(new TextBlock { Text = entry.Details, Foreground = Brush("#A8C7D5"), FontSize = 10, Margin = new Thickness(0, 7, 0, 0), TextWrapping = TextWrapping.Wrap, MaxHeight = 48 });
        var remove = SecondaryButton("Удалить");
        remove.HorizontalAlignment = HorizontalAlignment.Right;
        remove.Margin = new Thickness(0, 8, 0, 0);
        remove.Click += (_, _) => { var current = ReadEntries(); current.RemoveAll(x => x.Name == entry.Name && x.Details == entry.Details); WriteEntries(current); RefreshPluginView(); };
        body.Children.Add(remove);
        _items.Children.Add(new Border { Background = Brush("#111D2A"), BorderBrush = Brush("#294052"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(9), Padding = new Thickness(14), Margin = new Thickness(0, 0, 0, 10), Child = body });
    }

    private void AddMcpCard(McpServerEntry server)
    {
        var body = new StackPanel();
        body.Children.Add(new TextBlock { Text = server.Name, Foreground = Brush("#EAF7FC"), FontSize = 15, FontWeight = FontWeights.SemiBold });
        body.Children.Add(new TextBlock { Text = server.Enabled ? "КОНФИГУРАЦИЯ СОХРАНЕНА · НЕ ПОДКЛЮЧЁН" : "ОТКЛЮЧЁН", Foreground = Brush("#E6C47A"), FontSize = 9, Margin = new Thickness(0, 4, 0, 0) });
        var transportLabel = string.Equals(server.TransportType, "streamable-http", StringComparison.OrdinalIgnoreCase)
            ? "STREAMABLE HTTP · " + server.Endpoint
            : "STDIO · " + server.Command + " " + server.Arguments;
        body.Children.Add(new TextBlock { Text = transportLabel, Foreground = Brush("#91A7B5"), FontSize = 10, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 5, 0, 0) });
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 8, 0, 0) };
        var test = SecondaryButton("Проверить");
        test.Click += async (_, _) =>
        {
            test.IsEnabled = false;
            test.Content = "Проверка…";
            try
            {
                var tools = await McpRuntimeService.ListToolsAsync(server);
                var names = tools.Count == 0 ? "Сервер ответил, но не предоставил инструменты." :
                    string.Join("\n", tools.Select(x => "• " + (x["name"]?.GetValue<string>() ?? "Без названия")));
                MessageBox.Show(Window.GetWindow(this), $"MCP-сервер «{server.Name}» ответил.\n\nИнструменты:\n{names}", "Проверка MCP", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(Window.GetWindow(this), "Не удалось подключиться к MCP-серверу.\n\n" + ex.Message, "Ошибка MCP", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            finally
            {
                test.IsEnabled = true;
                test.Content = "Проверить";
            }
        };
        var edit = SecondaryButton("Изменить");
        edit.Click += (_, _) => AddMcpServer(new MarketplaceItem(server.Name, "MCP", "Редактирование конфигурации", server.Command, server.Arguments, server.Name));
        var remove = SecondaryButton("Удалить");
        remove.Margin = new Thickness(7, 0, 0, 0);
        remove.Click += (_, _) =>
        {
            var list = ReadMcpServers();
            var removed = list.Where(x => x.Name == server.Name).ToList();
            foreach (var item in removed) DeleteMcpSecrets(item);
            list.RemoveAll(x => x.Name == server.Name);
            WriteMcpServers(list);
            RefreshPluginView();
        };
        buttons.Children.Add(test);
        test.Margin = new Thickness(0, 0, 7, 0);
        buttons.Children.Add(edit);
        buttons.Children.Add(remove);
        body.Children.Add(buttons);
        _items.Children.Add(new Border { Background = Brush("#111D2A"), BorderBrush = Brush("#294052"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(9), Padding = new Thickness(14), Margin = new Thickness(0, 0, 0, 10), Child = body });
    }

    private void Refresh()
    {
        if (_kind == CatalogKind.Plugin) { RefreshPluginView(); return; }
        var entries = ReadEntries();
        _items.Children.Clear();
        _count.Text = "Элементов: " + entries.Count;
        if (entries.Count == 0)
            _items.Children.Add(new TextBlock { Text = "Пока нет навыков. Нажми «Добавить файлы» или +.", Foreground = Brush("#91A7B5"), FontSize = 13, Margin = new Thickness(8, 18, 0, 0) });
        foreach (var entry in entries) AddCatalogCard(entry);
    }
}
