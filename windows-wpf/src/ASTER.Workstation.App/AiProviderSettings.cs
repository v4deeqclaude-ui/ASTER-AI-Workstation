using System.Text.Json;
using ASTER.Workstation.Core.Services;

namespace ASTER.Workstation.App;

public sealed class AiProviderSettings
{
    public string Provider { get; set; } = "ollama";
    public string OllamaEndpoint { get; set; } = "http://localhost:11434";
    public string OllamaModel { get; set; } = "qwen3:1.7b";

    private static string SettingsFile => Path.Combine(AppPaths.SettingsDirectory, "ai-provider.json");

    public static AiProviderSettings Load()
    {
        try
        {
            if (!File.Exists(SettingsFile)) return new AiProviderSettings();
            var settings = JsonSerializer.Deserialize<AiProviderSettings>(File.ReadAllText(SettingsFile));
            return settings ?? new AiProviderSettings();
        }
        catch
        {
            return new AiProviderSettings();
        }
    }

    public void Save()
    {
        Directory.CreateDirectory(AppPaths.SettingsDirectory);
        File.WriteAllText(SettingsFile, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
    }
}
