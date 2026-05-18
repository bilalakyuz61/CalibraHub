using Microsoft.Data.SqlClient;

namespace CalibraHub.Persistence.Database;

/// <summary>
/// __SchemaVersion tablosu yardımcısı — yapısal migration calismalarini
/// kayit altina alip resume guvenligi saglar. Rapor §2.8 cozumu.
///
/// Mevcut migration'lar zaten idempotent (her ALTER "IF NOT EXISTS" ile sarili)
/// oldugundan bu tracker zorunlu degildir; yeni eklenen migration'lar bu helper
/// uzerinden kayit tutarak su faydalari kazanir:
///   1. Resume guvenligi — yarim kalan migration'lar yeniden calistirilmaz
///   2. Operasyonel gorunurluk — SELECT * FROM __SchemaVersion ile DB durumu
///   3. Audit log — kim ne zaman calistirdi
///
/// Tablo schema (her per-company DB'de bir tane):
///   __SchemaVersion (Id INT IDENTITY PK, Name NVARCHAR(160) UNIQUE,
///                    AppliedUtc DATETIME2, AppliedBy NVARCHAR(120))
///
/// Tipik kullanim (yeni migration metodu icinde):
///   var tracker = new MigrationVersionTracker(schema);
///   if (!await tracker.ShouldRunAsync(connection, "AddDocumentCommentColumn_2026_05", ct))
///       return; // zaten uygulanmis
///   await using (var cmd = connection.CreateCommand()) {
///       cmd.CommandText = "ALTER TABLE Document ADD Comment NVARCHAR(MAX) NULL;";
///       await cmd.ExecuteNonQueryAsync(ct);
///   }
///   await tracker.MarkAppliedAsync(connection, "AddDocumentCommentColumn_2026_05", "system", ct);
/// </summary>
public sealed class MigrationVersionTracker
{
    private readonly string _schema;

    public MigrationVersionTracker(string schema)
    {
        _schema = string.IsNullOrWhiteSpace(schema) ? "dbo" : schema.Trim();
    }

    /// <summary>
    /// Tablo yoksa olusturur. Idempotent — initializer'in en basinda bir kere cagrilir.
    /// </summary>
    public async Task EnsureTableAsync(SqlConnection connection, CancellationToken ct)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = $@"
            IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = '__SchemaVersion' AND schema_id = SCHEMA_ID(N'{_schema}'))
            BEGIN
                CREATE TABLE [{_schema}].[__SchemaVersion] (
                    [Id]          INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_SchemaVersion PRIMARY KEY,
                    [Name]        NVARCHAR(160)    NOT NULL,
                    [AppliedUtc]  DATETIME2        NOT NULL CONSTRAINT DF_SchemaVersion_AppliedUtc DEFAULT SYSUTCDATETIME(),
                    [AppliedBy]   NVARCHAR(120)    NULL
                );
                CREATE UNIQUE INDEX UX_SchemaVersion_Name ON [{_schema}].[__SchemaVersion]([Name]);
            END;
        ";
        await cmd.ExecuteNonQueryAsync(ct);
    }

    /// <summary>
    /// Bu migration daha once basariyla uygulandi mi? Idempotency check.
    /// Tablo yoksa false doner (ilk run'da migration calismali).
    /// </summary>
    public async Task<bool> HasAppliedAsync(SqlConnection connection, string migrationName, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(migrationName))
            throw new ArgumentException("migrationName bos olamaz.", nameof(migrationName));

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = $@"
            IF OBJECT_ID(N'[{_schema}].[__SchemaVersion]', N'U') IS NULL
                SELECT 0;
            ELSE
                SELECT CASE WHEN EXISTS (
                    SELECT 1 FROM [{_schema}].[__SchemaVersion] WHERE [Name] = @Name)
                THEN 1 ELSE 0 END;
        ";
        cmd.Parameters.Add(new SqlParameter("@Name", migrationName));
        var result = (int)(await cmd.ExecuteScalarAsync(ct) ?? 0);
        return result == 1;
    }

    /// <summary>
    /// Inverse of HasAppliedAsync — sytax sugar: "if (await tracker.ShouldRunAsync(...))".
    /// </summary>
    public async Task<bool> ShouldRunAsync(SqlConnection connection, string migrationName, CancellationToken ct)
        => !await HasAppliedAsync(connection, migrationName, ct);

    /// <summary>
    /// Migration basariyla uygulandiktan SONRA cagrilmali. INSERT idempotent
    /// (UNIQUE INDEX ihlali olursa nazikçe yutulur — paralel run koruma).
    /// </summary>
    public async Task MarkAppliedAsync(SqlConnection connection, string migrationName, string? appliedBy, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(migrationName))
            throw new ArgumentException("migrationName bos olamaz.", nameof(migrationName));

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = $@"
            IF NOT EXISTS (SELECT 1 FROM [{_schema}].[__SchemaVersion] WHERE [Name] = @Name)
            BEGIN
                INSERT INTO [{_schema}].[__SchemaVersion] ([Name], [AppliedBy])
                VALUES (@Name, @By);
            END;
        ";
        cmd.Parameters.Add(new SqlParameter("@Name", migrationName));
        cmd.Parameters.Add(new SqlParameter("@By", (object?)appliedBy ?? DBNull.Value));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    /// <summary>
    /// Idempotent helper: shouldRun + action + mark as applied. Action exception fırlatırsa
    /// kayıt eklenmez (resume guvenligi — yarim kalan migration tekrar denenir).
    /// </summary>
    public async Task RunOnceAsync(
        SqlConnection connection,
        string migrationName,
        Func<SqlConnection, CancellationToken, Task> action,
        string? appliedBy,
        CancellationToken ct)
    {
        if (await HasAppliedAsync(connection, migrationName, ct)) return;
        await action(connection, ct);
        await MarkAppliedAsync(connection, migrationName, appliedBy, ct);
    }
}
