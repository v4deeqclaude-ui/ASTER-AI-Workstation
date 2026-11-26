using System.Text.Json;
using System.Windows;

namespace ASTER.Workstation.App;

public partial class SettingsWindow : Window
{
    public SettingsWindow()
    {
        InitializeComponent();
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
                "connected" => "Аккаунт подключён, но разрешение на использование ChatGPT plan не предоставлено.",
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
                : "Вход выполнен, но ChatGPT plan sharing для этого аккаунта не разрешён.";
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
            ChatGPTStatusText.Text = "Аккаунт ChatGPT отключён.";
        }
        catch (Exception ex) { StatusText.Text = ex.Message; }
        finally { await RefreshChatGPTStatusAsync(); }
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
            ApiKeyBox.Clear();
            KeyStatusText.Text = "API-ключ сохранён в Windows.";
            StatusText.Text = "API-ключ сохранён отдельно от входа через ChatGPT.";
        }
        catch (Exception ex) { StatusText.Text = "Не удалось сохранить ключ: " + ex.Message; }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
