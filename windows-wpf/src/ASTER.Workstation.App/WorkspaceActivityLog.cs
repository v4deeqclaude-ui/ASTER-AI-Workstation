using System.Text.RegularExpressions;

namespace ASTER.Workstation.App;

public static class WorkspaceActivityLog
{
    public static event Action<string>? EntryAdded;

    public static void Write(string source, string message)
    {
        var safe = Regex.Replace(message ?? "", @"(?i)(bearer\\s+|access[_ -]?token[=: ]+|refresh[_ -]?token[=: ]+|client[_ -]?secret[=: ]+)[^\\s,;]+", "$1[скрыто]");
        safe = Regex.Replace(safe, @"https?://[^\\s""'<>]+", "[URL скрыт]", RegexOptions.IgnoreCase);
        if (safe.Length > 1000) safe = safe[..1000];
        EntryAdded?.Invoke($"[{DateTime.Now:HH:mm:ss}] [{source}] {safe}");
    }
}
