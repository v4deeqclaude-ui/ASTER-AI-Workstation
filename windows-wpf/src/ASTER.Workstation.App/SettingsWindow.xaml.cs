using System.Windows;
using ASTER.Workstation.Core.Services;

namespace ASTER.Workstation.App;

public partial class SettingsWindow : Window
{
    public SettingsWindow()
    {
        InitializeComponent();
        WorkspaceActivityLog.Write("SETTINGS", "Открыты настройки ASTER.");
        var settings = AiProviderSettings.Load();
        OllamaEndpointBox.Text = settings.OllamaEndpoint;
        OllamaModelBox.Text = settings.OllamaModel;
        LocalAiStatusText.Text = "Локальный ИИ ещё не проверен.";
        ConnectStatusText.Text = "Нажми «Проверить» или запусти встроенный коннектор.";
        AddConsoleLine("Открыты настройки Ollama и ASTER Connect.");
    }

    private void AddConsoleLine(string line)
    {
        DiagnosticConsole.AppendText($"[{DateTime.Now:HH:mm:ss}] {line}{Environment.NewLine}");
        DiagnosticConsole.ScrollToEnd();
    }

    private void ClearConsole_Click(object sender, RoutedEventArgs e)
    {
        DiagnosticConsole.Clear();
        AddConsoleLine("Журнал очищен.");
    }

    private void SaveAiSettings_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var endpoint = OllamaEndpointBox.Text.Trim().TrimEnd('/');
            if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) ||
                (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            {
                StatusText.Text = "Укажи корректный адрес Ollama, например http://localhost:11434.";
                return;
            }

            var model = OllamaModelBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(model))
            {
                StatusText.Text = "Укажи название локальной модели, например qwen3:1.7b.";
                return;
            }

            new AiProviderSettings
            {
                OllamaEndpoint = endpoint,
                OllamaModel = model
            }.Save();

            StatusText.Text = "Настройки локального ИИ сохранены.";
            AddConsoleLine($"Сохранены настройки Ollama: {endpoint}, модель {model}.");
            WorkspaceActivityLog.Write("SETTINGS", "Сохранены настройки локальной модели Ollama.");
        }
        catch (Exception ex)
        {
            StatusText.Text = "Не удалось сохранить настройки ИИ: " + ex.Message;
            AddConsoleLine("Ошибка сохранения: " + ex.Message);
        }
    }

    private async void CheckOllama_Click(object sender, RoutedEventArgs e)
    {
        CheckOllamaButton.IsEnabled = false;
        LocalAiStatusText.Text = "Проверяем Ollama и список моделей…";
        try
        {
            var endpoint = OllamaEndpointBox.Text.Trim().TrimEnd('/');
            var models = await OllamaChatClient.GetInstalledModelsAsync(endpoint);
            var model = OllamaModelBox.Text.Trim();
            if (models.Contains(model, StringComparer.OrdinalIgnoreCase))
            {
                LocalAiStatusText.Text = "Ollama доступен. Модель " + model + " установлена.";
                AddConsoleLine("Проверка Ollama успешна; выбранная модель найдена.");
            }
            else
            {
                LocalAiStatusText.Text = models.Count == 0
                    ? "Ollama отвечает, но установленных моделей не найдено."
                    : "Ollama доступен, но модель " + model + " не найдена. Установленные: " + string.Join(", ", models);
                AddConsoleLine("Ollama доступен; выбранная модель отсутствует.");
            }
        }
        catch (Exception ex)
        {
            LocalAiStatusText.Text = "Ollama недоступен: " + ex.Message;
            AddConsoleLine("Проверка Ollama завершилась ошибкой: " + ex.Message);
        }
        finally
        {
            CheckOllamaButton.IsEnabled = true;
        }
    }

    private async void StartConnect_Click(object sender, RoutedEventArgs e)
    {
        StartConnectButton.IsEnabled = false;
        ConnectStatusText.Text = "Запускаем встроенный ASTER Connect…";
        try
        {
            ConnectStatusText.Text = await AsterConnectServiceManager.StartAsync();
            AddConsoleLine(ConnectStatusText.Text);
            WorkspaceActivityLog.Write("CONNECT", "Локальный ASTER Connect запущен.");
        }
        catch (Exception ex)
        {
            ConnectStatusText.Text = "Не удалось запустить ASTER Connect: " + ex.Message;
            AddConsoleLine(ConnectStatusText.Text);
            WorkspaceActivityLog.Write("ERROR", "Ошибка запуска ASTER Connect: " + ex.Message);
        }
        finally
        {
            StartConnectButton.IsEnabled = true;
        }
    }

    private async void CheckConnect_Click(object sender, RoutedEventArgs e)
    {
        CheckConnectButton.IsEnabled = false;
        ConnectStatusText.Text = "Проверяем локальный коннектор…";
        try
        {
            ConnectStatusText.Text = await AsterConnectServiceManager.CheckAsync();
            AddConsoleLine(ConnectStatusText.Text);
        }
        catch (Exception ex)
        {
            ConnectStatusText.Text = "Проверка ASTER Connect не выполнена: " + ex.Message;
            AddConsoleLine(ConnectStatusText.Text);
        }
        finally
        {
            CheckConnectButton.IsEnabled = true;
        }
    }

    private void StopConnect_Click(object sender, RoutedEventArgs e)
    {
        AsterConnectServiceManager.Stop();
        ConnectStatusText.Text = "Локальный ASTER Connect остановлен.";
        AddConsoleLine(ConnectStatusText.Text);
        WorkspaceActivityLog.Write("CONNECT", "Локальный ASTER Connect остановлен из настроек.");
    }

    private void OpenTerminal_Click(object sender, RoutedEventArgs e)
    {
        WorkspaceActivityLog.Write("SETTINGS", "Командная строка открыта из раздела настроек.");
        var terminal = new TerminalWindow { Owner = this };
        terminal.Show();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}