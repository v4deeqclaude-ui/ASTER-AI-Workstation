using System.Text.Json;
using System.IO;
using System.Windows;
using ASTER.Workstation.Core.Services;

namespace ASTER.Workstation.App;

public partial class SettingsWindow : Window
{
    public SettingsWindow()
    {
        InitializeComponent();
        WorkspaceActivityLog.Write("SETTINGS", "Открыты настройки приложения.");
        var proxyPath = Path.Combine(AppPaths.Root, "chatgpt-proxy.txt");
        ProxyBox.Text = File.Exists(proxyPath) ? File.ReadAllText(proxyPath).Trim() : "http://127.0.0.1:10809";
        ChatGPTBridgeService.Shared.DiagnosticReceived += OnDiagnosticReceived;
        AddConsoleLine("Консоль готова. Здесь будут отображаться события подключения ChatGPT.");
        KeyStatusText.Text = string.IsNullOrWhiteSpace(WindowsCredentialStore.ReadApiKey())
            ? "Ключ не сохранён."
            : "API-ключ уже сохранён в Windows.";
        Loaded += async (_, _) => await RefreshChatGPTStatusAsync();
    }

    private void OnDiagnosticReceived(string line)
    {
        Dispatcher.BeginInvoke(() => AddConsoleLine(line));
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

    protected override void OnClosed(EventArgs e)
    {
        ChatGPTBridgeService.Shared.DiagnosticReceived -= OnDiagnosticReceived;
        base.OnClosed(e);
    }

    private async Task RefreshChatGPTStatusAsync()
    {
        try
        {
            AddConsoleLine("Проверка текущей сессии ChatGPT…");
            var session = await ChatGPTBridgeService.Shared.GetSessionAsync();
            var status = session.TryGetProperty("status", out var s) ? s.GetString() : "disconnected";
            var sharing = session.TryGetProperty("sharing", out var sh) && sh.GetBoolean();
            var identity = session.TryGetProperty("identity", out var ident) && ident.TryGetProperty("email", out var email)
                ? email.GetString() : null;
            ChatGPTStatusText.Text = status switch
            {
                "connected" when sharing => "Подключено · доступ к ChatGPT plan sharing подтверждён" + (string.IsNullOrWhiteSpace(identity) ? "" : " · " + identity),
                "connected" => "Аккаунт ChatGPT подключён. Для AI-чата добавь OpenAI API-ключ ниже.",
                "reauth_required" => "Требуется повторный вход в ChatGPT.",
                "connecting" => "Выполняется вход в ChatGPT…",
                _ => "ChatGPT не подключён."
            };
            DisconnectButton.IsEnabled = status == "connected" || status == "reauth_required";
            AddConsoleLine("Статус сессии: " + (status ?? "unknown") + ".");
        }
        catch (Exception ex)
        {
            ChatGPTStatusText.Text = "Модуль ChatGPT пока недоступен.";
            StatusText.Text = ex.Message;
            AddConsoleLine("Ошибка проверки сессии: " + ex.Message);
        }
    }

    private async void SignIn_Click(object sender, RoutedEventArgs e)
    {
        SignInButton.IsEnabled = false;
        ChatGPTStatusText.Text = "Открываем защищённую страницу входа…";
        StatusText.Text = "Войди в ChatGPT в браузере. Не отправляй никому пароль или коды.";
        AddConsoleLine("Начат процесс входа в ChatGPT.");
        try
        {
            var session = await ChatGPTBridgeService.Shared.SignInAsync();
            var sharing = session.TryGetProperty("sharing", out var sh) && sh.GetBoolean();
            AddConsoleLine("Авторизация завершена. Plan sharing: " + (sharing ? "доступен." : "не подтверждён."));
            ChatGPTStatusText.Text = sharing
                ? "Вход выполнен. Разрешение на ChatGPT plan sharing подтверждено."
                : "Вход в аккаунт выполнен. Для AI-чата используй отдельный OpenAI API-ключ.";
        }
        catch (Exception ex)
        {
            ChatGPTStatusText.Text = "Не удалось завершить вход.";
            StatusText.Text = ex.Message;
            AddConsoleLine("Ошибка авторизации: " + ex.Message);
        }
        finally
        {
            SignInButton.IsEnabled = true;
            await RefreshChatGPTStatusAsync();
        }
    }

    private async void Disconnect_Click(object sender, RoutedEventArgs e)
    {
        DisconnectButton.IsEnabled = false;
        try
        {
            await ChatGPTBridgeService.Shared.DisconnectAsync();
            WorkspaceActivityLog.Write("CHATGPT", "Пользователь отключил аккаунт ChatGPT.");
            ChatGPTStatusText.Text = "Аккаунт ChatGPT отключён.";
        }
        catch (Exception ex) { StatusText.Text = ex.Message; }
        finally { await RefreshChatGPTStatusAsync(); }
    }

    private void SaveProxy_Click(object sender, RoutedEventArgs e)
    {
        var value = ProxyBox.Text.Trim();
        if (!string.Equals(value, "direct", StringComparison.OrdinalIgnoreCase) &&
            (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttp || string.IsNullOrWhiteSpace(uri.Host) || uri.Port < 1))
        {
            StatusText.Text = "Укажи корректный HTTP-прокси или слово direct.";
            return;
        }
        try
        {
            Directory.CreateDirectory(AppPaths.Root);
            File.WriteAllText(Path.Combine(AppPaths.Root, "chatgpt-proxy.txt"), value);
            WorkspaceActivityLog.Write("SETTINGS", "Сохранена настройка сетевого подключения (значение не выводится в журнал).");
            StatusText.Text = "Настройка прокси сохранена. Полностью закрой и заново запусти ASTER, чтобы применить её.";
            AddConsoleLine("Настройка прокси сохранена; для применения нужен перезапуск ASTER.");
        }
        catch (Exception ex)
        {
            StatusText.Text = "Не удалось сохранить прокси: " + ex.Message;
            AddConsoleLine("Ошибка сохранения настройки прокси: " + ex.Message);
        }
    }

    private void DeleteApiKey_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            ApiKeyBox.Clear();
            WindowsCredentialStore.DeleteApiKey();
            WorkspaceActivityLog.Write("SECURITY", "Сохранённый OpenAI API-ключ удалён из Windows Credential Manager.");
            KeyStatusText.Text = "API-ключ удалён из Windows. В ASTER больше не сохранён.";
            StatusText.Text = "Сохранённый API-ключ удалён. Вход через ChatGPT и история переписки не затронуты.";
            AddConsoleLine("Сохранённый OpenAI API-ключ удалён из Windows Credential Manager.");
        }
        catch (Exception ex)
        {
            StatusText.Text = "Не удалось удалить API-ключ: " + ex.Message;
            AddConsoleLine("Ошибка удаления API-ключа: " + ex.Message);
        }
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var key = ApiKeyBox.Password.Trim();
        if (string.IsNullOrWhiteSpace(key))
        {
            StatusText.Text = "Вставь API-ключ перед сохранением.";
            return;
        }
        try
        {
            WindowsCredentialStore.SaveApiKey(key);
            WorkspaceActivityLog.Write("SECURITY", "OpenAI API-ключ сохранён в Windows Credential Manager. Значение ключа не журналируется.");
            ApiKeyBox.Clear();
            KeyStatusText.Text = "API-ключ сохранён в Windows.";
            StatusText.Text = "API-ключ сохранён отдельно от входа через ChatGPT.";
        }
        catch (Exception ex) { StatusText.Text = "Не удалось сохранить ключ: " + ex.Message; WorkspaceActivityLog.Write("ERROR", "Не удалось сохранить API-ключ: " + ex.Message); }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
