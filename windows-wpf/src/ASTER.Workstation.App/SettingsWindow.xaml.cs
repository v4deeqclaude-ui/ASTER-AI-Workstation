using System.Windows;

namespace ASTER.Workstation.App;

public partial class SettingsWindow : Window
{
    public SettingsWindow()
    {
        InitializeComponent();
        KeyStatusText.Text = string.IsNullOrWhiteSpace(WindowsCredentialStore.ReadApiKey())
            ? "Ключ не сохранён."
            : "API-ключ уже сохранён в Windows.";
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
            StatusText.Text = "Готово. Теперь можно открыть AI-ассистента.";
        }
        catch (Exception ex)
        {
            StatusText.Text = "Не удалось сохранить ключ: " + ex.Message;
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}