using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using ASTER.Workstation.Core.Services;
using Microsoft.Win32;

namespace ASTER.Workstation.App;

public sealed class DesktopApplicationEntry
{
    public string Name { get; set; } = "";
    public string ExecutablePath { get; set; } = "";
    public string McpServerName { get; set; } = "";
    public DateTime AddedUtc { get; set; } = DateTime.UtcNow;
}

public sealed class MyApplicationsControl : UserControl
{
    private readonly string _folder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ASTER", "Applications");
    private readonly string _appsFile;
    private readonly string _mcpFile;
    private readonly StackPanel _items = new();
    private readonly TextBlock _count = new();

    public MyApplicationsControl()
    {
        Directory.CreateDirectory(_folder);
        _appsFile = Path.Combine(_folder, "my-applications.json");
        _mcpFile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ASTER", "Catalogs", "mcp-servers.json");
        BuildView();
        Refresh();
    }

    private static System.Windows.Media.Brush Brush(string value) =>
        (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFromString(value)!;

    private void BuildView()
    {
        var root = new Grid { Margin = new Thickness(24) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var heading = new StackPanel { Margin = new Thickness(0, 0, 0, 18) };
        heading.Children.Add(new TextBlock { Text = "ASTER · LOCAL WORKSPACE", Foreground = Brush("#16C8F5"), FontSize = 10, FontWeight = FontWeights.SemiBold });
        heading.Children.Add(new TextBlock { Text = "Мои приложения", Foreground = Brush("#EAF7FC"), FontSize = 26, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 5, 0, 4) });
        heading.Children.Add(new TextBlock { Text = "Добавляй программы с компьютера и подключай к ним инструменты для ИИ.", Foreground = Brush("#91A7B5"), FontSize = 12 });
        Grid.SetRow(heading, 0);
        root.Children.Add(heading);

        var toolbar = new DockPanel { Margin = new Thickness(0, 0, 0, 14) };
        var add = new Button { Content = "＋  Добавить приложение с компьютера", Padding = new Thickness(15, 9, 15, 9),
            Background = Brush("#16C8F5"), Foreground = Brush("#06131C"), BorderBrush = Brush("#16C8F5"), FontWeight = FontWeights.SemiBold };
        add.Click += (_, _) => AddApplication();
        toolbar.Children.Add(add);
        _count.VerticalAlignment = VerticalAlignment.Center;
        _count.Foreground = Brush("#91A7B5");
        _count.Margin = new Thickness(14, 0, 0, 0);
        toolbar.Children.Add(_count);
        Grid.SetRow(toolbar, 1);
        root.Children.Add(toolbar);

        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        _items.Margin = new Thickness(0, 2, 8, 8);
        scroll.Content = _items;
        Grid.SetRow(scroll, 2);
        root.Children.Add(scroll);
        Content = root;
    }

    private List<DesktopApplicationEntry> ReadApps()
    {
        try { return File.Exists(_appsFile) ? JsonSerializer.Deserialize<List<DesktopApplicationEntry>>(File.ReadAllText(_appsFile)) ?? new() : new(); }
        catch { return new(); }
    }

    private void SaveApps(List<DesktopApplicationEntry> apps) =>
        File.WriteAllText(_appsFile, JsonSerializer.Serialize(apps, new JsonSerializerOptions { WriteIndented = true }));

    private List<string> ReadMcpServers()
    {
        try
        {
            if (!File.Exists(_mcpFile)) return new();
            using var doc = JsonDocument.Parse(File.ReadAllText(_mcpFile));
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return new();
            return doc.RootElement.EnumerateArray()
                .Where(x => x.TryGetProperty("Enabled", out var enabled) && enabled.ValueKind == JsonValueKind.True)
                .Select(x => x.TryGetProperty("Name", out var name) ? name.GetString() ?? "" : "")
                .Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }
        catch { return new(); }
    }

    private bool ShowOwnedDialog(Window dialog)
    {
        dialog.Owner = Window.GetWindow(this);
        return dialog.ShowDialog() == true;
    }

    private void AddApplication()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Выбери программу для добавления в ASTER",
            Filter = "Программы Windows (*.exe)|*.exe",
            Multiselect = false
        };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;

        var path = Path.GetFullPath(dialog.FileName);
        var apps = ReadApps();
        if (apps.Any(x => string.Equals(x.ExecutablePath, path, StringComparison.OrdinalIgnoreCase)))
        {
            MessageBox.Show(Window.GetWindow(this), "Эта программа уже добавлена.", "ASTER", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var entry = new DesktopApplicationEntry { Name = Path.GetFileNameWithoutExtension(path), ExecutablePath = path };
        var servers = ReadMcpServers();
        if (servers.Count > 0)
        {
            var dialogWindow = new Window { Title = "Подключение инструментов ИИ", Width = 480, Height = 230,
                WindowStartupLocation = WindowStartupLocation.CenterOwner, ResizeMode = ResizeMode.NoResize,
                Background = Brush("#0B1623"), Foreground = Brush("#EAF7FC") };
            var panel = new StackPanel { Margin = new Thickness(20) };
            panel.Children.Add(new TextBlock { Text = "Выбери MCP-сервер, который предоставляет инструменты для этой программы.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0,0,0,12) });
            var combo = new ComboBox { ItemsSource = servers, SelectedIndex = 0, Padding = new Thickness(8), Margin = new Thickness(0,0,0,14) };
            panel.Children.Add(combo);
            panel.Children.Add(new TextBlock { Text = "Если сервер ещё не настроен, оставь поле пустым и добавь его позже в PLUGIN → MCP-серверы.", Foreground = Brush("#91A7B5"), TextWrapping = TextWrapping.Wrap, FontSize = 11, Margin = new Thickness(0,0,0,14) });
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            var skip = new Button { Content = "Без подключения", Padding = new Thickness(10,7,10,7), Margin = new Thickness(0,0,8,0) };
            var connect = new Button { Content = "Сохранить", Padding = new Thickness(12,7,12,7), Background = Brush("#16C8F5"), Foreground = Brush("#06131C") };
            skip.Click += (_, _) => { entry.McpServerName = ""; dialogWindow.DialogResult = true; };
            connect.Click += (_, _) => { entry.McpServerName = combo.SelectedItem as string ?? ""; dialogWindow.DialogResult = true; };
            buttons.Children.Add(skip); buttons.Children.Add(connect); panel.Children.Add(buttons);
            dialogWindow.Content = panel;
            if (ShowOwnedDialog(dialogWindow) != true) return;
        }

        apps.Add(entry);
        SaveApps(apps);
        WorkspaceActivityLog.Write("APPLICATIONS", $"Добавлена программа: {entry.Name}.");
        Refresh();
    }

    private void Refresh()
    {
        var apps = ReadApps();
        _items.Children.Clear();
        _count.Text = $"{apps.Count} приложений";
        if (apps.Count == 0)
        {
            _items.Children.Add(new TextBlock { Text = "Здесь пока пусто. Нажми «Добавить приложение с компьютера», чтобы выбрать .exe-файл.",
                Foreground = Brush("#91A7B5"), FontSize = 13, Margin = new Thickness(8, 18, 0, 0) });
            return;
        }

        foreach (var app in apps)
        {
            var card = new Border { Background = Brush("#101F2D"), BorderBrush = Brush("#24465D"),
                BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12), Padding = new Thickness(15), Margin = new Thickness(0,0,0,10) };
            var layout = new Grid();
            layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            layout.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var name = new TextBlock { Text = app.Name, FontSize = 15, FontWeight = FontWeights.SemiBold, Foreground = Brush("#EAF7FC") };
            layout.Children.Add(name);
            var path = new TextBlock { Text = app.ExecutablePath, FontSize = 10, Foreground = Brush("#91A7B5"), TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0,5,10,0) };
            Grid.SetRow(path, 1); layout.Children.Add(path);
            var connection = new TextBlock {
                Text = string.IsNullOrWhiteSpace(app.McpServerName)
                    ? "Запуск доступен · инструменты ИИ ещё не подключены"
                    : $"MCP-инструменты: {app.McpServerName}",
                FontSize = 11, Foreground = string.IsNullOrWhiteSpace(app.McpServerName) ? Brush("#D6B36A") : Brush("#83D7B0"),
                Margin = new Thickness(0,8,0,0), TextWrapping = TextWrapping.Wrap };
            Grid.SetRow(connection, 2); layout.Children.Add(connection);

            var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            var launch = new Button { Content = "Запустить", Padding = new Thickness(11,7), Margin = new Thickness(6,0,0,0) };
            launch.Click += (_, _) => Launch(app);
            actions.Children.Add(launch);
            var link = new Button { Content = "Подключить MCP", Padding = new Thickness(11,7), Margin = new Thickness(6,0,0,0) };
            link.Click += (_, _) => LinkMcp(app);
            actions.Children.Add(link);
            var remove = new Button { Content = "Убрать", Padding = new Thickness(11,7), Margin = new Thickness(6,0,0,0) };
            remove.Click += (_, _) => Remove(app);
            actions.Children.Add(remove);
            Grid.SetColumn(actions, 1); Grid.SetRowSpan(actions, 3); layout.Children.Add(actions);
            card.Child = layout;
            _items.Children.Add(card);
        }
    }

    private void Launch(DesktopApplicationEntry app)
    {
        if (!File.Exists(app.ExecutablePath))
        {
            MessageBox.Show(Window.GetWindow(this), "Файл программы не найден. Возможно, она была перемещена.", "ASTER", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        try
        {
            Process.Start(new ProcessStartInfo { FileName = app.ExecutablePath, UseShellExecute = true });
            WorkspaceActivityLog.Write("APPLICATIONS", $"Запущена программа: {app.Name}.");
        }
        catch (Exception ex)
        {
            MessageBox.Show(Window.GetWindow(this), "Не удалось запустить программу.\n\n" + ex.Message, "ASTER", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void LinkMcp(DesktopApplicationEntry app)
    {
        var servers = ReadMcpServers();
        if (servers.Count == 0)
        {
            MessageBox.Show(Window.GetWindow(this), "Сначала добавь и включи MCP-сервер в PLUGIN → MCP-серверы. Сам .exe-файл не предоставляет ИИ команды для управления программой.", "ASTER · подключение ИИ", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var dialog = new Window { Title = "Подключить инструменты ИИ", Width = 440, Height = 180,
            WindowStartupLocation = WindowStartupLocation.CenterOwner, ResizeMode = ResizeMode.NoResize,
            Background = Brush("#0B1623"), Foreground = Brush("#EAF7FC") };
        var panel = new StackPanel { Margin = new Thickness(18) };
        panel.Children.Add(new TextBlock { Text = $"Выбери MCP-сервер с инструментами для «{app.Name}».", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0,0,0,12) });
        var combo = new ComboBox { ItemsSource = servers, SelectedItem = servers.Contains(app.McpServerName) ? app.McpServerName : servers[0], Padding = new Thickness(8), Margin = new Thickness(0,0,0,12) };
        panel.Children.Add(combo);
        var save = new Button { Content = "Подключить", HorizontalAlignment = HorizontalAlignment.Right, Padding = new Thickness(12,7,12,7), Background = Brush("#16C8F5"), Foreground = Brush("#06131C") };
        save.Click += (_, _) => { app.McpServerName = combo.SelectedItem as string ?? ""; var apps = ReadApps(); var current = apps.FirstOrDefault(x => string.Equals(x.ExecutablePath, app.ExecutablePath, StringComparison.OrdinalIgnoreCase)); if (current is not null) current.McpServerName = app.McpServerName; SaveApps(apps); dialog.DialogResult = true; };
        panel.Children.Add(save); dialog.Content = panel;
        if (ShowOwnedDialog(dialog) == true) Refresh();
    }

    private void Remove(DesktopApplicationEntry app)
    {
        var answer = MessageBox.Show(Window.GetWindow(this), $"Убрать «{app.Name}» из списка ASTER? Файл программы не будет удалён.",
            "Подтверждение", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (answer != MessageBoxResult.Yes) return;
        var apps = ReadApps();
        apps.RemoveAll(x => string.Equals(x.ExecutablePath, app.ExecutablePath, StringComparison.OrdinalIgnoreCase));
        SaveApps(apps);
        Refresh();
    }
}
