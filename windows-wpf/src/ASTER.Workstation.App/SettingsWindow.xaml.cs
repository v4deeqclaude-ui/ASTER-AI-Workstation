using System.Windows;
using ASTER.Workstation.Core.Services;

namespace ASTER.Workstation.App;

public partial class SettingsWindow : Window
{
    public SettingsWindow()
    {
        InitializeComponent();
        WorkspaceActivityLog.Write("SETTINGS", "Открыты настройки локального ИИ.");
        var settings = AiProviderSettings.Load();
        OllamaEndpointBox.Text = settings.OllamaEndpoint;
        OllamaModelBox.Text = settings.OllamaModel;
        LocalAiStatusText.Text = "Локальный ИИ ещё не проверен.";
        AddConsoleLine("ASTER использует только локальную модель Ollama.");
    }

    private void AddConsoleLine(string line)
    {
        DiagnosticConsole.AppendText(line + Environment.NewLine);
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

    private void OpenTerminal_Click(object sender, RoutedEventArgs e)
    {
        WorkspaceActivityLog.Write("SETTINGS", "Командная строка открыта из раздела настроек.");
        var terminal = new TerminalWindow { Owner = this };
        terminal.Show();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}