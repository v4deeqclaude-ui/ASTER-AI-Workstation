using System.Collections.Concurrent;
using System.Text.RegularExpressions;

namespace ASTER.Workstation.App;

public static class WorkspaceActivityLog
{
    private const int MaxEntries = 1000;
    private static readonly ConcurrentQueue<string> Entries = new();

    public static event Action<string>? EntryAdded;

    public static IReadOnlyList<string> GetRecentEntries(int limit = 500) =>
        Entries.ToArray().TakeLast(Math.Clamp(limit, 1, MaxEntries)).ToArray();

    public static void Write(string source, string message)
    {
        var safe = (message ?? "").Replace("\r", " ").Replace("\n", " ").Replace("\t", " ");
        safe = Regex.Replace(safe, @"(?i)(bearer\s+|access[_ -]?token[=: ]+|refresh[_ -]?token[=: ]+|client[_ -]?secret[=: ]+|api[_ -]?key[=: ]+)[^\s,;]+", "$1[скрыто]");
        safe = Regex.Replace(safe, @"https?://[^\s""'<>]+", "[URL скрыт]", RegexOptions.IgnoreCase);
        if (safe.Length > 1000) safe = safe[..1000];
        var entry = $"[{DateTime.Now:HH:mm:ss}] [{source}] {safe}";
        Entries.Enqueue(entry);
        while (Entries.Count > MaxEntries) Entries.TryDequeue(out _);
        EntryAdded?.Invoke(entry);
    }
}
