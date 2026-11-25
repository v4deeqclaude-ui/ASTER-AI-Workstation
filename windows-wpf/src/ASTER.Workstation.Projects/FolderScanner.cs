using ASTER.Workstation.Core.Models;

namespace ASTER.Workstation.Projects;

public sealed class FolderScanner
{
    public IReadOnlyList<FolderItem> GetChildren(string directoryPath)
    {
        if (!Directory.Exists(directoryPath)) return Array.Empty<FolderItem>();
        var items = new List<FolderItem>();
        try
        {
            foreach (var directory in Directory.EnumerateDirectories(directoryPath)
                         .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase))
                items.Add(new FolderItem { Name = Path.GetFileName(directory), FullPath = directory, IsDirectory = true });
            foreach (var file in Directory.EnumerateFiles(directoryPath)
                         .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase))
                items.Add(new FolderItem { Name = Path.GetFileName(file), FullPath = file, IsDirectory = false });
        }
        catch (UnauthorizedAccessException) { }
        catch (IOException) { }
        return items;
    }
}
