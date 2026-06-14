using System.Data;
using System.Text.RegularExpressions;
using CalibraHub.Application.Abstractions.DesignProvider;
using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Application.Contracts;
using CalibraHub.Persistence.Database;
using CalibraHub.Persistence.Options;
using Microsoft.Data.SqlClient;

namespace CalibraHub.Persistence.Repositories;

/// <summary>
/// DocLayoutRule tablosunu DI'dan gelen IDesignCriterion listesinden dinamik
/// inşa edilen sorgular ile sorgular. Yeni kriter eklemek bu sınıfı değiştirmez.
///
/// Güvenlik:
///   - Tüm kullanıcı değerleri SqlParameter olarak geçer (injection-safe).
///   - Kolon adları + parametre adları whitelist-kontrolünden geçer
///     (sadece harf/rakam/_, max 60 karakter). Dinamik SQL'in tek "dinamik"
///     parçası bu whitelist edilmiş identifier'lar.
///
/// Performans:
///   - DocLayoutRule küçük bir referans tablodur; hot read path.
///   - Sorgu WITH (READUNCOMMITTED) hint'i kullanır — dirty read riski kabul
///     edilir (kural değişikliği nadir, yazma sırasında basım engellenmesin).
/// </summary>
public sealed class SqlDocLayoutRuleRepository : IDocLayoutRuleRepository
{
    private readonly SqlServerConnectionFactory _factory;
    private readonly IReadOnlyList<IDesignCriterion> _criteria;
    private readonly string _ruleTable;
    private readonly string _layoutTable;

    // Identifier whitelist: SQL injection riskini sıfırlar — kolon/parametre
    // isimleri sadece harf, rakam ve alt çizgi içerebilir, max 60 karakter.
    private static readonly Regex SafeIdentifier =
        new(@"^[A-Za-z_][A-Za-z0-9_]{0,59}$", RegexOptions.Compiled);

    public SqlDocLayoutRuleRepository(
        SqlServerConnectionFactory factory,
        IEnumerable<IDesignCriterion> criteria,
        CalibraDatabaseOptions options)
    {
        _factory = factory;

        // Kriterleri Weight DESC sırala (debug/log okunabilirliği için);
        // sorgu sonucu sıralaması ORDER BY weight DESC ile garantilenir.
        _criteria = criteria.OrderByDescending(c => c.Weight).ToList();

        // Güvenlik kontrolü: registered criterions whitelist'e uymalı.
        foreach (var c in _criteria)
        {
            if (!SafeIdentifier.IsMatch(c.ColumnName))
                throw new InvalidOperationException(
                    $"IDesignCriterion ColumnName geçersiz: '{c.ColumnName}'. Sadece [A-Za-z_][A-Za-z0-9_]* izinli.");
            if (!c.ParameterName.StartsWith('@') || !SafeIdentifier.IsMatch(c.ParameterName[1..]))
                throw new InvalidOperationException(
                    $"IDesignCriterion ParameterName geçersiz: '{c.ParameterName}'. '@' + [A-Za-z_][A-Za-z0-9_]* olmalı.");
            if (c.Weight <= 0 || (c.Weight & (c.Weight - 1)) != 0)
                throw new InvalidOperationException(
                    $"IDesignCriterion '{c.ColumnName}' Weight 2^n olmalı (1,2,4,8,16,...), gelen: {c.Weight}.");
        }

        // Eşsiz ağırlık kontrolü — iki kriterin aynı 2^n değerini taşıması
        // sıralama eşitliğine ve belirsizliğe yol açar.
        var dupWeights = _criteria.GroupBy(c => c.Weight).Where(g => g.Count() > 1).ToList();
        if (dupWeights.Count > 0)
            throw new InvalidOperationException(
                "IDesignCriterion ağırlıkları eşsiz olmalı; tekrarlananlar: " +
                string.Join(", ", dupWeights.Select(g => $"Weight={g.Key} → [{string.Join(',', g.Select(c => c.ColumnName))}]")));

        var s = (string.IsNullOrWhiteSpace(options.Schema) ? "dbo" : options.Schema.Trim()).Replace("]", "]]");
        _ruleTable   = $"[{s}].[DocLayoutRule]";
        _layoutTable = $"[{s}].[DocLayout]";
    }

    public async Task<int?> FindBestMatchAsync(DesignSelectionContext ctx, CancellationToken ct)
    {
        if (ctx is null) throw new ArgumentNullException(nameof(ctx));

        // ── Dinamik WHERE: her kriter için ([Col] IS NULL OR [Col] = @Param)
        // Bu yapı, NULL olan rule rows (genel kurallar) ile dolu rows (özel
        // kurallar) arasından, bağlamla çelişmeyenlerin hepsini getirir.
        var whereClauses = _criteria.Select(c =>
            $"([{c.ColumnName}] IS NULL OR [{c.ColumnName}] = {c.ParameterName})");

        // ── Dinamik ORDER BY: her kriter için CASE WHEN [Col] IS NOT NULL THEN Weight ELSE 0 END
        // Hepsi toplanır; 2^n ağırlıklar olduğu için her kombinasyon eşsiz toplam üretir.
        var weightTerms = _criteria.Select(c =>
            $"CASE WHEN [{c.ColumnName}] IS NOT NULL THEN {c.Weight} ELSE 0 END");

        var sql = $@"
            SELECT TOP 1 r.[LayoutId]
            FROM {_ruleTable} r WITH (READUNCOMMITTED)
            WHERE r.[IsActive] = 1
              AND r.[DocType] = @DocType
              AND {string.Join(" AND ", whereClauses)}
            ORDER BY ({string.Join(" + ", weightTerms)}) DESC, r.[UpdatedAt] DESC;";

        await using var conn = await _factory.OpenConnectionAsync(ct);
        await using var cmd  = conn.CreateCommand();
        cmd.CommandText = sql;

        // DocType parametresi
        cmd.Parameters.Add(new SqlParameter("@DocType", SqlDbType.NVarChar, 60) { Value = ctx.DocType });

        // Her kriter için parametre (null değer → DBNull, ki rule'un IS NULL
        // bacağını seçsin — yani bağlamda yoksa o kriteri sormamış oluruz).
        foreach (var c in _criteria)
        {
            cmd.Parameters.Add(new SqlParameter(c.ParameterName, c.SqlType)
            {
                Value = c.ExtractValue(ctx) ?? DBNull.Value
            });
        }

        var result = await cmd.ExecuteScalarAsync(ct);
        if (result == null || result == DBNull.Value) return null;
        return Convert.ToInt32(result);
    }

    public async Task<int?> FindDefaultAsync(string docType, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(docType))
            throw new ArgumentException("docType zorunludur.", nameof(docType));

        var sql = $@"
            SELECT TOP 1 [Id]
            FROM {_layoutTable} WITH (READUNCOMMITTED)
            WHERE [IsActive] = 1 AND [DocType] = @DocType
            ORDER BY [IsDefault] DESC, [UpdatedAt] DESC;";

        await using var conn = await _factory.OpenConnectionAsync(ct);
        await using var cmd  = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.Add(new SqlParameter("@DocType", SqlDbType.NVarChar, 60) { Value = docType });

        var result = await cmd.ExecuteScalarAsync(ct);
        if (result == null || result == DBNull.Value) return null;
        return Convert.ToInt32(result);
    }

    // ── Yönetim CRUD ─────────────────────────────────────────────────────────

    private string SelectRuleFields() => $@"
        r.[Id], r.[DocType], r.[LayoutId], l.[Name] AS LayoutName,
        r.[CustomerId], r.[UserId], r.[BranchId], r.[WarehouseId],
        r.[IsActive], r.[UpdatedAt], r.[ContactGroupId]";

    public async Task<IReadOnlyCollection<DocLayoutRuleDto>> ListAllAsync(CancellationToken ct)
    {
        var sql = $@"
            SELECT {SelectRuleFields()}
            FROM {_ruleTable} r WITH (READUNCOMMITTED)
            INNER JOIN {_layoutTable} l WITH (READUNCOMMITTED) ON l.[Id] = r.[LayoutId]
            WHERE r.[IsActive] = 1
            ORDER BY r.[DocType], r.[UpdatedAt] DESC;";

        await using var conn = await _factory.OpenConnectionAsync(ct);
        await using var cmd  = conn.CreateCommand();
        cmd.CommandText = sql;

        var list = new List<DocLayoutRuleDto>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            list.Add(MapRule(reader));
        return list;
    }

    public async Task<DocLayoutRuleDto?> GetByIdAsync(int id, CancellationToken ct)
    {
        var sql = $@"
            SELECT {SelectRuleFields()}
            FROM {_ruleTable} r WITH (READUNCOMMITTED)
            INNER JOIN {_layoutTable} l WITH (READUNCOMMITTED) ON l.[Id] = r.[LayoutId]
            WHERE r.[Id] = @Id;";

        await using var conn = await _factory.OpenConnectionAsync(ct);
        await using var cmd  = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("@Id", id);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? MapRule(reader) : null;
    }

    public async Task<int> UpsertAsync(SaveDocLayoutRuleRequest req, CancellationToken ct)
    {
        await using var conn = await _factory.OpenConnectionAsync(ct);
        await using var cmd  = conn.CreateCommand();

        if (req.Id > 0)
        {
            cmd.CommandText = $@"
                UPDATE {_ruleTable}
                SET [DocType]        = @DocType,
                    [LayoutId]       = @LayoutId,
                    [CustomerId]     = @CustomerId,
                    [ContactGroupId] = @ContactGroupId,
                    [UserId]         = @UserId,
                    [BranchId]       = @BranchId,
                    [WarehouseId]    = @WarehouseId,
                    [IsActive]       = @IsActive,
                    [UpdatedAt]      = SYSUTCDATETIME()
                WHERE [Id] = @Id;
                SELECT @Id;";
            cmd.Parameters.AddWithValue("@Id", req.Id);
        }
        else
        {
            cmd.CommandText = $@"
                INSERT INTO {_ruleTable}
                    ([DocType],[LayoutId],[CustomerId],[ContactGroupId],[UserId],[BranchId],[WarehouseId],[IsActive])
                VALUES
                    (@DocType,@LayoutId,@CustomerId,@ContactGroupId,@UserId,@BranchId,@WarehouseId,@IsActive);
                SELECT CAST(SCOPE_IDENTITY() AS INT);";
        }

        cmd.Parameters.Add(new SqlParameter("@DocType",  SqlDbType.NVarChar, 60) { Value = req.DocType });
        cmd.Parameters.Add(new SqlParameter("@LayoutId", SqlDbType.Int)         { Value = req.LayoutId });
        cmd.Parameters.Add(new SqlParameter("@CustomerId",     SqlDbType.Int)              { Value = (object?)req.CustomerId     ?? DBNull.Value });
        cmd.Parameters.Add(new SqlParameter("@ContactGroupId", SqlDbType.Int)              { Value = (object?)req.ContactGroupId ?? DBNull.Value });
        cmd.Parameters.Add(new SqlParameter("@UserId",         SqlDbType.Int)              { Value = (object?)req.UserId         ?? DBNull.Value });
        cmd.Parameters.Add(new SqlParameter("@BranchId",       SqlDbType.Int)              { Value = (object?)req.BranchId       ?? DBNull.Value });
        cmd.Parameters.Add(new SqlParameter("@WarehouseId",    SqlDbType.Int)              { Value = (object?)req.WarehouseId    ?? DBNull.Value });
        cmd.Parameters.Add(new SqlParameter("@IsActive",       SqlDbType.Bit)              { Value = req.IsActive });

        try
        {
            var result = await cmd.ExecuteScalarAsync(ct);
            return result != null && result != DBNull.Value ? Convert.ToInt32(result) : 0;
        }
        catch (SqlException ex) when (ex.Number == 2601 || ex.Number == 2627)
        {
            // 2601: filtered unique index violation; 2627: unique constraint violation
            throw new InvalidOperationException(
                "Bu belge tipi ve kriter kombinasyonu için zaten aktif bir kural var.", ex);
        }
    }

    public async Task SoftDeleteAsync(int id, CancellationToken ct)
    {
        var sql = $"UPDATE {_ruleTable} SET [IsActive]=0, [UpdatedAt]=SYSUTCDATETIME() WHERE [Id]=@Id;";
        await using var conn = await _factory.OpenConnectionAsync(ct);
        await using var cmd  = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("@Id", id);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<IReadOnlyList<DocLayoutRuleMatchRow>> ListActiveByDocTypeAsync(string docType, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(docType))
            return Array.Empty<DocLayoutRuleMatchRow>();

        // Sorgu: belirtilen DocType için tüm aktif kuralların kriter alanları
        // ile UpdatedAt'ı. Küçük dataset; tamamen RAM'e alınıp eşleştirme C#'ta.
        var sql = $@"
            SELECT [Id], [LayoutId], [CustomerId], [UserId], [BranchId], [WarehouseId], [UpdatedAt], [ContactGroupId]
            FROM {_ruleTable} WITH (READUNCOMMITTED)
            WHERE [IsActive] = 1 AND [DocType] = @DocType;";

        await using var conn = await _factory.OpenConnectionAsync(ct);
        await using var cmd  = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.Add(new SqlParameter("@DocType", SqlDbType.NVarChar, 60) { Value = docType });

        var list = new List<DocLayoutRuleMatchRow>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            list.Add(new DocLayoutRuleMatchRow(
                Id:             reader.GetInt32(0),
                LayoutId:       reader.GetInt32(1),
                CustomerId:     reader.IsDBNull(2) ? null : reader.GetInt32(2),
                UserId:         reader.IsDBNull(3) ? null : reader.GetInt32(3),
                BranchId:       reader.IsDBNull(4) ? null : reader.GetInt32(4),
                WarehouseId:    reader.IsDBNull(5) ? null : reader.GetInt32(5),
                UpdatedAt:      reader.GetDateTime(6),
                ContactGroupId: reader.IsDBNull(7) ? null : reader.GetInt32(7)));
        }
        return list;
    }

    private DocLayoutRuleDto MapRule(SqlDataReader r)
    {
        var customerId     = r.IsDBNull(4) ? (int?)null  : r.GetInt32(4);
        var userId         = r.IsDBNull(5) ? (int?)null  : r.GetInt32(5);
        var branchId       = r.IsDBNull(6) ? (int?)null  : r.GetInt32(6);
        var warehouseId    = r.IsDBNull(7) ? (int?)null  : r.GetInt32(7);
        var contactGroupId = r.IsDBNull(10) ? (int?)null : r.GetInt32(10);

        // Weight'i kriter listesinden dinamik hesapla
        int weight = 0;
        foreach (var c in _criteria)
        {
            object? val = c.ColumnName switch
            {
                "CustomerId"     => customerId,
                "ContactGroupId" => contactGroupId,
                "UserId"         => userId,
                "BranchId"       => branchId,
                "WarehouseId"    => warehouseId,
                _                => null,
            };
            if (val != null) weight += c.Weight;
        }

        return new DocLayoutRuleDto(
            Id:             r.GetInt32(0),
            DocType:        r.GetString(1),
            DocTypeLabel:   r.GetString(1), // controller'da insanlaştırılır
            LayoutId:       r.GetInt32(2),
            LayoutName:     r.GetString(3),
            CustomerId:     customerId,
            ContactGroupId: contactGroupId,
            UserId:         userId,
            BranchId:       branchId,
            WarehouseId:    warehouseId,
            IsActive:       r.GetBoolean(8),
            Weight:         weight,
            UpdatedAt:      r.GetDateTime(9));
    }
}
