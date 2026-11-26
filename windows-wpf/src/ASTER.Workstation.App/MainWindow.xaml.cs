using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using ASTER.Workstation.Core.Models;
using ASTER.Workstation.Core.Services;
using ASTER.Workstation.Data.Database;
using ASTER.Workstation.Data.Repositories;
using ASTER.Workstation.Projects;
using Microsoft.Win32;

namespace ASTER.Workstation.App;

public partial class MainWindow : Window
{
    private readonly ProjectRepository _repository;
    private readonly SqliteDatabase _database;
    private readonly FolderScanner _folderScanner = new();
    private readonly ObservableCollection<ProjectEntry> _projects = new();
    private bool _isReady;

    public MainWindow()
    {
        InitializeComponent();
        _database = new SqliteDatabase();
        _repository = new ProjectRepository(_database);
        ProjectsList.ItemsSource = _projects;
        Loaded += MainWindow_Loaded;
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            AppPaths.EnsureCreated();
            await _repository.InitializeAsync();
            await LoadProjectsAsync();
            _isReady = true;
            SetStatus("Готово. Проекты и настройки хранятся локально.");
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Не удалось подготовить локальную базу данных.\n\n" + ex.Message,
                "ASTER — ошибка запуска", MessageBoxButton.OK, MessageBoxImage.Error);
            SetStatus("Ошибка инициализации базы данных.");
        }
    }

    private async Task LoadProjectsAsync()
    {
        var selectedId = (ProjectsList.SelectedItem as ProjectEntry)?.Id;
        var items = await _repository.GetAllAsync();
        _projects.Clear();
        foreach (var project in items) _projects.Add(project);
        ProjectCountText.Text = _projects.Count.ToString();

        if (selectedId is not null)
            ProjectsList.SelectedItem = _projects.FirstOrDefault(p => p.Id == selectedId);
        if (ProjectsList.SelectedItem is null && _projects.Count > 0)
            ProjectsList.SelectedIndex = 0;
        if (_projects.Count == 0) ClearProjectView();
    }

    private async void AddProject_Click(object sender, RoutedEventArgs e)
    {
        if (!_isReady) return;
        var dialog = new OpenFolderDialog
        {
            Title = "Выбери корневую папку игрового проекта",
            Multiselect = false
        };
        if (dialog.ShowDialog(this) != true) return;

        var selectedPath = Path.GetFullPath(dialog.FolderName);
        if (!Directory.Exists(selectedPath))
        {
            MessageBox.Show(this, "Выбранная папка недоступна.", "ASTER", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var existing = _projects.FirstOrDefault(p => string.Equals(p.RootPath, selectedPath, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            ProjectsList.SelectedItem = existing;
            SetStatus("Этот проект уже есть в списке.");
            return;
        }

        var project = new ProjectEntry
        {
            Name = new DirectoryInfo(selectedPath).Name,
            RootPath = selectedPath,
            AddedUtc = DateTime.UtcNow,
            LastOpenedUtc = DateTime.UtcNow
        };

        try
        {
            await _repository.AddAsync(project);
            await LoadProjectsAsync();
            ProjectsList.SelectedItem = _projects.FirstOrDefault(p => p.Id == project.Id);
            SetStatus($"Проект «{project.Name}» добавлен. Исходная папка не изменялась.");
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Не удалось добавить проект. Возможно, эта папка уже зарегистрирована.\n\n" + ex.Message,
                "ASTER — ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async void RemoveProject_Click(object sender, RoutedEventArgs e)
    {
        if (ProjectsList.SelectedItem is not ProjectEntry project) return;
        var answer = MessageBox.Show(this,
            $"Убрать «{project.Name}» из списка ASTER?\n\nФайлы и папки на диске НЕ будут удалены.",
            "Подтверждение", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (answer != MessageBoxResult.Yes) return;

        await _repository.DeleteAsync(project.Id);
        await LoadProjectsAsync();
        SetStatus("Проект убран из списка. Файлы на диске не затронуты.");
    }

    private async void ProjectsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ProjectsList.SelectedItem is not ProjectEntry project) return;
        SelectedProjectName.Text = project.Name;
        SelectedProjectPath.Text = project.RootPath;
        FilesTree.Items.Clear();

        if (!Directory.Exists(project.RootPath))
        {
            EmptyProjectPanel.Visibility = Visibility.Visible;
            SetStatus("Папка проекта не найдена. Проверь путь или убери проект из списка.");
            return;
        }

        EmptyProjectPanel.Visibility = Visibility.Collapsed;
        foreach (var item in _folderScanner.GetChildren(project.RootPath).Take(250))
            FilesTree.Items.Add(CreateTreeItem(item, 0));

        await _repository.UpdateLastOpenedAsync(project.Id);
        SetStatus($"Открыт обзор проекта: {project.Name}. Режим только чтения.");
    }

    private TreeViewItem CreateTreeItem(FolderItem item, int depth)
    {
        var icon = item.IsDirectory ? "📁" : "📄";
        var node = new TreeViewItem
        {
            Header = $"{icon}  {item.Name}",
            Tag = item.FullPath,
            IsExpanded = false
        };
        if (item.IsDirectory && depth < 1)
        {
            foreach (var child in _folderScanner.GetChildren(item.FullPath).Take(100))
                node.Items.Add(CreateTreeItem(child, depth + 1));
            if (node.Items.Count == 0)
                node.Items.Add(new TreeViewItem { Header = "(папка пуста)" });
        }
        return node;
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e)
    {
        if (!_isReady) return;
        await LoadProjectsAsync();
        if (ProjectsList.SelectedItem is ProjectEntry selected)
            SetStatus($"Список обновлён. Выбран проект: {selected.Name}.");
        else
            SetStatus("Список проектов обновлён.");
    }

    private async void Backup_Click(object sender, RoutedEventArgs e)
    {
        if (!_isReady) return;
        try
        {
            AppPaths.EnsureCreated();
            await using (var connection = _database.CreateConnection())
            {
                await connection.OpenAsync();
                await using var checkpoint = connection.CreateCommand();
                checkpoint.CommandText = "PRAGMA wal_checkpoint(FULL);";
                await checkpoint.ExecuteNonQueryAsync();
            }
            var fileName = $"aster-workstation-{DateTime.Now:yyyyMMdd-HHmmss}.db";
            var destination = Path.Combine(AppPaths.BackupDirectory, fileName);
            File.Copy(AppPaths.DatabaseFile, destination, overwrite: false);
            SetStatus("Резервная копия создана: " + destination);
            MessageBox.Show(this, "Резервная копия базы данных создана.\n\n" + destination,
                "ASTER — резервирование", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Не удалось создать резервную копию.\n\n" + ex.Message,
                "ASTER — ошибка резервирования", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Home_Click(object sender, RoutedEventArgs e)
    {
        EmbeddedPage.Content = null;
        EmbeddedPage.Visibility = Visibility.Collapsed;
        HomePage.Visibility = Visibility.Visible;
        PageTitle.Text = "Главная";
        PageSubtitle.Text = "Твоя рабочая среда для создания игр.";
        SetStatus("Главная рабочая область ASTER.");
    }

    private void Projects_Click(object sender, RoutedEventArgs e)
    {
        PageTitle.Text = "Проекты";
        PageSubtitle.Text = "Добавляй существующие папки и переключайся между проектами.";
        SetStatus("Управление проектами доступно в версии 0.1.");
    }

    private void Applications_Click(object sender, RoutedEventArgs e)
    {
        PageTitle.Text = "Приложения";
        PageSubtitle.Text = "Unity, Blender и другие инструменты будут подключаться через адаптеры.";
        MessageBox.Show(this, "Подключение Unity, Blender и других программ запланировано на следующие этапы.\n\nВ версии 0.1 этот раздел является информационным.",
            "ASTER — приложения", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void History_Click(object sender, RoutedEventArgs e)
    {
        PageTitle.Text = "История";
        PageSubtitle.Text = "История задач и изменений появится после подключения AI-модуля.";
        MessageBox.Show(this, "История AI-задач будет добавлена на следующем этапе. В версии 0.1 сохраняется список проектов и время последнего открытия.",
            "ASTER — история", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void Chat_Click(object sender, RoutedEventArgs e)
    {
        HomePage.Visibility = Visibility.Collapsed;
        EmbeddedPage.Content = new ChatWindow(_database);
        EmbeddedPage.Visibility = Visibility.Visible;
        SetStatus("AI-ассистент открыт внутри ASTER. Остальные разделы остаются доступны.");
    }

    private void Terminal_Click(object sender, RoutedEventArgs e)
    {
        WorkspaceActivityLog.Write("ASTER", "Открыта командная строка рабочего пространства.");
        var window = new TerminalWindow { Owner = this };
        window.Show();
    }

    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        PageTitle.Text = "Настройки";
        PageSubtitle.Text = "Параметры подключения AI-провайдера."; 
        var window = new SettingsWindow { Owner = this };
        window.ShowDialog();
    }

    private void ClearProjectView()
    {
        SelectedProjectName.Text = "Проект не выбран";
        SelectedProjectPath.Text = "Добавь проект, чтобы просмотреть его папки и файлы.";
        FilesTree.Items.Clear();
        EmptyProjectPanel.Visibility = Visibility.Visible;
    }

    private void SetStatus(string message) => StatusText.Text = message;
}
