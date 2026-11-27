using ASTER.Workstation.Core.Services;
using Microsoft.Data.Sqlite;

namespace ASTER.Workstation.Data.Database;

public sealed class SqliteDatabase
{
    public SqliteConnection CreateConnection()
    {
        AppPaths.EnsureCreated();
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = AppPaths.DatabaseFile,
            ForeignKeys = true,
            Mode = SqliteOpenMode.ReadWriteCreate
        };
        return new SqliteConnection(builder.ToString());
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                PRAGMA journal_mode=WAL;
                CREATE TABLE IF NOT EXISTS Projects (
                    Id TEXT NOT NULL PRIMARY KEY,
                    Name TEXT NOT NULL,
                    RootPath TEXT NOT NULL UNIQUE,
                    AddedUtc TEXT NOT NULL,
                    LastOpenedUtc TEXT NOT NULL
                );
                CREATE TABLE IF NOT EXISTS AppSettings (
                    [Key] TEXT NOT NULL PRIMARY KEY,
                    [Value] TEXT NOT NULL
                );
                CREATE TABLE IF NOT EXISTS AiMessages (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    Role TEXT NOT NULL,
                    Content TEXT NOT NULL,
                    CreatedUtc TEXT NOT NULL,
                    Workspace TEXT NOT NULL DEFAULT 'CHAT'
                );
                """;
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        var hasWorkspaceColumn = false;
        await using (var inspect = connection.CreateCommand())
        {
            inspect.CommandText = "PRAGMA table_info(AiMessages);";
            await using var reader = await inspect.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                if (string.Equals(reader.GetString(1), "Workspace", StringComparison.OrdinalIgnoreCase))
                {
                    hasWorkspaceColumn = true;
                    break;
                }
            }
        }

        if (!hasWorkspaceColumn)
        {
            await using var migrate = connection.CreateCommand();
            migrate.CommandText = "ALTER TABLE AiMessages ADD COLUMN Workspace TEXT NOT NULL DEFAULT 'CHAT';";
            await migrate.ExecuteNonQueryAsync(cancellationToken);
        }
    }
}