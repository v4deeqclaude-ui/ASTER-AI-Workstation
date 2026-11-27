using System.IO;
using System.Text.Json;
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

public sealed class CatalogWorkspaceControl : UserControl
{
    private readonly CatalogKind _kind;
    private readonly StackPanel _items = new();
    private readonly TextBlock _count = new();
    private readonly string _filePath;

    public CatalogWorkspaceControl(CatalogKind kind)
    {
        _kind = kind;
        var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ASTER", "Catalogs");
        Directory.CreateDirectory(folder);
        _filePath = Path.Combine(folder, kind == CatalogKind.Skill ? "skills.json" : "plugins.json");

        var root = new Grid { Margin = new Thickness(24) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        var heading = new StackPanel { Margin = new Thickness(0, 0, 0, 18) };
        heading.Children.Add(new TextBlock { Text = kind == CatalogKind.Skill ? "SKILL LIBRARY" : "PLUGIN CENTER", Foreground = Brush("#16C8F5"), FontSize = 10, FontWeight = FontWeights.SemiBold });
        heading.Children.Add(new TextBlock { Text = kind == CatalogKind.Skill ? "Навыки ASTER" : "Подключения и плагины", Foreground = Brush("#EAF7FC"), FontSize = 26, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 5, 0, 4) });
        heading.Children.Add(new TextBlock { Text = kind == CatalogKind.Skill ? "Каталог инструкций и навыков. Здесь нет чата." : "Подключай внешние сервисы, API, MCP-серверы и локальные программы. Здесь нет чата.", Foreground = Brush("#91A7B5"), FontSize = 12 });
        Grid.SetRow(heading, 0);
        root.Children.Add(heading);

        var actions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 14) };
        var add = new Button { Content = kind == CatalogKind.Skill ? "＋  Добавить навык" : "＋  Добавить плагин", Padding = new Thickness(16, 9, 16, 9), Background = Brush("#16C8F5"), Foreground = Brush("#06131C"), BorderBrush = Brush("#16C8F5"), FontWeight = FontWeights.SemiBold };
        add.Click += (_, _) => AddEntry();
        actions.Children.Add(add);
        if (kind == CatalogKind.Plugin)
        {
            var program = new Button { Content = "＋  Добавить программу", Padding = new Thickness(14, 9, 14, 9), Margin = new Thickness(9, 0, 0, 0), Background = Brush("#17364A"), BorderBrush = Brush("#16C8F5"), Foreground = Brush("#A8F1FF") };
            program.Click += (_, _) => AddProgram();
            actions.Children.Add(program);
        }
        _count.VerticalAlignment = VerticalAlignment.Center;
        _count.Foreground = Brush("#91A7B5");
        _count.Margin = new Thickness(14, 0, 0, 0);
        actions.Children.Add(_count);
        Grid.SetRow(actions, 1);
        root.Children.Add(actions);

        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        _items.Margin = new Thickness(0, 2, 8, 8);
        scroll.Content = _items;
        Grid.SetRow(scroll, 2);
        root.Children.Add(scroll);
        Content = root;
        Refresh();
    }

    private static System.Windows.Media.Brush Brush(string value) => (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFromString(value)!;

    private List<CatalogEntry> ReadEntries()
    {
        try { return File.Exists(_filePath) ? JsonSerializer.Deserialize<List<CatalogEntry>>(File.ReadAllText(_filePath)) ?? new() : new(); }
        catch { return new(); }
    }

    private void WriteEntries(List<CatalogEntry> entries) => File.WriteAllText(_filePath, JsonSerializer.Serialize(entries, new JsonSerializerOptions { WriteIndented = true }));

    private void AddEntry()
    {
        var isSkill = _kind == CatalogKind.Skill;
        var dialog = new Window { Title = isSkill ? "Добавить навык" : "Добавить плагин", Width = 500, Height = 430, WindowStartupLocation = WindowStartupLocation.CenterOwner, ResizeMode = ResizeMode.NoResize, Background = Brush("#111D2A"), Foreground = Brush("#EAF7FC") };
        var panel = new StackPanel { Margin = new Thickness(20) };
        panel.Children.Add(new TextBlock { Text = "НАЗВАНИЕ", Foreground = Brush("#16C8F5"), FontSize = 10 });
        var name = new TextBox { Margin = new Thickness(0, 5, 0, 12), Padding = new Thickness(8) };
        panel.Children.Add(name);
        panel.Children.Add(new TextBlock { Text = "ОПИСАНИЕ", Foreground = Brush("#16C8F5"), FontSize = 10 });
        var description = new TextBox { Margin = new Thickness(0, 5, 0, 12), Padding = new Thickness(8) };
        panel.Children.Add(description);
        panel.Children.Add(new TextBlock { Text = isSkill ? "ИНСТРУКЦИЯ / СИСТЕМНЫЙ ПРОМТ" : "АДРЕС СЕРВЕРА / API / MCP ИЛИ ДЕТАЛИ ПОДКЛЮЧЕНИЯ", Foreground = Brush("#16C8F5"), FontSize = 10 });
        var details = new TextBox { Margin = new Thickness(0, 5, 0, 12), Padding = new Thickness(8), Height = 145, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        panel.Children.Add(details);
        var save = new Button { Content = "Сохранить в каталог", Padding = new Thickness(14, 8, 14, 8), HorizontalAlignment = HorizontalAlignment.Right, Background = Brush("#16C8F5"), Foreground = Brush("#06131C") };
        save.Click += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(name.Text)) { MessageBox.Show(dialog, "Укажи название."); return; }
            var entries = ReadEntries();
            entries.Add(new CatalogEntry { Name = name.Text.Trim(), Description = description.Text.Trim(), Details = details.Text.Trim(), EntryType = isSkill ? "skill" : "plugin" });
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

    private void AddProgram()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Выбери установленную программу",
            Filter = "Программы Windows (*.exe)|*.exe",
            CheckFileExists = true,
            Multiselect = false
        };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;

        var entries = ReadEntries();
        var existing = entries.FirstOrDefault(x => x.EntryType == "program" &&
            string.Equals(x.Details, dialog.FileName, StringComparison.OrdinalIgnoreCase));
        if (existing is null)
        {
            entries.Add(new CatalogEntry
            {
                Name = Path.GetFileNameWithoutExtension(dialog.FileName),
                Description = "Локальная программа зарегистрирована в ASTER. Путь сохранён; управление ИИ будет подключаться отдельно с разрешениями пользователя.",
                Details = dialog.FileName,
                EntryType = "program"
            });
            WriteEntries(entries);
        }
        Refresh();
        MessageBox.Show(Window.GetWindow(this),
            "Программа добавлена в каталог ASTER. Сейчас сохранён путь к .exe. Это ещё не предоставляет ИИ полный доступ к программе: безопасный запуск и управление будут выполняться через отдельные разрешённые действия.",
            "ASTER — PLUGIN", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void Refresh()
    {
        var entries = ReadEntries();
        _items.Children.Clear();
        _count.Text = "Элементов: " + entries.Count;
        if (entries.Count == 0)
        {
            _items.Children.Add(new TextBlock { Text = _kind == CatalogKind.Skill ? "Пока нет навыков. Нажми «Добавить навык»." : "Пока нет плагинов. Добавь сервис, API или MCP-сервер.", Foreground = Brush("#91A7B5"), FontSize = 13, Margin = new Thickness(8, 18, 0, 0) });
            return;
        }
        foreach (var entry in entries)
        {
            var card = new Border { Background = Brush("#111D2A"), BorderBrush = Brush("#294052"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(9), Padding = new Thickness(14), Margin = new Thickness(0, 0, 0, 10) };
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var body = new StackPanel();
            body.Children.Add(new TextBlock { Text = entry.Name, Foreground = Brush("#EAF7FC"), FontSize = 15, FontWeight = FontWeights.SemiBold });
            body.Children.Add(new TextBlock { Text = entry.Description, Foreground = Brush("#91A7B5"), FontSize = 11, Margin = new Thickness(0, 4, 0, 0), TextWrapping = TextWrapping.Wrap });
            if (!string.IsNullOrWhiteSpace(entry.Details))
                body.Children.Add(new TextBlock { Text = entry.Details, Foreground = Brush("#A8C7D5"), FontSize = 10, Margin = new Thickness(0, 7, 0, 0), TextWrapping = TextWrapping.Wrap, MaxHeight = 48 });
            grid.Children.Add(body);
            var remove = new Button { Content = "Удалить", Padding = new Thickness(9, 5, 9, 5), VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(12, 0, 0, 0) };
            remove.Click += (_, _) => { var current = ReadEntries(); current.RemoveAll(x => x.Name == entry.Name && x.Details == entry.Details); WriteEntries(current); Refresh(); };
            Grid.SetColumn(remove, 1);
            grid.Children.Add(remove);
            card.Child = grid;
            _items.Children.Add(card);
        }
    }
}
