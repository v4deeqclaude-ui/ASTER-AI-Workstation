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
        WorkspaceActivityLog.Write("ASTER", "Главное окно приложения создано.");
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            AppPaths.EnsureCreated();
            await _repository.InitializeAsync();
            await LoadProjectsAsync();
            _isReady = true;
            WorkspaceActivityLog.Write("PROJECTS", $"Загружено проектов: {_projects.Count}.");
            WorkspaceActivityLog.Write("ASTER", "Приложение готово к работе.");
            SetStatus("Готово. Проекты и настройки хранятся локально.");
            Home_Click(this, new RoutedEventArgs());
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Не удалось подготовить локальную базу данных.\n\n" + ex.Message,
                "ASTER — ошибка запуска", MessageBoxButton.OK, MessageBoxImage.Error);
            SetStatus("Ошибка инициализации базы данных.");
            WorkspaceActivityLog.Write("ERROR", "Ошибка инициализации базы данных: " + ex.Message);
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
            WorkspaceActivityLog.Write("PROJECTS", $"Добавлен проект: {project.Name}.");
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
        WorkspaceActivityLog.Write("PROJECTS", $"Проект убран из списка: {project.Name}.");
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
        WorkspaceActivityLog.Write("PROJECTS", $"Открыт проект: {project.Name}.");
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
        WorkspaceActivityLog.Write("PROJECTS", "Выполнено обновление списка проектов.");
        WorkspaceActivityLog.Write("PROJECTS", "Список проектов обновлён.");
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
            WorkspaceActivityLog.Write("BACKUP", "Создана резервная копия базы данных.");
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
        SetActiveNavigation(NavHome);
        OpenAiWorkspace("HOME", "Главная",
            "Ты — ASTER, универсальный интеллектуальный помощник. Помогай пользователю с любыми повседневными и профессиональными вопросами: объясняй, анализируй, планируй, обучай, помогай с текстами, технологиями и творческими задачами. Отвечай на русском, если пользователь пишет по-русски. Будь честен о своих возможностях и не утверждай, что выполнял действия вне приложения.");
        SetStatus("Главный ИИ-чат ASTER готов к работе.");
    }

    private void Projects_Click(object sender, RoutedEventArgs e)
    {
        SetActiveNavigation(NavProjects);
        EmbeddedPage.Content = null;
        EmbeddedPage.Visibility = Visibility.Collapsed;
        HomePage.Visibility = Visibility.Visible;
        PageTitle.Text = "Проекты";
        PageSubtitle.Text = "Твои рабочие пространства, папки и файлы проектов.";
        SetStatus("Открыт центр управления проектами.");
        WorkspaceActivityLog.Write("NAVIGATION", "Открыт раздел «Проекты».");
    }

    private void Applications_Click(object sender, RoutedEventArgs e)
    {
        SetActiveNavigation(NavApplications);
        OpenAiWorkspace("APPS", "ПРИЛОЖЕНИЯ",
            "Ты — ASTER App Creator. Помогай проектировать и создавать новые приложения: сначала уточняй назначение и платформу, затем предлагай архитектуру, интерфейс, структуру проекта и пошаговый план реализации. Учитывай, что пользователь предпочитает готовые файлы и простые инструкции. Не утверждай, что файлы созданы или приложение собрано, если это не подтверждено.");
    }

    private void Plugin_Click(object sender, RoutedEventArgs e)
    {
        SetActiveNavigation(NavPlugin);
        HomePage.Visibility = Visibility.Collapsed;
        EmbeddedPage.Content = new CatalogWorkspaceControl(CatalogKind.Plugin);
        EmbeddedPage.Visibility = Visibility.Visible;
        WorkspaceActivityLog.Write("NAVIGATION", "Открыт каталог PLUGIN.");
    }

    private void Skill_Click(object sender, RoutedEventArgs e)
    {
        SetActiveNavigation(NavSkill);
        HomePage.Visibility = Visibility.Collapsed;
        EmbeddedPage.Content = new CatalogWorkspaceControl(CatalogKind.Skill);
        EmbeddedPage.Visibility = Visibility.Visible;
        WorkspaceActivityLog.Write("NAVIGATION", "Открыт каталог SKILL.");
    }

    private void Video_Click(object sender, RoutedEventArgs e)
    {
        SetActiveNavigation(NavVideo);
        OpenAiWorkspace("VIDEO", "ВИДЕО",
            "Ты — помощник ASTER для подготовки видеозадач. Превращай обычное описание пользователя в подробный профессиональный промт для генерации или редактирования видео: сцена, объект, действие, камера, движение, свет, стиль, длительность, формат и ограничения. Не заявляй, что видео уже создано: пока готовь промт и уточняй важные детали.");
    }

    private void Photo_Click(object sender, RoutedEventArgs e)
    {
        SetActiveNavigation(NavPhoto);
        OpenAiWorkspace("PHOTO", "ФОТО",
            "Ты — помощник ASTER для подготовки задач с изображениями. Превращай запрос пользователя в точный промт для создания или редактирования изображения: субъект, композиция, поза, окружение, освещение, стиль, детали, сохранение важных особенностей и негативные ограничения. Не заявляй, что изображение уже обработано: пока готовь промт и уточняй только необходимые детали.");
    }

    private void Music_Click(object sender, RoutedEventArgs e)
    {
        SetActiveNavigation(NavMusic);
        OpenAiWorkspace("MUSIC", "МУЗЫКА",
            "Ты — помощник ASTER для подготовки музыкальных задач. Превращай запрос пользователя в подробное техническое задание и промт: жанр, настроение, темп, инструменты, вокал, структура, длительность, динамика, микс и ограничения. Не заявляй, что музыка уже создана: пока готовь промт и уточняй важные детали.");
    }

    private void OpenAiWorkspace(string key, string title, string prompt)
    {
        HomePage.Visibility = Visibility.Collapsed;
        EmbeddedPage.Content = new ChatWindow(_database, key, title, prompt);
        EmbeddedPage.Visibility = Visibility.Visible;
        WorkspaceActivityLog.Write("NAVIGATION", $"Открыт раздел «{title}» со своим чатом и инструкциями ASTER.");
    }

    private void History_Click(object sender, RoutedEventArgs e)
    {
        PageTitle.Text = "История";
        PageSubtitle.Text = "История задач и изменений появится после подключения AI-модуля.";
        WorkspaceActivityLog.Write("NAVIGATION", "Открыт раздел «История».");
        MessageBox.Show(this, "История AI-задач будет добавлена на следующем этапе. В версии 0.1 сохраняется список проектов и время последнего открытия.",
            "ASTER — история", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void Chat_Click(object sender, RoutedEventArgs e)
    {
        OpenAiWorkspace("CHAT", "ИИ-ЧАТ",
            "Ты — ASTER, локальный интеллектуальный помощник. Отвечай понятно, полезно и по делу.");
        SetStatus("ИИ-чат открыт внутри ASTER.");
    }

    private void Terminal_Click(object sender, RoutedEventArgs e)
    {
        WorkspaceActivityLog.Write("ASTER", "Открыта командная строка рабочего пространства.");
        var window = new TerminalWindow { Owner = this };
        window.Show();
    }

    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        SetActiveNavigation(NavSettings);
        PageTitle.Text = "Настройки";
        PageSubtitle.Text = "Параметры подключения AI-провайдера."; 
        WorkspaceActivityLog.Write("NAVIGATION", "Открыты настройки приложения.");
        var window = new SettingsWindow { Owner = this };
        window.ShowDialog();
    }

    private void SetActiveNavigation(Button active)
    {
        var buttons = new[] { NavHome, NavProjects, NavPlugin, NavSkill, NavPhoto, NavVideo, NavApplications, NavMusic, NavSettings };
        foreach (var button in buttons)
        {
            button.Background = button == active
                ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(20, 61, 82))
                : System.Windows.Media.Brushes.Transparent;
            button.Foreground = button == active
                ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(168, 241, 255))
                : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(234, 247, 252));
            button.BorderBrush = button == active
                ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(22, 200, 245))
                : System.Windows.Media.Brushes.Transparent;
            button.BorderThickness = button == active ? new Thickness(1) : new Thickness(0);
        }
    }

    public void ReturnToHome() => Home_Click(this, new RoutedEventArgs());

    private void ClearProjectView()
    {
        SelectedProjectName.Text = "Проект не выбран";
        SelectedProjectPath.Text = "Добавь проект, чтобы просмотреть его папки и файлы.";
        FilesTree.Items.Clear();
        EmptyProjectPanel.Visibility = Visibility.Visible;
    }

    private void SetStatus(string message) => StatusText.Text = message;
}
