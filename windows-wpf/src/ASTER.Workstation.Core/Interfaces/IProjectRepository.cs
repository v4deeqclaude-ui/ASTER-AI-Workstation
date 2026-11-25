using ASTER.Workstation.Core.Models;

namespace ASTER.Workstation.Core.Interfaces;

public interface IProjectRepository
{
    Task InitializeAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ProjectEntry>> GetAllAsync(CancellationToken cancellationToken = default);
    Task AddAsync(ProjectEntry project, CancellationToken cancellationToken = default);
    Task DeleteAsync(string id, CancellationToken cancellationToken = default);
    Task UpdateLastOpenedAsync(string id, CancellationToken cancellationToken = default);
}
