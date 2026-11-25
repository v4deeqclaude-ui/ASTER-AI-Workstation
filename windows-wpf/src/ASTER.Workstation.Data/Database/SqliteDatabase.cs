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
        await using var command = connection.CreateCommand();
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
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
