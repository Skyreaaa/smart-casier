using Microsoft.Data.Sqlite;
using System.Security;
using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace SmartCasier.Core;

/// <summary>
/// Session exists only in process memory. Its constructor is not public.
/// It is not a defense against Windows administrators or direct SQLite file editing.
/// </summary>
public sealed class AuthSession
{
    internal AuthSession(string userId, string username, string role, string databasePath)
    {
        UserId = userId;
        Username = username;
        Role = role;
        DatabasePath = databasePath;
        IssuedUtc = DateTimeOffset.UtcNow;
    }
    public string UserId { get; }
    public string Username { get; }
    public string Role { get; }
    public DateTimeOffset IssuedUtc { get; }
    internal string DatabasePath { get; }
    public bool IsAdmin => Role == "ADMIN";
}

public sealed class AuthService(PosDatabase database)
{
    private const int Iterations = 310_000;
    private const int MaxFailed = 5;
    private static readonly TimeSpan LockDuration = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan SessionDuration = TimeSpan.FromHours(8);
    private static readonly Regex UsernamePattern = new(
        @"^[a-zA-Z][a-zA-Z0-9._-]{2,31}$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public bool HasUsers()
    {
        using var conn = database.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT EXISTS(SELECT 1 FROM app_users);";
        return Convert.ToInt64(cmd.ExecuteScalar()) == 1;
    }

    private static string CheckUsername(string username)
    {
        var normalized = (username ?? "").Trim().ToLowerInvariant();
        if (!UsernamePattern.IsMatch(normalized))
            throw new ArgumentException("Username: 3–32 karakter, awali huruf; gunakan huruf/angka/._-.");
        return normalized;
    }

    private static void CheckPassword(string password)
    {
        if (password is null || password.Length is < 12 or > 128)
            throw new ArgumentException("Password minimal 12 dan maksimal 128 karakter.");
    }

    private static byte[] Hash(string password, byte[] salt, int rounds)
        => Rfc2898DeriveBytes.Pbkdf2(password, salt, rounds,
            HashAlgorithmName.SHA256, 32);

    private static string NewUser(SqliteConnection connection, SqliteTransaction tx,
        string username, string password, string role)
    {
        var salt = RandomNumberGenerator.GetBytes(32);
        var hash = Hash(password, salt, Iterations);
        try
        {
            string id = Guid.NewGuid().ToString("N");
            using var cmd = connection.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = """
                INSERT INTO app_users(id,username,role,salt,password_hash,hash_iterations)
                VALUES(@id,@name,@role,@salt,@hash,@rounds);
                """;
            cmd.Parameters.AddWithValue("@id", id);
            cmd.Parameters.AddWithValue("@name", username);
            cmd.Parameters.AddWithValue("@role", role);
            cmd.Parameters.AddWithValue("@salt", salt);
            cmd.Parameters.AddWithValue("@hash", hash);
            cmd.Parameters.AddWithValue("@rounds", Iterations);
            cmd.ExecuteNonQuery();
            return id;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(hash);
        }
    }

    private static void Audit(SqliteConnection conn, SqliteTransaction tx,
        string? actorId, string action, string? targetUser)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
            INSERT INTO security_audit(actor_id, action, target_user)
            VALUES(@actor,@action,@target);
            """;
        cmd.Parameters.AddWithValue("@actor", (object?)actorId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@action", action);
        cmd.Parameters.AddWithValue("@target", (object?)targetUser ?? DBNull.Value);
        cmd.ExecuteNonQuery();
    }

    /// <summary>First-run bootstrap only. No default credentials are shipped.</summary>
    public void CreateFirstAdmin(string username, string password)
    {
        var name = CheckUsername(username);
        CheckPassword(password);
        using var conn = database.Open();
        using var tx = conn.BeginTransaction();
        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = "SELECT COUNT(*) FROM app_users;";
            if (Convert.ToInt64(cmd.ExecuteScalar()) != 0)
                throw new SecurityException("Akun pertama sudah dibuat; setup admin ditutup.");
        }
        var id = NewUser(conn, tx, name, password, "ADMIN");
        Audit(conn, tx, id, "BOOTSTRAP_ADMIN", name);
        tx.Commit();
    }

    public AuthSession Login(string username, string password)
    {
        // Failed credentials must not reveal password, hash, salt or account existence.
        var name = CheckUsername(username);
        if (password is null) throw new SecurityException("Login gagal.");
        using var conn = database.Open();
        using var tx = conn.BeginTransaction();
        string? id = null, role = null, lockedUntil = null;
        byte[]? salt = null, expected = null;
        int rounds = Iterations, attempts = 0;
        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = """
                SELECT id,role,salt,password_hash,hash_iterations,failed_attempts,
                       lock_until_utc FROM app_users
                WHERE username=@name COLLATE NOCASE AND active=1;
                """;
            cmd.Parameters.AddWithValue("@name", name);
            using var row = cmd.ExecuteReader();
            if (row.Read())
            {
                id = row.GetString(0);
                role = row.GetString(1);
                salt = (byte[])row[2];
                expected = (byte[])row[3];
                rounds = row.GetInt32(4);
                attempts = row.GetInt32(5);
                lockedUntil = row.IsDBNull(6) ? null : row.GetString(6);
            }
        }

        if (lockedUntil is not null &&
            DateTimeOffset.TryParse(lockedUntil, out var end) &&
            end > DateTimeOffset.UtcNow)
            throw new SecurityException("Akun terkunci sementara. Coba kembali nanti.");

        var candidateSalt = salt ?? new byte[32];
        byte[] actual = Hash(password, candidateSalt, rounds);
        bool success;
        try
        {
            success = expected is not null &&
                CryptographicOperations.FixedTimeEquals(actual, expected);
        }
        finally { CryptographicOperations.ZeroMemory(actual); }

        if (!success || id is null || role is null)
        {
            if (id is not null)
            {
                using var failCmd = conn.CreateCommand();
                failCmd.Transaction = tx;
                failCmd.CommandText = """
                    UPDATE app_users
                    SET failed_attempts=@attempts, lock_until_utc=@lock
                    WHERE id=@id;
                    """;
                failCmd.Parameters.AddWithValue("@id", id);
                failCmd.Parameters.AddWithValue("@attempts", attempts + 1);
                failCmd.Parameters.AddWithValue("@lock",
                    attempts + 1 >= MaxFailed
                        ? DateTimeOffset.UtcNow.Add(LockDuration).ToString("O")
                        : (object?)DBNull.Value);
                failCmd.ExecuteNonQuery();
                Audit(conn, tx, id, "LOGIN_FAILED", name);
            }
            tx.Commit();
            throw new SecurityException("Username atau password salah.");
        }

        using (var successCmd = conn.CreateCommand())
        {
            successCmd.Transaction = tx;
            successCmd.CommandText = """
                UPDATE app_users
                SET failed_attempts=0,lock_until_utc=NULL
                WHERE id=@id;
                """;
            successCmd.Parameters.AddWithValue("@id", id);
            successCmd.ExecuteNonQuery();
        }
        Audit(conn, tx, id, "LOGIN", name);
        tx.Commit();
        return new AuthSession(id, name, role, database.FilePath);
    }

    /// <summary>Operator must be active, same role/database and within session lifetime.</summary>
    public void RequireOperator(AuthSession? session)
    {
        using var connection = database.Open();
        RequireOperator(session, connection, null);
    }

    internal void RequireOperator(
        AuthSession? session, SqliteConnection connection, SqliteTransaction? transaction)
    {
        if (session is null || (session.Role != "ADMIN" && session.Role != "CASHIER"))
            throw new SecurityException("Aksi ini membutuhkan login operator.");
        if (session.IssuedUtc > DateTimeOffset.UtcNow.AddMinutes(1) ||
            DateTimeOffset.UtcNow - session.IssuedUtc >= SessionDuration)
            throw new SecurityException("Sesi operator berakhir. Silakan login ulang.");
        if (!string.Equals(session.DatabasePath, database.FilePath,
                OperatingSystem.IsWindows()
                    ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            throw new SecurityException("Sesi operator tidak berlaku untuk database ini.");
        using var cmd = connection.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = """
            SELECT role FROM app_users WHERE id=@user AND active=1;
            """;
        cmd.Parameters.AddWithValue("@user", session.UserId);
        var currentRole = cmd.ExecuteScalar() as string;
        if (currentRole is null ||
            !string.Equals(currentRole, session.Role, StringComparison.Ordinal))
            throw new SecurityException("Hak akses operator sudah tidak berlaku.");
    }

    /// <summary>Only active Admin on this database can perform privileged mutation.</summary>
    public void RequireAdmin(AuthSession? session)
    {
        RequireOperator(session);
        if (session?.IsAdmin != true)
            throw new SecurityException("Aksi ini membutuhkan login Admin.");
    }

    public string CreateCashier(AuthSession admin, string username, string password)
    {
        RequireAdmin(admin);
        var name = CheckUsername(username);
        CheckPassword(password);
        using var conn = database.Open();
        using var tx = conn.BeginTransaction();
        var id = NewUser(conn, tx, name, password, "CASHIER");
        Audit(conn, tx, admin.UserId, "CREATE_CASHIER", name);
        tx.Commit();
        return id;
    }
}
