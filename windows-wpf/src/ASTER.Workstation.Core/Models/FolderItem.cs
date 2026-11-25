namespace ASTER.Workstation.Core.Models;

public sealed class FolderItem
{
    public string Name { get; set; } = string.Empty;
    public string FullPath { get; set; } = string.Empty;
    public bool IsDirectory { get; set; }
    public string Kind => IsDirectory ? "Папка" : "Файл";
}
