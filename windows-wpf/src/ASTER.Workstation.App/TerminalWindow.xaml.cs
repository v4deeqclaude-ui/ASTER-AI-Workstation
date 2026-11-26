using System.Diagnostics;
using System.Windows;
using System.Windows.Input;

namespace ASTER.Workstation.App;

public partial class TerminalWindow : Window
{
    private Process? _running;

    public TerminalWindow()
    {
        InitializeComponent();
        WorkspaceActivityLog.EntryAdded += OnActivity;
        OutputBox.AppendText("ASTER WORKSPACE TERMINAL\\r\\n");
        OutputBox.AppendText("Системные события и команды ASTER будут отображаться здесь.\\r\\n");
        OutputBox.AppendText("Ввод команд выполняется через cmd.exe от имени текущего пользователя.\\r\\n\\r\\n");
        Closed += (_, _) => WorkspaceActivityLog.EntryAdded -= OnActivity;
    }

    private void OnActivity(string line) => Dispatcher.BeginInvoke(() =>
    {
        OutputBox.AppendText(line + Environment.NewLine);
        OutputBox.ScrollToEnd();
    });

    private async void Run_Click(object sender, RoutedEventArgs e) => await RunCommandAsync();

    private async void CommandBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            await RunCommandAsync();
        }
    }

    private async Task RunCommandAsync()
    {
        var command = CommandBox.Text.Trim();
        if (command.Length == 0 || _running is not null) return;
        CommandBox.Clear();
        OutputBox.AppendText($"> {command}{Environment.NewLine}");
        OutputBox.ScrollToEnd();
        WorkspaceActivityLog.Write("TERMINAL", "Запуск команды: " + command);

        var start = new ProcessStartInfo("cmd.exe")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        start.ArgumentList.Add("/d");
        start.ArgumentList.Add("/s");
        start.ArgumentList.Add("/c");
        start.ArgumentList.Add(command);
        start.WorkingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        var process = new Process { StartInfo = start, EnableRaisingEvents = true };
        process.OutputDataReceived += (_, e) => AppendOutput(e.Data, false);
        process.ErrorDataReceived += (_, e) => AppendOutput(e.Data, true);
        try
        {
            _running = process;
            RunButton.IsEnabled = false;
            StopButton.IsEnabled = true;
            StatusText.Text = "Команда выполняется…";
            if (!process.Start()) throw new InvalidOperationException("Не удалось запустить cmd.exe.");
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            await process.WaitForExitAsync();
            AppendOutput($"[Код завершения: {process.ExitCode}]", process.ExitCode != 0);
            WorkspaceActivityLog.Write("TERMINAL", $"Команда завершена с кодом {process.ExitCode}.");
        }
        catch (Exception ex)
        {
            AppendOutput("[Ошибка запуска] " + ex.Message, true);
            WorkspaceActivityLog.Write("TERMINAL", "Ошибка запуска команды: " + ex.Message);
        }
        finally
        {
            process.Dispose();
            _running = null;
            RunButton.IsEnabled = true;
            StopButton.IsEnabled = false;
            StatusText.Text = "Готово · команды выполняются от имени текущего пользователя Windows";
            CommandBox.Focus();
        }
    }

    private void AppendOutput(string? line, bool isError)
    {
        if (string.IsNullOrEmpty(line)) return;
        Dispatcher.BeginInvoke(() =>
        {
            OutputBox.AppendText((isError ? "[ОШИБКА] " : "") + line + Environment.NewLine);
            OutputBox.ScrollToEnd();
        });
    }

    private void Stop_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_running is { HasExited: false }) _running.Kill(entireProcessTree: true);
            WorkspaceActivityLog.Write("TERMINAL", "Пользователь остановил команду.");
        }
        catch (Exception ex) { AppendOutput("[Ошибка остановки] " + ex.Message, true); }
    }

    private void Clear_Click(object sender, RoutedEventArgs e) => OutputBox.Clear();
}
