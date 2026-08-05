using System.Globalization;
using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Application.Contracts;
using CalibraHub.Persistence.Database;
using CalibraHub.Persistence.Options;
using Microsoft.Data.SqlClient;

namespace CalibraHub.Persistence.Repositories;

/// <summary>
/// Makine Planlama Faz 2 (2026-08-05). Bkz. <c>IMachineCalendarRepository</c> XML doc'u.
/// <c>MachineWorkWindow</c>/<c>CompanyHoliday</c> per-company DB tabloları (CompanyId kolonu
/// yok) — <c>MachineWorkWindow</c> yalnız <c>Machine</c> join'i üzerinden şirkete taşınır
/// (Machine.CompanyId), <c>CompanyHoliday</c> zaten DB'nin kendisiyle şirkete özeldir.
/// </summary>
public sealed class SqlMachineCalendarRepository : IMachineCalendarRepository
{
    private readonly SqlServerConnectionFactory _connectionFactory;
    private readonly string _schema;

    public SqlMachineCalendarRepository(SqlServerConnectionFactory connectionFactory, CalibraDatabaseOptions options)
    {
        _connectionFactory = connectionFactory;
        _schema = string.IsNullOrWhiteSpace(options.Schema) ? "dbo" : options.Schema.Trim();
    }

    private string T(string table) => $"[{_schema}].[{table}]";

    public async Task<IReadOnlyList<ScheduleMachineDto>> ListActiveMachinesAsync(CancellationToken ct)
    {
        var companyId = _connectionFactory.ResolveCurrentCompanyId();
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            SELECT [Id], [Code], [Name], [HourlyCapacity]
            FROM {T("Machine")}
            WHERE [CompanyId] = @CompanyId AND [IsActive] = 1
            ORDER BY [SortOrder], [Code];
            """;
        cmd.Parameters.AddWithValue("@CompanyId", companyId);

        var list = new List<ScheduleMachineDto>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
        {
            list.Add(new ScheduleMachineDto(
                Id: r.GetInt32(0),
                Code: r.GetString(1),
                Name: r.IsDBNull(2) ? null : r.GetString(2),
                HourlyCapacity: r.IsDBNull(3) ? null : r.GetDecimal(3)));
        }
        return list;
    }

    public async Task<IReadOnlyList<MachineWorkWindowDto>> ListWorkWindowsAsync(CancellationToken ct)
    {
        var companyId = _connectionFactory.ResolveCurrentCompanyId();
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            SELECT w.[Id], w.[MachineId], w.[DayOfWeek], w.[StartMinute], w.[EndMinute]
            FROM {T("MachineWorkWindow")} w
            INNER JOIN {T("Machine")} m ON m.[Id] = w.[MachineId] AND m.[CompanyId] = @CompanyId AND m.[IsActive] = 1
            WHERE w.[IsActive] = 1
            ORDER BY w.[MachineId], w.[DayOfWeek], w.[StartMinute];
            """;
        cmd.Parameters.AddWithValue("@CompanyId", companyId);

        var list = new List<MachineWorkWindowDto>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
        {
            list.Add(new MachineWorkWindowDto(
                Id: r.GetInt32(0),
                MachineId: r.GetInt32(1),
                DayOfWeek: r.GetByte(2),
                StartMinute: r.GetInt16(3),
                EndMinute: r.GetInt16(4)));
        }
        return list;
    }

    public async Task<MachineWorkWindowDto?> GetWorkWindowAsync(int id, CancellationToken ct)
    {
        var companyId = _connectionFactory.ResolveCurrentCompanyId();
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            SELECT w.[Id], w.[MachineId], w.[DayOfWeek], w.[StartMinute], w.[EndMinute]
            FROM {T("MachineWorkWindow")} w
            INNER JOIN {T("Machine")} m ON m.[Id] = w.[MachineId] AND m.[CompanyId] = @CompanyId
            WHERE w.[Id] = @Id AND w.[IsActive] = 1;
            """;
        cmd.Parameters.AddWithValue("@CompanyId", companyId);
        cmd.Parameters.AddWithValue("@Id", id);

        await using var r = await cmd.ExecuteReaderAsync(ct);
        if (!await r.ReadAsync(ct)) return null;
        return new MachineWorkWindowDto(
            Id: r.GetInt32(0),
            MachineId: r.GetInt32(1),
            DayOfWeek: r.GetByte(2),
            StartMinute: r.GetInt16(3),
            EndMinute: r.GetInt16(4));
    }

    public async Task<int> SaveWorkWindowAsync(SaveMachineWorkWindowRequest request, int? userId, CancellationToken ct)
    {
        var companyId = _connectionFactory.ResolveCurrentCompanyId();
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);

        // Makinenin bu şirkete ait aktif bir makine olduğunu doğrula (cross-company id guessing savunması).
        await using (var chk = conn.CreateCommand())
        {
            chk.CommandText = $"SELECT 1 FROM {T("Machine")} WHERE [Id] = @MachineId AND [CompanyId] = @CompanyId AND [IsActive] = 1;";
            chk.Parameters.AddWithValue("@MachineId", request.MachineId);
            chk.Parameters.AddWithValue("@CompanyId", companyId);
            var exists = await chk.ExecuteScalarAsync(ct);
            if (exists is null) throw new ArgumentException("Seçilen makine bulunamadı.");
        }

        int id;
        if (request.Id <= 0)
        {
            await using var ins = conn.CreateCommand();
            ins.CommandText = $"""
                INSERT INTO {T("MachineWorkWindow")}
                    ([MachineId],[DayOfWeek],[StartMinute],[EndMinute],[IsActive],[CreatedById],[Created])
                VALUES
                    (@MachineId,@DayOfWeek,@StartMinute,@EndMinute,1,@CreatedById,SYSUTCDATETIME());
                SELECT CAST(SCOPE_IDENTITY() AS INT);
                """;
            AddWindowParams(ins, request);
            ins.Parameters.Add(new SqlParameter("@CreatedById", (object?)(userId is > 0 ? userId : null) ?? DBNull.Value));
            id = (int)(await ins.ExecuteScalarAsync(ct))!;
        }
        else
        {
            await using var upd = conn.CreateCommand();
            upd.CommandText = $"""
                UPDATE {T("MachineWorkWindow")}
                SET [MachineId] = @MachineId, [DayOfWeek] = @DayOfWeek,
                    [StartMinute] = @StartMinute, [EndMinute] = @EndMinute,
                    [UpdatedById] = @UpdatedById, [Updated] = SYSUTCDATETIME()
                WHERE [Id] = @Id AND [IsActive] = 1;
                """;
            AddWindowParams(upd, request);
            upd.Parameters.Add(new SqlParameter("@UpdatedById", (object?)(userId is > 0 ? userId : null) ?? DBNull.Value));
            upd.Parameters.AddWithValue("@Id", request.Id);
            await upd.ExecuteNonQueryAsync(ct);
            id = request.Id;
        }
        return id;
    }

    private static void AddWindowParams(SqlCommand cmd, SaveMachineWorkWindowRequest r)
    {
        cmd.Parameters.AddWithValue("@MachineId", r.MachineId);
        cmd.Parameters.AddWithValue("@DayOfWeek", r.DayOfWeek);
        cmd.Parameters.AddWithValue("@StartMinute", r.StartMinute);
        cmd.Parameters.AddWithValue("@EndMinute", r.EndMinute);
    }

    public async Task DeleteWorkWindowAsync(int id, int? userId, CancellationToken ct)
    {
        var companyId = _connectionFactory.ResolveCurrentCompanyId();
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            UPDATE w
            SET [IsActive] = 0, [UpdatedById] = @UpdatedById, [Updated] = SYSUTCDATETIME()
            FROM {T("MachineWorkWindow")} w
            INNER JOIN {T("Machine")} m ON m.[Id] = w.[MachineId]
            WHERE w.[Id] = @Id AND w.[IsActive] = 1 AND m.[CompanyId] = @CompanyId;
            """;
        cmd.Parameters.AddWithValue("@Id", id);
        cmd.Parameters.AddWithValue("@CompanyId", companyId);
        cmd.Parameters.Add(new SqlParameter("@UpdatedById", (object?)(userId is > 0 ? userId : null) ?? DBNull.Value));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<IReadOnlyList<HolidayDto>> ListHolidaysAsync(CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            SELECT [Id], CONVERT(varchar(10), [HolidayDate], 23), [Name]
            FROM {T("CompanyHoliday")}
            WHERE [IsActive] = 1
            ORDER BY [HolidayDate];
            """;

        var list = new List<HolidayDto>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
        {
            list.Add(new HolidayDto(
                Id: r.GetInt32(0),
                Date: r.GetString(1),
                Name: r.IsDBNull(2) ? null : r.GetString(2)));
        }
        return list;
    }

    public async Task<HolidayDto?> GetHolidayAsync(int id, CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            SELECT [Id], CONVERT(varchar(10), [HolidayDate], 23), [Name]
            FROM {T("CompanyHoliday")}
            WHERE [Id] = @Id AND [IsActive] = 1;
            """;
        cmd.Parameters.AddWithValue("@Id", id);

        await using var r = await cmd.ExecuteReaderAsync(ct);
        if (!await r.ReadAsync(ct)) return null;
        return new HolidayDto(
            Id: r.GetInt32(0),
            Date: r.GetString(1),
            Name: r.IsDBNull(2) ? null : r.GetString(2));
    }

    public async Task<int> SaveHolidayAsync(SaveHolidayRequest request, int? userId, CancellationToken ct)
    {
        if (!DateTime.TryParseExact(request.Date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            throw new ArgumentException("Geçersiz tarih formatı.");

        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);

        // UX_CompanyHoliday_Date (filtered unique) ile uyumlu ön-kontrol — kendisi hariç aynı tarihte aktif kayıt var mı.
        await using (var chk = conn.CreateCommand())
        {
            chk.CommandText = $"""
                SELECT 1 FROM {T("CompanyHoliday")}
                WHERE [HolidayDate] = @Date AND [IsActive] = 1 AND [Id] <> @SelfId;
                """;
            chk.Parameters.AddWithValue("@Date", date.Date);
            chk.Parameters.AddWithValue("@SelfId", request.Id);
            var dup = await chk.ExecuteScalarAsync(ct);
            if (dup is not null) throw new ArgumentException($"Bu tarih için zaten tanımlı bir tatil var: {request.Date}");
        }

        int id;
        if (request.Id <= 0)
        {
            await using var ins = conn.CreateCommand();
            ins.CommandText = $"""
                INSERT INTO {T("CompanyHoliday")} ([HolidayDate],[Name],[IsActive],[CreatedById],[Created])
                VALUES (@Date,@Name,1,@CreatedById,SYSUTCDATETIME());
                SELECT CAST(SCOPE_IDENTITY() AS INT);
                """;
            ins.Parameters.AddWithValue("@Date", date.Date);
            ins.Parameters.Add(new SqlParameter("@Name", (object?)request.Name ?? DBNull.Value));
            ins.Parameters.Add(new SqlParameter("@CreatedById", (object?)(userId is > 0 ? userId : null) ?? DBNull.Value));
            id = (int)(await ins.ExecuteScalarAsync(ct))!;
        }
        else
        {
            await using var upd = conn.CreateCommand();
            upd.CommandText = $"""
                UPDATE {T("CompanyHoliday")}
                SET [HolidayDate] = @Date, [Name] = @Name, [UpdatedById] = @UpdatedById, [Updated] = SYSUTCDATETIME()
                WHERE [Id] = @Id AND [IsActive] = 1;
                """;
            upd.Parameters.AddWithValue("@Date", date.Date);
            upd.Parameters.Add(new SqlParameter("@Name", (object?)request.Name ?? DBNull.Value));
            upd.Parameters.Add(new SqlParameter("@UpdatedById", (object?)(userId is > 0 ? userId : null) ?? DBNull.Value));
            upd.Parameters.AddWithValue("@Id", request.Id);
            await upd.ExecuteNonQueryAsync(ct);
            id = request.Id;
        }
        return id;
    }

    public async Task DeleteHolidayAsync(int id, int? userId, CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            UPDATE {T("CompanyHoliday")}
            SET [IsActive] = 0, [UpdatedById] = @UpdatedById, [Updated] = SYSUTCDATETIME()
            WHERE [Id] = @Id AND [IsActive] = 1;
            """;
        cmd.Parameters.AddWithValue("@Id", id);
        cmd.Parameters.Add(new SqlParameter("@UpdatedById", (object?)(userId is > 0 ? userId : null) ?? DBNull.Value));
        await cmd.ExecuteNonQueryAsync(ct);
    }
}
