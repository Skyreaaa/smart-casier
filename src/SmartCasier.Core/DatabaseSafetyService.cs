using Microsoft.Data.Sqlite;
using System.Security.Cryptography;

namespace SmartCasier.Core;

public sealed record BackupFileInfo(string FilePath, long SizeBytes, string Sha256);

/// <summary>
/// Backup SQLite online aman untuk WAL. Restore hanya membuat FILE BARU yang tervalidasi;
/// layanan ini TIDAK pernah menimpa database operasional yang sedang aktif.
/// </summary>
public sealed class DatabaseSafetyService(PosDatabase database)
{
    private static string Full(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("Path database tidak boleh kosong.", nameof(path));
        return Path.GetFullPath(path);
    }

    private static void RequireDifferent(string source, string destination)
    {
        if (string.Equals(Full(source), Full(destination),
            OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            throw new InvalidOperationException("Sumber dan tujuan harus berbeda.");
    }

    private static string DatabasePath(SqliteConnection connection)
        => new SqliteConnectionStringBuilder(connection.ConnectionString).DataSource;

    private static SqliteConnection OpenFile(string path, SqliteOpenMode mode)
    {
        var conn = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path, Mode = mode, Pooling = false
        }.ToString());
        conn.Open();
        return conn;
    }

    /// <summary>Memeriksa tipe skema, integritas SQLite, dan foreign key tanpa mengubah data.</summary>
    public static void ValidateBackup(string filePath)
    {
        var path = Full(filePath);
        if (!File.Exists(path))
            throw new FileNotFoundException("File backup tidak ditemukan.", path);
        using var conn = OpenFile(path, SqliteOpenMode.ReadOnly);
        ValidateConnection(conn);
    }

    private static void ValidateConnection(SqliteConnection conn)
    {
        static string Scalar(SqliteConnection connection, string sql)
        {
            using var cmd = connection.CreateCommand();
            cmd.CommandText = sql;
            return Convert.ToString(cmd.ExecuteScalar()) ?? "";
        }
        if (Scalar(conn, "PRAGMA quick_check;") != "ok")
            throw new InvalidDataException("Backup gagal pemeriksaan integritas SQLite.");
        if (Scalar(conn, "PRAGMA user_version;") is not ("3" or "4" or "5"))
            throw new InvalidDataException(
                "Skema tidak sesuai Smart Casier POS v3. Staging DB tidak dapat dipulihkan.");
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "PRAGMA foreign_key_check;";
            using var reader = cmd.ExecuteReader();
            if (reader.Read())
                throw new InvalidDataException("Backup memiliki foreign key yang tidak valid.");
        }
        // A SQLite file with user_version=3 alone is NOT sufficient.
        foreach (var table in new[] { "sales", "sale_lines", "variants", "stock_movements" })
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT count(*) FROM sqlite_master WHERE type='table' AND name=@table;";
            cmd.Parameters.AddWithValue("@table", table);
            if (Convert.ToInt64(cmd.ExecuteScalar()) != 1L)
                throw new InvalidDataException("Tabel POS wajib tidak ditemukan: " + table);
        }
    }

    /// <summary>Backup online ke file tujuan BARU; WAL aktif tetap konsisten.</summary>
    public BackupFileInfo BackupToNewFile(string destinationPath, AuthSession admin)
    {
        new AuthService(database).RequireAdmin(admin);
        var destination = Full(destinationPath);
        using var source = database.Open();
        RequireDifferent(DatabasePath(source), destination);
        if (File.Exists(destination))
            throw new IOException("File tujuan sudah ada; backup tidak boleh menimpa file lama.");
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        bool createdByUs = false;
        try
        {
            // Atomic CreateNew removes the check-then-create overwrite race.
            using (new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { }
            createdByUs = true;
            using (var target = OpenFile(destination, SqliteOpenMode.ReadWriteCreate))
            {
                source.BackupDatabase(target);
                ValidateConnection(target);
            }
            return Info(destination);
        }
        catch
        {
            if (createdByUs) DeleteIncomplete(destination);
            throw;
        }
    }

    /// <summary>
    /// Pulihkan snapshot ke file BARU dan uji integritasnya. DB kasir saat ini tidak diubah.
    /// File hasil dapat digunakan pada sesi berikut setelah prosedur pengalihan database.
    /// </summary>
    public BackupFileInfo RestoreToNewFile(string backupPath, string destinationPath, AuthSession admin)
    {
        new AuthService(database).RequireAdmin(admin);
        var backup = Full(backupPath);
        var destination = Full(destinationPath);
        RequireDifferent(backup, destination);
        using (var live = database.Open())
            RequireDifferent(DatabasePath(live), destination);
        ValidateBackup(backup);
        if (File.Exists(destination))
            throw new IOException("Tujuan restore sudah ada; database lama tidak boleh ditimpa.");
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        bool createdByUs = false;
        try
        {
            // Claim the NEW destination atomically. Never open an existing file for write.
            using (new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { }
            createdByUs = true;
            using (var source = OpenFile(backup, SqliteOpenMode.ReadOnly))
            using (var target = OpenFile(destination, SqliteOpenMode.ReadWriteCreate))
            {
                source.BackupDatabase(target);
                ValidateConnection(target);
            }
            return Info(destination);
        }
        catch
        {
            if (createdByUs) DeleteIncomplete(destination);
            throw;
        }
    }

    private static BackupFileInfo Info(string path)
    {
        using var stream = File.OpenRead(path);
        var hash = Convert.ToHexString(SHA256.HashData(stream));
        return new BackupFileInfo(path, stream.Length, hash);
    }

    private static void DeleteIncomplete(string path)
    {
        // No operational file is ever deleted here. Only newly-created failed targets.
        foreach (var name in new[] { path, path + "-wal", path + "-shm" })
        {
            try { if (File.Exists(name)) File.Delete(name); }
            catch (IOException) { /* preserve root failure */ }
            catch (UnauthorizedAccessException) { /* preserve root failure */ }
        }
    }
}
