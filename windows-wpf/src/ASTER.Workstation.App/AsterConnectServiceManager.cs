using System.Diagnostics;
using System.IO;
using System.Net.Http;


namespace ASTER.Workstation.App;

internal static class AsterConnectServiceManager
{
    private const string BaseAddress = "http://127.0.0.1:17843";
    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromSeconds(3) };
    private static Process? _process;

    public static async Task<string> StartAsync()
    {
        if (await IsHealthyAsync())
            return "ASTER Connect уже запущен и отвечает.";

        var executable = Path.Combine(AppContext.BaseDirectory, "ASTER.Connect", "ASTER.Connect.exe");
        if (!File.Exists(executable))
            throw new FileNotFoundException(
                "Файлы ASTER Connect не найдены рядом с ASTER. Установи полную сборку ASTER, в которую встроен коннектор.",
                executable);

        var dataDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ASTER", "Connect");
        var keyRingDirectory = Path.Combine(dataDirectory, "keys");
        Directory.CreateDirectory(keyRingDirectory);
        Directory.CreateDirectory(dataDirectory);

        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            WorkingDirectory = Path.GetDirectoryName(executable)!,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        startInfo.Environment["ASPNETCORE_ENVIRONMENT"] = "Production";
        startInfo.Environment["ASPNETCORE_URLS"] = BaseAddress;
        startInfo.Environment["DataProtection__KeyRingPath"] = keyRingDirectory;
        startInfo.Environment["PairingStore__FilePath"] = Path.Combine(dataDirectory, "pairings.json");

        _process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Windows не смог запустить ASTER Connect.");

        var deadline = DateTime.UtcNow.AddSeconds(20);
        while (DateTime.UtcNow < deadline)
        {
            if (_process.HasExited)
                throw new InvalidOperationException(
                    $"ASTER Connect завершился при запуске (код {_process.ExitCode}). Проверь файлы сборки.");

            if (await IsHealthyAsync())
                return "ASTER Connect запущен и отвечает на локальном адресе " + BaseAddress + ".";

            await Task.Delay(400);
        }

        Stop();
        throw new TimeoutException("ASTER Connect запустился, но не ответил на проверку доступности за 20 секунд.");
    }

    public static async Task<string> CheckAsync()
    {
        if (!await IsHealthyAsync())
            return "ASTER Connect пока недоступен. Нажми «Запустить коннектор».";

        using var response = await Client.GetAsync(BaseAddress + "/api/github/status");
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync();
        using var document = System.Text.Json.JsonDocument.Parse(json);
        var configured = document.RootElement.TryGetProperty("appConfigured", out var value) && value.GetBoolean();

        return configured
            ? "ASTER Connect доступен. Настройки GitHub App обнаружены; этап обмена токенами и доступа к репозиториям ещё требует реализации."
            : "ASTER Connect доступен. Для подключения GitHub потребуется настроить GitHub App и публичный HTTPS-адрес сервера.";
    }

    public static void Stop()
    {
        try
        {
            if (_process is { HasExited: false })
            {
                _process.Kill(entireProcessTree: true);
                _process.WaitForExit(3000);
            }
        }
        catch
        {
            // Shutdown must not prevent ASTER from closing.
        }
        finally
        {
            _process?.Dispose();
            _process = null;
        }
    }

    private static async Task<bool> IsHealthyAsync()
    {
        try
        {
            using var response = await Client.GetAsync(BaseAddress + "/health/live");
            return response.IsSuccessStatusCode;
        }
        catch (HttpRequestException)
        {
            return false;
        }
        catch (TaskCanceledException)
        {
            return false;
        }
    }
}
