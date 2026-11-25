namespace ASTER.Workstation.Core.Models;

public sealed class ProjectEntry
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "Новый проект";
    public string RootPath { get; set; } = string.Empty;
    public DateTime AddedUtc { get; set; } = DateTime.UtcNow;
    public DateTime LastOpenedUtc { get; set; } = DateTime.UtcNow;

    public override string ToString() => Name;
}
