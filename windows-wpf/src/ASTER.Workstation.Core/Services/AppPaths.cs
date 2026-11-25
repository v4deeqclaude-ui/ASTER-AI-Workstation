namespace ASTER.Workstation.Core.Services;

public static class AppPaths
{
    public static string Root { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ASTER", "Workstation");
    public static string DatabaseDirectory => Path.Combine(Root, "Database");
    public static string BackupDirectory => Path.Combine(Root, "Backups");
    public static string SettingsDirectory => Path.Combine(Root, "Settings");
    public static string DatabaseFile => Path.Combine(DatabaseDirectory, "aster-workstation.db");

    public static void EnsureCreated()
    {
        Directory.CreateDirectory(DatabaseDirectory);
        Directory.CreateDirectory(BackupDirectory);
        Directory.CreateDirectory(SettingsDirectory);
    }
}
