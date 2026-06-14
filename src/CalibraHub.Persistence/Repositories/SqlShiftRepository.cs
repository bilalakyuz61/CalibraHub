using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Application.Contracts;
using CalibraHub.Domain.Entities;
using CalibraHub.Persistence.Database;
using CalibraHub.Persistence.Options;
using Microsoft.Data.SqlClient;

namespace CalibraHub.Persistence.Repositories;

public sealed class SqlShiftRepository : IShiftRepository
{
    private readonly SqlServerConnectionFactory _factory;
    private readonly string _schema;
    private readonly string _table;
    private readonly string _breakTable;

    public SqlShiftRepository(SqlServerConnectionFactory factory, CalibraDatabaseOptions options)
    {
        _factory = factory;
        _schema = string.IsNullOrWhiteSpace(options.Schema) ? "dbo" : options.Schema.Trim();
        var s = _schema.Replace("]", "]]");
        _table      = $"[{s}].[Shift]";
        _breakTable = $"[{s}].[ShiftBreak]";
    }

    public async Task<IReadOnlyList<ShiftDto>> ListAsync(bool includeInactive, CancellationToken ct)
    {
        await using var conn = await _factory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        var filter = includeInactive ? "" : "WHERE [IsActive] = 1";
        cmd.CommandText = $@"
            SELECT [Id],[Code],[Name],[StartTime],[EndTime],[IsOvernight],
                   [ColorHex],[SortOrder],[IsActive],[Created],[Updated]
            FROM {_table}
            {filter}
            ORDER BY [SortOrder], [StartTime], [Code];";
        var list = new List<ShiftDto>();
        await using (var r = await cmd.ExecuteReaderAsync(ct))
            while (await r.ReadAsync(ct)) list.Add(Read(r));

        // Liste için tüm araları tek seferde çek + grupla (N+1 önle).
        if (list.Count > 0)
        {
            var breaksByShift = await GetBreaksForShiftsAsync(conn, list.Select(s => s.Id).ToArray(), ct);
            for (var i = 0; i < list.Count; i++)
            {
                if (breaksByShift.TryGetValue(list[i].Id, out var breaks))
                    list[i] = WithBreaks(list[i], breaks);
            }
        }
        return list;
    }

    public async Task<ShiftDto?> GetAsync(int id, CancellationToken ct)
    {
        await using var conn = await _factory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $@"
            SELECT [Id],[Code],[Name],[StartTime],[EndTime],[IsOvernight],
                   [ColorHex],[SortOrder],[IsActive],[Created],[Updated]
            FROM {_table} WHERE [Id] = @Id;";
        cmd.Parameters.AddWithValue("@Id", id);
        ShiftDto? dto;
        await using (var r = await cmd.ExecuteReaderAsync(ct))
            dto = await r.ReadAsync(ct) ? Read(r) : null;
        if (dto is null) return null;

        var breaks = await GetBreaksForShiftsAsync(conn, new[] { dto.Id }, ct);
        return breaks.TryGetValue(dto.Id, out var list) ? WithBreaks(dto, list) : dto;
    }

    public async Task<int> SaveAsync(Shift entity, IReadOnlyList<ShiftBreak>? breaks, CancellationToken ct)
    {
        await using var conn = await _factory.OpenConnectionAsync(ct);
        await using var tx = (SqlTransaction)await conn.BeginTransactionAsync(ct);
        try
        {
            // 1) Shift UPSERT
            int newId;
            await using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = tx;
                if (entity.Id <= 0)
                {
                    cmd.CommandText = $@"
                        INSERT INTO {_table}
                            ([Code],[Name],[StartTime],[EndTime],[IsOvernight],
                             [ColorHex],[SortOrder],[IsActive],[CreatedById],[Created])
                        VALUES
                            (@Code,@Name,@Start,@End,@Overnight,
                             @Color,@Sort,@Active,@CreatedById,SYSUTCDATETIME());
                        SELECT CAST(SCOPE_IDENTITY() AS INT);";
                }
                else
                {
                    cmd.CommandText = $@"
                        UPDATE {_table}
                        SET [Code]=@Code,[Name]=@Name,[StartTime]=@Start,[EndTime]=@End,
                            [IsOvernight]=@Overnight,[ColorHex]=@Color,[SortOrder]=@Sort,
                            [IsActive]=@Active,[UpdatedById]=@UpdatedById,[Updated]=SYSUTCDATETIME()
                        WHERE [Id]=@Id;
                        SELECT @Id;";
                    cmd.Parameters.AddWithValue("@Id", entity.Id);
                    cmd.Parameters.AddWithValue("@UpdatedById", (object?)entity.UpdatedById ?? DBNull.Value);
                }
                cmd.Parameters.AddWithValue("@Code",      entity.Code.Trim());
                cmd.Parameters.AddWithValue("@Name",      entity.Name.Trim());
                cmd.Parameters.AddWithValue("@Start",     entity.StartTime);
                cmd.Parameters.AddWithValue("@End",       entity.EndTime);
                cmd.Parameters.AddWithValue("@Overnight", entity.IsOvernight);
                cmd.Parameters.AddWithValue("@Color",     (object?)entity.ColorHex ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@Sort",      entity.SortOrder);
                cmd.Parameters.AddWithValue("@Active",    entity.IsActive);
                if (entity.Id <= 0)
                    cmd.Parameters.AddWithValue("@CreatedById", (object?)entity.CreatedById ?? DBNull.Value);
                var result = await cmd.ExecuteScalarAsync(ct);
                newId = result != null && result != DBNull.Value ? Convert.ToInt32(result) : 0;
            }

            // 2) Aralar — null ise dokunma; aksi halde "tümünü sil + yeni listeyi yaz" (replace pattern).
            //    UI inline grid bunu kolayca tüketir; aktif iş emirlerinde mola hesabı zaten gerçek
            //    zamana göre çalışır, geçmiş kayıt ayarları için ShiftBreak audit tutmayalım (MVP).
            if (breaks is not null)
            {
                await using (var delCmd = conn.CreateCommand())
                {
                    delCmd.Transaction = tx;
                    delCmd.CommandText = $"DELETE FROM {_breakTable} WHERE [ShiftId] = @Id;";
                    delCmd.Parameters.AddWithValue("@Id", newId);
                    await delCmd.ExecuteNonQueryAsync(ct);
                }
                foreach (var b in breaks)
                {
                    await using var insCmd = conn.CreateCommand();
                    insCmd.Transaction = tx;
                    // 2026-06-06: IsPaid kullanım dışı — DB kolonu duruyor (backward-compat),
                    // INSERT'te yer almıyor, default(0) ile kayıt edilir.
                    insCmd.CommandText = $@"
                        INSERT INTO {_breakTable}
                            ([ShiftId],[Name],[StartTime],[EndTime],[SortOrder])
                        VALUES
                            (@ShiftId,@Name,@Start,@End,@Sort);";
                    insCmd.Parameters.AddWithValue("@ShiftId", newId);
                    insCmd.Parameters.AddWithValue("@Name",    b.Name.Trim());
                    insCmd.Parameters.AddWithValue("@Start",   b.StartTime);
                    insCmd.Parameters.AddWithValue("@End",     b.EndTime);
                    insCmd.Parameters.AddWithValue("@Sort",    b.SortOrder);
                    await insCmd.ExecuteNonQueryAsync(ct);
                }
            }

            await tx.CommitAsync(ct);
            return newId;
        }
        catch
        {
            try { await tx.RollbackAsync(ct); } catch { /* ignore */ }
            throw;
        }
    }

    // ── helpers — breaks ─────────────────────────────────────────────────
    private async Task<Dictionary<int, List<ShiftBreakDto>>> GetBreaksForShiftsAsync(
        SqlConnection conn, int[] shiftIds, CancellationToken ct)
    {
        var result = new Dictionary<int, List<ShiftBreakDto>>();
        if (shiftIds.Length == 0) return result;

        await using var cmd = conn.CreateCommand();
        // Tek sorgu — IN(...) literal ID listesi (admin tarafı; sayı düşük, SQL injection riski yok int olduğu için).
        var ids = string.Join(',', shiftIds);
        // 2026-06-06: IsPaid SELECT dışında — kolon legacy, dönmeyecek.
        cmd.CommandText = $@"
            SELECT [Id],[ShiftId],[Name],[StartTime],[EndTime],[SortOrder]
            FROM {_breakTable}
            WHERE [ShiftId] IN ({ids})
            ORDER BY [ShiftId], [SortOrder], [StartTime];";
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
        {
            var start = (TimeSpan)r.GetValue(3);
            var end   = (TimeSpan)r.GetValue(4);
            var dur   = (int)(end - start).TotalMinutes;
            var shiftId = r.GetInt32(1);
            if (!result.TryGetValue(shiftId, out var list))
            {
                list = new List<ShiftBreakDto>();
                result[shiftId] = list;
            }
            list.Add(new ShiftBreakDto(
                Id:              r.GetInt32(0),
                ShiftId:         shiftId,
                Name:            r.GetString(2),
                StartTime:       FormatTime(start),
                EndTime:         FormatTime(end),
                DurationMinutes: dur,
                SortOrder:       r.GetInt32(5)));
        }
        return result;
    }

    private static ShiftDto WithBreaks(ShiftDto dto, List<ShiftBreakDto> breaks)
    {
        var totalBreak = breaks.Sum(b => b.DurationMinutes);
        var net = Math.Max(0, dto.DurationMinutes - totalBreak);
        return dto with { Breaks = breaks, TotalBreakMinutes = totalBreak, NetWorkMinutes = net };
    }

    public async Task DeleteAsync(int id, int? userId, CancellationToken ct)
    {
        // Soft delete — ShiftAssignment FK referansı varsa fiziksel silme FK ihlali.
        await using var conn = await _factory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $@"
            UPDATE {_table}
            SET [IsActive] = 0,
                [UpdatedById] = @UpdatedById,
                [Updated]     = SYSUTCDATETIME()
            WHERE [Id] = @Id;";
        cmd.Parameters.AddWithValue("@Id", id);
        cmd.Parameters.AddWithValue("@UpdatedById", (object?)userId ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static ShiftDto Read(SqlDataReader r)
    {
        var startTs = (TimeSpan)r.GetValue(3);
        var endTs   = (TimeSpan)r.GetValue(4);
        var overnight = r.GetBoolean(5);
        var duration = overnight
            ? (TimeSpan.FromHours(24) - startTs + endTs)
            : (endTs - startTs);
        return new ShiftDto(
            Id:              r.GetInt32(0),
            Code:            r.GetString(1),
            Name:            r.GetString(2),
            StartTime:       FormatTime(startTs),
            EndTime:         FormatTime(endTs),
            IsOvernight:     overnight,
            DurationMinutes: (int)duration.TotalMinutes,
            ColorHex:        r.IsDBNull(6) ? null : r.GetString(6),
            SortOrder:       r.GetInt32(7),
            IsActive:        r.GetBoolean(8),
            Created:         r.GetDateTime(9),
            Updated:         r.IsDBNull(10) ? null : r.GetDateTime(10));
    }

    private static string FormatTime(TimeSpan t) => $"{(int)t.TotalHours:D2}:{t.Minutes:D2}";
}

public sealed class SqlShiftAssignmentRepository : IShiftAssignmentRepository
{
    private readonly SqlServerConnectionFactory _factory;
    private readonly string _schema;
    private readonly string _table;

    public SqlShiftAssignmentRepository(SqlServerConnectionFactory factory, CalibraDatabaseOptions options)
    {
        _factory = factory;
        _schema = string.IsNullOrWhiteSpace(options.Schema) ? "dbo" : options.Schema.Trim();
        var s = _schema.Replace("]", "]]");
        _table = $"[{s}].[ShiftAssignment]";
    }

    public async Task<IReadOnlyList<ShiftAssignmentDto>> GetByPersonnelAsync(int personnelId, CancellationToken ct)
    {
        await using var conn = await _factory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = BuildSelect("WHERE a.[PersonnelId] = @Id AND a.[IsActive] = 1 ORDER BY a.[DayOfWeek]");
        cmd.Parameters.AddWithValue("@Id", personnelId);
        return await ReadList(cmd, ct);
    }

    public async Task<IReadOnlyList<ShiftAssignmentDto>> GetByShiftAsync(int shiftId, CancellationToken ct)
    {
        await using var conn = await _factory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = BuildSelect("WHERE a.[ShiftId] = @Id AND a.[IsActive] = 1 ORDER BY a.[DayOfWeek], p.[FullName]");
        cmd.Parameters.AddWithValue("@Id", shiftId);
        return await ReadList(cmd, ct);
    }

    public async Task<ShiftAssignmentDto?> GetAsync(int id, CancellationToken ct)
    {
        await using var conn = await _factory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = BuildSelect("WHERE a.[Id] = @Id");
        cmd.Parameters.AddWithValue("@Id", id);
        var list = await ReadList(cmd, ct);
        return list.FirstOrDefault();
    }

    public async Task<int> SaveAsync(ShiftAssignment entity, CancellationToken ct)
    {
        await using var conn = await _factory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        if (entity.Id <= 0)
        {
            // Aynı personel+gün için varsa eski kaydı pasif yap (yeni vardiyaya geçiş)
            cmd.CommandText = $@"
                UPDATE {_table}
                SET [IsActive] = 0, [UpdatedById] = @CreatedById, [Updated] = SYSUTCDATETIME()
                WHERE [PersonnelId] = @PersonnelId AND [DayOfWeek] = @Day AND [IsActive] = 1;

                INSERT INTO {_table}
                    ([PersonnelId],[ShiftId],[DayOfWeek],[EffectiveFrom],[EffectiveTo],
                     [IsActive],[CreatedById],[Created])
                VALUES
                    (@PersonnelId,@ShiftId,@Day,@From,@To,
                     @Active,@CreatedById,SYSUTCDATETIME());
                SELECT CAST(SCOPE_IDENTITY() AS INT);";
        }
        else
        {
            cmd.CommandText = $@"
                UPDATE {_table}
                SET [PersonnelId]=@PersonnelId,[ShiftId]=@ShiftId,[DayOfWeek]=@Day,
                    [EffectiveFrom]=@From,[EffectiveTo]=@To,[IsActive]=@Active,
                    [UpdatedById]=@CreatedById,[Updated]=SYSUTCDATETIME()
                WHERE [Id]=@Id;
                SELECT @Id;";
            cmd.Parameters.AddWithValue("@Id", entity.Id);
        }
        cmd.Parameters.AddWithValue("@PersonnelId", entity.PersonnelId);
        cmd.Parameters.AddWithValue("@ShiftId",     entity.ShiftId);
        cmd.Parameters.AddWithValue("@Day",         (byte)entity.DayOfWeek);
        cmd.Parameters.AddWithValue("@From",        (object?)(entity.EffectiveFrom?.ToDateTime(TimeOnly.MinValue)) ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@To",          (object?)(entity.EffectiveTo?.ToDateTime(TimeOnly.MinValue))   ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Active",      entity.IsActive);
        cmd.Parameters.AddWithValue("@CreatedById",  (object?)entity.CreatedById ?? DBNull.Value);
        var result = await cmd.ExecuteScalarAsync(ct);
        return result != null && result != DBNull.Value ? Convert.ToInt32(result) : 0;
    }

    public async Task DeleteAsync(int id, int? userId, CancellationToken ct)
    {
        await using var conn = await _factory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $@"
            UPDATE {_table}
            SET [IsActive] = 0, [UpdatedById] = @UpdatedById, [Updated] = SYSUTCDATETIME()
            WHERE [Id] = @Id;";
        cmd.Parameters.AddWithValue("@Id", id);
        cmd.Parameters.AddWithValue("@UpdatedById", (object?)userId ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<ShiftAssignmentDto?> GetCurrentAsync(int personnelId, DateOnly date, CancellationToken ct)
    {
        await using var conn = await _factory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = BuildSelect(@"
            WHERE a.[PersonnelId] = @Id
              AND a.[DayOfWeek]   = @Day
              AND a.[IsActive]    = 1
              AND (a.[EffectiveFrom] IS NULL OR a.[EffectiveFrom] <= @Date)
              AND (a.[EffectiveTo]   IS NULL OR a.[EffectiveTo]   >= @Date)");
        cmd.Parameters.AddWithValue("@Id",   personnelId);
        cmd.Parameters.AddWithValue("@Day",  (byte)date.DayOfWeek);
        cmd.Parameters.AddWithValue("@Date", date.ToDateTime(TimeOnly.MinValue));
        var list = await ReadList(cmd, ct);
        return list.FirstOrDefault();
    }

    private string BuildSelect(string filter) => $@"
        SELECT a.[Id], a.[PersonnelId], p.[FullName] AS PersonnelName,
               a.[ShiftId], s.[Code] AS ShiftCode, s.[Name] AS ShiftName,
               a.[DayOfWeek], a.[EffectiveFrom], a.[EffectiveTo], a.[IsActive]
        FROM {_table} a
        LEFT JOIN [{_schema}].[Personnel] p ON p.[Id] = a.[PersonnelId]
        LEFT JOIN [{_schema}].[Shift]     s ON s.[Id] = a.[ShiftId]
        {filter};";

    private static async Task<List<ShiftAssignmentDto>> ReadList(SqlCommand cmd, CancellationToken ct)
    {
        var list = new List<ShiftAssignmentDto>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
        {
            list.Add(new ShiftAssignmentDto(
                Id:             r.GetInt32(0),
                PersonnelId:    r.GetInt32(1),
                PersonnelName:  r.IsDBNull(2) ? null : r.GetString(2),
                ShiftId:        r.GetInt32(3),
                ShiftCode:      r.IsDBNull(4) ? null : r.GetString(4),
                ShiftName:      r.IsDBNull(5) ? null : r.GetString(5),
                DayOfWeek:      (DayOfWeek)r.GetByte(6),
                EffectiveFrom:  r.IsDBNull(7) ? null : DateOnly.FromDateTime(r.GetDateTime(7)),
                EffectiveTo:    r.IsDBNull(8) ? null : DateOnly.FromDateTime(r.GetDateTime(8)),
                IsActive:       r.GetBoolean(9)));
        }
        return list;
    }
}
