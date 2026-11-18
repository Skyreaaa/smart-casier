using Microsoft.Data.Sqlite;
using System.Reflection;

namespace SmartCasier.Core;

/// <summary>SQLite local: satu database operasional; dataset staging terpisah.</summary>
public sealed class PosDatabase
{
    private readonly string _connectionString;

    public PosDatabase(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            throw new ArgumentException("Database path is required.", nameof(filePath));
        var fullPath = Path.GetFullPath(filePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = fullPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = false
        }.ToString();
    }

    public SqliteConnection Open()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA foreign_keys=ON; PRAGMA busy_timeout=5000;";
        command.ExecuteNonQuery();
        return connection;
    }

    public void Initialize()
    {
        using var connection = Open();
        using (var pragma = connection.CreateCommand())
        {
            pragma.CommandText = "PRAGMA journal_mode=WAL;";
            pragma.ExecuteNonQuery();
        }
        var version = GetVersion(connection);
        if (version is not (0 or 1 or 3))
            throw new InvalidOperationException(
                "Unsupported operational database version: " + version +
                ". Staging schema version 2 belongs in a separate file.");
        if (version == 0) RunEmbeddedMigration(connection, "001_core.sql");
        if (GetVersion(connection) == 1)
            RunEmbeddedMigration(connection, "003_pos_sales.sql");
        if (GetVersion(connection) != 3)
            throw new InvalidOperationException("Operational database migration failed.");
    }

    private static int GetVersion(SqliteConnection conn)
    {
        using var command = conn.CreateCommand();
        command.CommandText = "PRAGMA user_version;";
        return Convert.ToInt32(command.ExecuteScalar());
    }

    private static void RunEmbeddedMigration(SqliteConnection conn, string name)
    {
        var resourceName = "SmartCasier.Core.Migrations." + name;
        using var stream = typeof(PosDatabase).Assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException("Embedded migration not found: " + resourceName);
        using var reader = new StreamReader(stream);
        using var command = conn.CreateCommand();
        command.CommandText = reader.ReadToEnd();
        command.ExecuteNonQuery();
    }
}
