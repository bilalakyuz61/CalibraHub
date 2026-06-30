using System.Globalization;
using CalibraHub.Application.Abstractions.Services;
using CalibraHub.Domain.Entities;
using CalibraHub.Persistence.Database;
using CalibraHub.Persistence.Options;
using Microsoft.Data.SqlClient;

namespace CalibraHub.Persistence.Repositories;

/// <summary>
/// SQL Server impl — DocumentNumberRule lookup + DocumentNumberCounter increment + format.
///
/// Concurrent-safe sayaç:
///   1. SELECT current value WITH (UPDLOCK, HOLDLOCK)  — satırı lock
///   2. Yoksa INSERT (CounterStart - 1)
///   3. UPDATE +1, OUTPUT inserted.CurrentValue
///   4. Format = PREFIX + YEAR + MONTH + ZeroPad(value, CounterLength)
///
/// Tek transaction içinde — paralel insert'lerde garanti unique.
/// </summary>
public sealed class SqlDocumentNumberService : IDocumentNumberService
{
    private readonly SqlServerConnectionFactory _connectionFactory;
    private readonly string _ruleTable;
    private readonly string _counterTable;
    private readonly string _docTable;

    public SqlDocumentNumberService(
        SqlServerConnectionFactory connectionFactory,
        CalibraDatabaseOptions options)
    {
        _connectionFactory = connectionFactory;
        var schema = string.IsNullOrWhiteSpace(options.Schema) ? "dbo" : options.Schema.Trim();
        _ruleTable    = $"[{schema}].[DocumentNumberRule]";
        _counterTable = $"[{schema}].[DocumentNumberCounter]";
        _docTable     = $"[{schema}].[Document]";
    }

    public async Task<string?> GenerateNextAsync(DocumentNumberContext context, CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);

        // 1) Aday kuralları çek — belge tipi + (NULL veya eşleşen filtre) + tarih aralığı + aktif
        var rules = await LoadCandidateRulesAsync(conn, context, ct);
        if (rules.Count == 0) return null;

        // 2) En yüksek ağırlıklı (Tasarım Kuralı pattern: Cari=16 + Grup=8 + Kullanıcı=4 + Şube=2 + Tarih=1)
        var winner = rules
            .Select(r => new { Rule = r, Weight = ComputeWeight(r, context) })
            .OrderByDescending(x => x.Weight)
            .ThenBy(x => x.Rule.Id)
            .First().Rule;

        // 3) Sayaç state'ini increment et (transaction + UPDLOCK)
        var resetKey = ComputeResetKey(winner.ResetPeriod, context.IssueDate);
        var nextValue = await IncrementCounterAsync(conn, winner, resetKey, context.IssueDate, ct);

        // 4) Format
        return FormatNumber(winner, context.IssueDate, nextValue);
    }

    // ── Kural lookup ────────────────────────────────────────────────────────

    private async Task<List<DocumentNumberRule>> LoadCandidateRulesAsync(
        SqlConnection conn, DocumentNumberContext ctx, CancellationToken ct)
    {
        var list = new List<DocumentNumberRule>();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            SELECT [Id],[Name],[DocumentTypeId],
                   [ContactId],[ContactGroupId],[UserId],[BranchId],[FromDate],[ToDate],
                   [Prefix],[YearFormat],[MonthFormat],[CounterLength],[CounterStart],
                   [ResetPeriod],[TotalLength],[Weight],[IsActive]
            FROM {_ruleTable}
            WHERE [IsActive] = 1
              AND [DocumentTypeId] = @DocumentTypeId
              AND ([ContactId]      IS NULL OR [ContactId]      = @ContactId)
              AND ([ContactGroupId] IS NULL OR [ContactGroupId] = @ContactGroupId)
              AND ([UserId]         IS NULL OR [UserId]         = @UserId)
              AND ([BranchId]       IS NULL OR [BranchId]       = @BranchId)
              AND ([FromDate]       IS NULL OR [FromDate] <= @IssueDate)
              AND ([ToDate]         IS NULL OR [ToDate]   >= @IssueDate);
            """;
        cmd.Parameters.Add(new SqlParameter("@DocumentTypeId", ctx.DocumentTypeId));
        cmd.Parameters.Add(new SqlParameter("@ContactId",      (object?)ctx.ContactId      ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@ContactGroupId", (object?)ctx.ContactGroupId ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@UserId",         (object?)ctx.UserId         ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@BranchId",       (object?)ctx.BranchId       ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@IssueDate",      ctx.IssueDate));

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            list.Add(new DocumentNumberRule
            {
                Id              = reader.GetInt32(0),
                Name            = reader.GetString(1),
                DocumentTypeId  = reader.GetInt32(2),
                ContactId       = reader.IsDBNull(3) ? null : reader.GetInt32(3),
                ContactGroupId  = reader.IsDBNull(4) ? null : reader.GetInt32(4),
                UserId          = reader.IsDBNull(5) ? null : reader.GetInt32(5),
                BranchId        = reader.IsDBNull(6) ? null : reader.GetInt32(6),
                FromDate        = reader.IsDBNull(7) ? null : reader.GetDateTime(7),
                ToDate          = reader.IsDBNull(8) ? null : reader.GetDateTime(8),
                Prefix          = reader.IsDBNull(9) ? null : reader.GetString(9),
                YearFormat      = reader.IsDBNull(10) ? null : reader.GetString(10),
                MonthFormat     = reader.IsDBNull(11) ? null : reader.GetString(11),
                CounterLength   = reader.GetInt32(12),
                CounterStart    = reader.GetInt32(13),
                ResetPeriod     = (DocumentNumberResetPeriod)reader.GetInt32(14),
                TotalLength     = reader.IsDBNull(15) ? null : reader.GetInt32(15),
                Weight          = reader.GetInt32(16),
                IsActive        = reader.GetBoolean(17),
            });
        }
        return list;
    }

    // ── Ağırlık (Tasarım Kuralı pattern: 2^n + 1 = 16/8/4/2/1) ──────────────
    private static int ComputeWeight(DocumentNumberRule r, DocumentNumberContext ctx)
    {
        int w = 0;
        if (r.ContactId      == ctx.ContactId      && r.ContactId      != null) w += 16;
        if (r.ContactGroupId == ctx.ContactGroupId && r.ContactGroupId != null) w += 8;
        if (r.UserId         == ctx.UserId         && r.UserId         != null) w += 4;
        if (r.BranchId       == ctx.BranchId       && r.BranchId       != null) w += 2;
        if (r.FromDate.HasValue || r.ToDate.HasValue)                           w += 1;
        return w;
    }

    // ── ResetKey hesabı ─────────────────────────────────────────────────────
    private static string ComputeResetKey(DocumentNumberResetPeriod period, DateTime date) => period switch
    {
        DocumentNumberResetPeriod.Yearly  => date.ToString("yyyy", CultureInfo.InvariantCulture),
        DocumentNumberResetPeriod.Monthly => date.ToString("yyyy-MM", CultureInfo.InvariantCulture),
        DocumentNumberResetPeriod.Daily   => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        _                                 => string.Empty,   // None → tek satır
    };

    // ── Sayaç increment (transaction + lock) ────────────────────────────────
    private async Task<long> IncrementCounterAsync(
        SqlConnection conn, DocumentNumberRule rule, string resetKey, DateTime date, CancellationToken ct)
    {
        await using var tx = (SqlTransaction)await conn.BeginTransactionAsync(ct);
        try
        {
            // 1) Lock + select (UPDLOCK + HOLDLOCK = serializable for this row)
            long current;
            await using (var sel = conn.CreateCommand())
            {
                sel.Transaction = tx;
                sel.CommandText = $"""
                    SELECT [CurrentValue]
                    FROM {_counterTable} WITH (UPDLOCK, HOLDLOCK)
                    WHERE [RuleId] = @RuleId AND [ResetKey] = @ResetKey;
                    """;
                sel.Parameters.Add(new SqlParameter("@RuleId", rule.Id));
                sel.Parameters.Add(new SqlParameter("@ResetKey", resetKey));
                var raw = await sel.ExecuteScalarAsync(ct);
                if (raw is null || raw is DBNull)
                {
                    // Sayaç yok — Document tablosundaki max numarayla başla (counter reset sonrası
                    // çakışmayı önler). CounterStart - 1 ile bu değerin büyüğü kullanılır.
                    var existingMax = await ReadMaxExistingCounterAsync(conn, tx, rule, date, ct);
                    current = Math.Max((long)(rule.CounterStart - 1), existingMax);

                    await using var ins = conn.CreateCommand();
                    ins.Transaction = tx;
                    ins.CommandText = $"""
                        INSERT INTO {_counterTable} ([RuleId], [ResetKey], [CurrentValue])
                        VALUES (@RuleId, @ResetKey, @StartValue);
                        """;
                    ins.Parameters.Add(new SqlParameter("@RuleId", rule.Id));
                    ins.Parameters.Add(new SqlParameter("@ResetKey", resetKey));
                    ins.Parameters.Add(new SqlParameter("@StartValue", current));
                    await ins.ExecuteNonQueryAsync(ct);
                }
                else
                {
                    current = Convert.ToInt64(raw);
                }
            }

            // 2) Increment; üretilen numara zaten Document tablosunda varsa geç (desync kurtarma)
            var next = current + 1;
            while (await DocumentNumberExistsAsync(conn, tx, rule, date, next, ct))
                next++;

            await using (var upd = conn.CreateCommand())
            {
                upd.Transaction = tx;
                upd.CommandText = $"""
                    UPDATE {_counterTable}
                    SET [CurrentValue] = @Next, [LastUpdated] = SYSUTCDATETIME()
                    WHERE [RuleId] = @RuleId AND [ResetKey] = @ResetKey;
                    """;
                upd.Parameters.Add(new SqlParameter("@Next", next));
                upd.Parameters.Add(new SqlParameter("@RuleId", rule.Id));
                upd.Parameters.Add(new SqlParameter("@ResetKey", resetKey));
                await upd.ExecuteNonQueryAsync(ct);
            }

            await tx.CommitAsync(ct);
            return next;
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }
    }

    // ── Numara çakışma kontrolü (desync kurtarma) ──────────────────────────
    private async Task<bool> DocumentNumberExistsAsync(
        SqlConnection conn, SqlTransaction tx, DocumentNumberRule rule, DateTime date, long counter, CancellationToken ct)
    {
        var docNumber = FormatNumber(rule, date, counter);
        await using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = $"""
            SELECT COUNT(1) FROM {_docTable}
            WHERE [DocumentNumber] = @DocNumber;
            """;
        cmd.Parameters.Add(new SqlParameter("@DocNumber", docNumber));
        var raw = await cmd.ExecuteScalarAsync(ct);
        return raw is not null && Convert.ToInt32(raw) > 0;
    }

    // ── Mevcut max sayaç okuma (counter sıfırdan başlarken desync önleme) ──
    private async Task<long> ReadMaxExistingCounterAsync(
        SqlConnection conn, SqlTransaction tx, DocumentNumberRule rule, DateTime date, CancellationToken ct)
    {
        // Prefix (prefix + yıl + ay) oluştur — sayaç kısmını hariç tut
        var sb = new System.Text.StringBuilder();
        if (!string.IsNullOrEmpty(rule.Prefix))      sb.Append(rule.Prefix);
        if (!string.IsNullOrEmpty(rule.YearFormat))  sb.Append(date.ToString(rule.YearFormat, CultureInfo.InvariantCulture));
        if (!string.IsNullOrEmpty(rule.MonthFormat)) sb.Append(date.ToString(rule.MonthFormat, CultureInfo.InvariantCulture));
        var prefix = sb.ToString();

        // prefix boşsa güvenli biçimde çık
        if (string.IsNullOrEmpty(prefix)) return 0;

        await using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = $"""
            SELECT TOP 1 [DocumentNumber]
            FROM {_docTable}
            WHERE [DocumentNumber] LIKE @Prefix + '%'
            ORDER BY LEN([DocumentNumber]) DESC, [DocumentNumber] DESC;
            """;
        cmd.Parameters.Add(new SqlParameter("@Prefix", prefix));
        var raw = await cmd.ExecuteScalarAsync(ct);
        if (raw is null || raw is DBNull) return 0;

        var docNumber = raw.ToString()!;
        if (docNumber.Length <= prefix.Length) return 0;
        var suffix = docNumber[prefix.Length..];
        return long.TryParse(suffix, out var parsed) ? parsed : 0;
    }

    // ── Format ──────────────────────────────────────────────────────────────
    private static string FormatNumber(DocumentNumberRule rule, DateTime date, long counter)
    {
        var sb = new System.Text.StringBuilder();
        if (!string.IsNullOrEmpty(rule.Prefix))      sb.Append(rule.Prefix);
        if (!string.IsNullOrEmpty(rule.YearFormat))  sb.Append(date.ToString(rule.YearFormat, CultureInfo.InvariantCulture));
        if (!string.IsNullOrEmpty(rule.MonthFormat)) sb.Append(date.ToString(rule.MonthFormat, CultureInfo.InvariantCulture));

        // Counter zero-padded
        var counterStr = counter.ToString(CultureInfo.InvariantCulture)
            .PadLeft(Math.Max(1, rule.CounterLength), '0');
        sb.Append(counterStr);

        var result = sb.ToString();

        // TotalLength override — eksikse 0 ile padle, fazlaysa orta-keserek (counter alanını) küçült
        if (rule.TotalLength.HasValue && rule.TotalLength.Value > 0 && result.Length < rule.TotalLength.Value)
        {
            // Sayaç hane sayısını TotalLength'e ulaşacak kadar arttır (önek 0 ile)
            var needed = rule.TotalLength.Value - result.Length;
            sb.Clear();
            if (!string.IsNullOrEmpty(rule.Prefix))      sb.Append(rule.Prefix);
            if (!string.IsNullOrEmpty(rule.YearFormat))  sb.Append(date.ToString(rule.YearFormat, CultureInfo.InvariantCulture));
            if (!string.IsNullOrEmpty(rule.MonthFormat)) sb.Append(date.ToString(rule.MonthFormat, CultureInfo.InvariantCulture));
            sb.Append(counter.ToString(CultureInfo.InvariantCulture)
                .PadLeft(rule.CounterLength + needed, '0'));
            result = sb.ToString();
        }

        return result;
    }
}
