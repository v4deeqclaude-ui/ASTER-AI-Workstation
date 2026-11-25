using ASTER.Workstation.Core.Interfaces;
using ASTER.Workstation.Core.Models;
using ASTER.Workstation.Data.Database;
using Microsoft.Data.Sqlite;

namespace ASTER.Workstation.Data.Repositories;

public sealed class ProjectRepository(SqliteDatabase database) : IProjectRepository
{
    public Task InitializeAsync(CancellationToken cancellationToken = default) => database.InitializeAsync(cancellationToken);

    public async Task<IReadOnlyList<ProjectEntry>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var projects = new List<ProjectEntry>();
        await using var connection = database.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT Id, Name, RootPath, AddedUtc, LastOpenedUtc FROM Projects ORDER BY LastOpenedUtc DESC;";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            projects.Add(new ProjectEntry
            {
                Id = reader.GetString(0),
                Name = reader.GetString(1),
                RootPath = reader.GetString(2),
                AddedUtc = DateTime.Parse(reader.GetString(3), null, System.Globalization.DateTimeStyles.RoundtripKind),
                LastOpenedUtc = DateTime.Parse(reader.GetString(4), null, System.Globalization.DateTimeStyles.RoundtripKind)
            });
        }
        return projects;
    }

    public async Task AddAsync(ProjectEntry project, CancellationToken cancellationToken = default)
    {
        await using var connection = database.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO Projects (Id, Name, RootPath, AddedUtc, LastOpenedUtc) VALUES ($id, $name, $path, $added, $opened);";
        command.Parameters.AddWithValue("$id", project.Id);
        command.Parameters.AddWithValue("$name", project.Name);
        command.Parameters.AddWithValue("$path", project.RootPath);
        command.Parameters.AddWithValue("$added", project.AddedUtc.ToString("O"));
        command.Parameters.AddWithValue("$opened", project.LastOpenedUtc.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        await using var connection = database.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM Projects WHERE Id = $id;";
        command.Parameters.AddWithValue("$id", id);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task UpdateLastOpenedAsync(string id, CancellationToken cancellationToken = default)
    {
        await using var connection = database.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "UPDATE Projects SET LastOpenedUtc = $opened WHERE Id = $id;";
        command.Parameters.AddWithValue("$opened", DateTime.UtcNow.ToString("O"));
        command.Parameters.AddWithValue("$id", id);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
