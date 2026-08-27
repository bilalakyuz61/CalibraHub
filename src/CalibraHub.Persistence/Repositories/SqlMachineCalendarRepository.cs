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

    /// <summary>
    /// Vardiya Senaryoları (2026-08-05) — bkz. <c>IMachineCalendarRepository.ListWorkWindowsAsync</c>
    /// XML doc'u. Akış: (1) scenarioId null ise IsDefault=1 senaryoyu çöz, (2) senaryonun
    /// ScenarioMachineShift atamalarını Shift ile join'le oku, (3) C# tarafında her atamayı
    /// DaysMask'in set-bit'i olan günler için MachineWorkWindowDto'ya TÜRET (overnight ise iki
    /// güne böl), (4) hiç türetilmiş satır yoksa eski MachineWorkWindow okumasına LEGACY FALLBACK.
    /// </summary>
    public async Task<IReadOnlyList<MachineWorkWindowDto>> ListWorkWindowsAsync(CancellationToken ct, int? scenarioId = null)
    {
        var companyId = _connectionFactory.ResolveCurrentCompanyId();
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);

        var resolvedScenarioId = scenarioId;
        if (resolvedScenarioId is null)
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = $"SELECT TOP 1 [Id] FROM {T("ShiftScenario")} WHERE [IsDefault] = 1 AND [IsActive] = 1;";
            var found = await cmd.ExecuteScalarAsync(ct);
            resolvedScenarioId = found is int i ? i : (int?)null;
        }

        var derived = new List<MachineWorkWindowDto>();
        if (resolvedScenarioId is > 0)
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = $"""
                SELECT sms.[MachineId], sms.[DaysMask], s.[StartTime], s.[EndTime], s.[IsOvernight]
                FROM {T("ScenarioMachineShift")} sms
                INNER JOIN {T("ShiftScenario")} sc ON sc.[Id] = sms.[ScenarioId] AND sc.[IsActive] = 1
                INNER JOIN {T("Shift")} s ON s.[Id] = sms.[ShiftId] AND s.[IsActive] = 1
                INNER JOIN {T("Machine")} m ON m.[Id] = sms.[MachineId] AND m.[CompanyId] = @CompanyId AND m.[IsActive] = 1
                WHERE sms.[ScenarioId] = @ScenarioId AND sms.[IsActive] = 1;
                """;
            cmd.Parameters.AddWithValue("@CompanyId", companyId);
            cmd.Parameters.AddWithValue("@ScenarioId", resolvedScenarioId.Value);

            await using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct))
            {
                var machineId = r.GetInt32(0);
                var daysMask = r.GetByte(1);
                var start = (TimeSpan)r.GetValue(2);
                var end = (TimeSpan)r.GetValue(3);
                var isOvernight = r.GetBoolean(4);
                var startMin = (short)start.TotalMinutes;
                var endMin = (short)end.TotalMinutes;

                for (var d = 0; d < 7; d++)
                {
                    if ((daysMask & (1 << d)) == 0) continue;
                    var day = (byte)d;

                    if (!isOvernight)
                    {
                        derived.Add(new MachineWorkWindowDto(0, machineId, day, startMin, endMin));
                    }
                    else
                    {
                        // Gece vardiyası: (start,1440)@gün + (0,end)@gün+1. end==0 → tam gün ilk
                        // pencere (00:00'da biten vardiya) — ikinci pencere sıfır uzunlukta olurdu, atlanır.
                        derived.Add(new MachineWorkWindowDto(0, machineId, day, startMin, 1440));
                        if (endMin > 0)
                        {
                            var nextDay = (byte)((day + 1) % 7);
                            derived.Add(new MachineWorkWindowDto(0, machineId, nextDay, 0, endMin));
                        }
                    }
                }
            }
        }

        if (derived.Count > 0) return derived;

        // Legacy fallback — senaryoda hiç atama yoksa (veya hiç senaryo yoksa) eski takvim çalışmaya devam eder.
        return await ListLegacyWorkWindowsAsync(conn, companyId, ct);
    }

    private async Task<IReadOnlyList<MachineWorkWindowDto>> ListLegacyWorkWindowsAsync(SqlConnection conn, int companyId, CancellationToken ct)
    {
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
                WHERE [HolidayDate] = @Date AND [IsActive] = 1 AND [Id] <> @SelfId
                  AND [CompanyId] = @CompanyId;
                """;
            // Index (CompanyId, HolidayDate) oldu; on-kontrol de sirket kapsamli olmali.
            // Olmazsa index izin verse bile uygulama BASKA sirketin ayni tarihli tatili
            // yuzunden reddeder — kullanici icin sebepsiz gorunen bir engel.
            chk.Parameters.AddWithValue("@CompanyId", _connectionFactory.ResolveEffectiveCompanyId());
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

    // ── Vardiya Senaryoları (2026-08-05) ──────────────────────────────────────────

    public async Task<IReadOnlyList<ShiftScenarioDto>> ListScenariosAsync(CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            SELECT [Id], [Name], [Description], [IsDefault], [IsActive]
            FROM {T("ShiftScenario")}
            WHERE [IsActive] = 1
            ORDER BY [IsDefault] DESC, [Name];
            """;
        var list = new List<ShiftScenarioDto>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
        {
            list.Add(new ShiftScenarioDto(
                Id: r.GetInt32(0),
                Name: r.GetString(1),
                Description: r.IsDBNull(2) ? null : r.GetString(2),
                IsDefault: r.GetBoolean(3),
                IsActive: r.GetBoolean(4)));
        }
        return list;
    }

    public async Task<ShiftScenarioDto?> GetScenarioAsync(int id, CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            SELECT [Id], [Name], [Description], [IsDefault], [IsActive]
            FROM {T("ShiftScenario")}
            WHERE [Id] = @Id AND [IsActive] = 1;
            """;
        cmd.Parameters.AddWithValue("@Id", id);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        if (!await r.ReadAsync(ct)) return null;
        return new ShiftScenarioDto(
            Id: r.GetInt32(0),
            Name: r.GetString(1),
            Description: r.IsDBNull(2) ? null : r.GetString(2),
            IsDefault: r.GetBoolean(3),
            IsActive: r.GetBoolean(4));
    }

    public async Task<int> SaveScenarioAsync(SaveShiftScenarioRequest request, int? userId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            throw new ArgumentException("Senaryo adı zorunludur.");

        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var tx = (SqlTransaction)await conn.BeginTransactionAsync(ct);
        try
        {
            if (request.IsDefault)
            {
                // Tek varsayılan — önce diğerlerini 0'a çek (UX_ShiftScenario_Default filtered
                // unique ile uyumlu; kendisi zaten güncelleniyorsa aşağıdaki UPDATE tekrar 1 yapar).
                await using var clear = conn.CreateCommand();
                clear.Transaction = tx;
                // SIRKET SUZGECI ZORUNLU: susuz hali TUM sirketlerin varsayilan senaryosunu
                // sifirliyordu — bir sirketin admini kendi varsayilanini secince digerlerinin
                // varsayilani sessizce kayboluyordu. Cok-sirketli tek DB'de gercek veri kaybi.
                clear.CommandText = $"UPDATE {T("ShiftScenario")} SET [IsDefault] = 0 WHERE [IsDefault] = 1 AND [CompanyId] = @CompanyId;";
                clear.Parameters.AddWithValue("@CompanyId", _connectionFactory.ResolveEffectiveCompanyId());
                await clear.ExecuteNonQueryAsync(ct);
            }

            int id;
            if (request.Id <= 0)
            {
                await using var ins = conn.CreateCommand();
                ins.Transaction = tx;
                ins.CommandText = $"""
                    INSERT INTO {T("ShiftScenario")}
                        ([Name],[Description],[IsDefault],[IsActive],[CreatedById],[Created])
                    VALUES
                        (@Name,@Description,@IsDefault,1,@CreatedById,SYSUTCDATETIME());
                    SELECT CAST(SCOPE_IDENTITY() AS INT);
                    """;
                ins.Parameters.AddWithValue("@Name", request.Name.Trim());
                ins.Parameters.Add(new SqlParameter("@Description", (object?)request.Description ?? DBNull.Value));
                ins.Parameters.AddWithValue("@IsDefault", request.IsDefault);
                ins.Parameters.Add(new SqlParameter("@CreatedById", (object?)(userId is > 0 ? userId : null) ?? DBNull.Value));
                id = (int)(await ins.ExecuteScalarAsync(ct))!;
            }
            else
            {
                await using var upd = conn.CreateCommand();
                upd.Transaction = tx;
                upd.CommandText = $"""
                    UPDATE {T("ShiftScenario")}
                    SET [Name] = @Name, [Description] = @Description, [IsDefault] = @IsDefault,
                        [UpdatedById] = @UpdatedById, [Updated] = SYSUTCDATETIME()
                    WHERE [Id] = @Id AND [IsActive] = 1;
                    """;
                upd.Parameters.AddWithValue("@Name", request.Name.Trim());
                upd.Parameters.Add(new SqlParameter("@Description", (object?)request.Description ?? DBNull.Value));
                upd.Parameters.AddWithValue("@IsDefault", request.IsDefault);
                upd.Parameters.Add(new SqlParameter("@UpdatedById", (object?)(userId is > 0 ? userId : null) ?? DBNull.Value));
                upd.Parameters.AddWithValue("@Id", request.Id);
                await upd.ExecuteNonQueryAsync(ct);
                id = request.Id;
            }

            await tx.CommitAsync(ct);
            return id;
        }
        catch
        {
            try { await tx.RollbackAsync(ct); } catch { /* ignore */ }
            throw;
        }
    }

    public async Task DeleteScenarioAsync(int id, int? userId, CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);

        await using (var chk = conn.CreateCommand())
        {
            chk.CommandText = $"SELECT [IsDefault] FROM {T("ShiftScenario")} WHERE [Id] = @Id AND [IsActive] = 1;";
            chk.Parameters.AddWithValue("@Id", id);
            var result = await chk.ExecuteScalarAsync(ct);
            if (result is null) return; // zaten yok/pasif — no-op
            if ((bool)result)
                throw new ArgumentException("Varsayılan senaryo silinemez. Önce başka bir senaryoyu varsayılan yapın.");
        }

        await using var cmd = conn.CreateCommand();
        // Senaryo silinince ait olduğu makine-vardiya atamaları da cascade soft-delete edilir
        // (frontend onay metni "atamalar da silinir" der + orphan satır türetmeye devam etmesin —
        // code review 2026-08-05). Türetme sorgusu ayrıca ShiftScenario.IsActive=1 join'i ile korunur.
        cmd.CommandText = $"""
            UPDATE {T("ShiftScenario")}
            SET [IsActive] = 0, [UpdatedById] = @UpdatedById, [Updated] = SYSUTCDATETIME()
            WHERE [Id] = @Id AND [IsActive] = 1;

            UPDATE {T("ScenarioMachineShift")}
            SET [IsActive] = 0, [UpdatedById] = @UpdatedById, [Updated] = SYSUTCDATETIME()
            WHERE [ScenarioId] = @Id AND [IsActive] = 1;
            """;
        cmd.Parameters.AddWithValue("@Id", id);
        cmd.Parameters.Add(new SqlParameter("@UpdatedById", (object?)(userId is > 0 ? userId : null) ?? DBNull.Value));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<IReadOnlyList<ScenarioMachineShiftDto>> ListScenarioMachineShiftsAsync(int scenarioId, CancellationToken ct)
    {
        var companyId = _connectionFactory.ResolveCurrentCompanyId();
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            SELECT sms.[Id], sms.[ScenarioId], sms.[MachineId], sms.[ShiftId], sms.[DaysMask],
                   m.[Name] AS MachineName, s.[Name] AS ShiftName
            FROM {T("ScenarioMachineShift")} sms
            INNER JOIN {T("Machine")} m ON m.[Id] = sms.[MachineId] AND m.[CompanyId] = @CompanyId
            INNER JOIN {T("Shift")} s ON s.[Id] = sms.[ShiftId]
            WHERE sms.[ScenarioId] = @ScenarioId AND sms.[IsActive] = 1
            ORDER BY m.[Name], s.[SortOrder];
            """;
        cmd.Parameters.AddWithValue("@CompanyId", companyId);
        cmd.Parameters.AddWithValue("@ScenarioId", scenarioId);

        var list = new List<ScenarioMachineShiftDto>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
        {
            list.Add(new ScenarioMachineShiftDto(
                Id: r.GetInt32(0),
                ScenarioId: r.GetInt32(1),
                MachineId: r.GetInt32(2),
                ShiftId: r.GetInt32(3),
                DaysMask: r.GetByte(4),
                MachineName: r.IsDBNull(5) ? null : r.GetString(5),
                ShiftName: r.IsDBNull(6) ? null : r.GetString(6)));
        }
        return list;
    }

    public async Task<int> SaveScenarioMachineShiftAsync(SaveScenarioMachineShiftRequest request, int? userId, CancellationToken ct)
    {
        if (request.ScenarioId <= 0) throw new ArgumentException("Senaryo seçilmelidir.");
        if (request.MachineId <= 0) throw new ArgumentException("Makine seçilmelidir.");
        if (request.ShiftId <= 0) throw new ArgumentException("Vardiya seçilmelidir.");
        if (request.DaysMask > 127) throw new ArgumentException("Geçersiz gün maskesi.");

        var companyId = _connectionFactory.ResolveCurrentCompanyId();
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);

        // Cross-company id guessing savunması + referans doğrulama (SaveWorkWindowAsync deseni).
        await using (var chk = conn.CreateCommand())
        {
            chk.CommandText = $"SELECT 1 FROM {T("Machine")} WHERE [Id] = @MachineId AND [CompanyId] = @CompanyId AND [IsActive] = 1;";
            chk.Parameters.AddWithValue("@MachineId", request.MachineId);
            chk.Parameters.AddWithValue("@CompanyId", companyId);
            if (await chk.ExecuteScalarAsync(ct) is null) throw new ArgumentException("Seçilen makine bulunamadı.");
        }
        await using (var chk = conn.CreateCommand())
        {
            chk.CommandText = $"SELECT 1 FROM {T("ShiftScenario")} WHERE [Id] = @ScenarioId AND [IsActive] = 1;";
            chk.Parameters.AddWithValue("@ScenarioId", request.ScenarioId);
            if (await chk.ExecuteScalarAsync(ct) is null) throw new ArgumentException("Seçilen senaryo bulunamadı.");
        }
        await using (var chk = conn.CreateCommand())
        {
            chk.CommandText = $"SELECT 1 FROM {T("Shift")} WHERE [Id] = @ShiftId AND [IsActive] = 1;";
            chk.Parameters.AddWithValue("@ShiftId", request.ShiftId);
            if (await chk.ExecuteScalarAsync(ct) is null) throw new ArgumentException("Seçilen vardiya bulunamadı.");
        }

        int id;
        if (request.Id <= 0)
        {
            await using var ins = conn.CreateCommand();
            ins.CommandText = $"""
                INSERT INTO {T("ScenarioMachineShift")}
                    ([ScenarioId],[MachineId],[ShiftId],[DaysMask],[IsActive],[CreatedById],[Created])
                VALUES
                    (@ScenarioId,@MachineId,@ShiftId,@DaysMask,1,@CreatedById,SYSUTCDATETIME());
                SELECT CAST(SCOPE_IDENTITY() AS INT);
                """;
            AddScenarioMachineShiftParams(ins, request);
            ins.Parameters.Add(new SqlParameter("@CreatedById", (object?)(userId is > 0 ? userId : null) ?? DBNull.Value));
            id = (int)(await ins.ExecuteScalarAsync(ct))!;
        }
        else
        {
            await using var upd = conn.CreateCommand();
            upd.CommandText = $"""
                UPDATE {T("ScenarioMachineShift")}
                SET [ScenarioId] = @ScenarioId, [MachineId] = @MachineId, [ShiftId] = @ShiftId,
                    [DaysMask] = @DaysMask, [UpdatedById] = @UpdatedById, [Updated] = SYSUTCDATETIME()
                WHERE [Id] = @Id AND [IsActive] = 1;
                """;
            AddScenarioMachineShiftParams(upd, request);
            upd.Parameters.Add(new SqlParameter("@UpdatedById", (object?)(userId is > 0 ? userId : null) ?? DBNull.Value));
            upd.Parameters.AddWithValue("@Id", request.Id);
            await upd.ExecuteNonQueryAsync(ct);
            id = request.Id;
        }
        return id;
    }

    private static void AddScenarioMachineShiftParams(SqlCommand cmd, SaveScenarioMachineShiftRequest r)
    {
        cmd.Parameters.AddWithValue("@ScenarioId", r.ScenarioId);
        cmd.Parameters.AddWithValue("@MachineId", r.MachineId);
        cmd.Parameters.AddWithValue("@ShiftId", r.ShiftId);
        cmd.Parameters.AddWithValue("@DaysMask", r.DaysMask);
    }

    public async Task DeleteScenarioMachineShiftAsync(int id, int? userId, CancellationToken ct)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            UPDATE {T("ScenarioMachineShift")}
            SET [IsActive] = 0, [UpdatedById] = @UpdatedById, [Updated] = SYSUTCDATETIME()
            WHERE [Id] = @Id AND [IsActive] = 1;
            """;
        cmd.Parameters.AddWithValue("@Id", id);
        cmd.Parameters.Add(new SqlParameter("@UpdatedById", (object?)(userId is > 0 ? userId : null) ?? DBNull.Value));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    // ── Vardiya Senaryoları Faz 2 — Personel kısıtı (2026-08-05) ──────────────────────

    /// <summary>
    /// <see cref="ListWorkWindowsAsync"/> ile BİREBİR AYNI türetme (senaryo çöz → ScenarioMachineShift
    /// × Shift join → DaysMask set-bit'lerine göre gün başına pencere, overnight ise iki güne böl),
    /// tek fark: <c>ShiftId</c> her türetilmiş pencereye iliştirilir (kilitli <see cref="MachineWorkWindowDto"/>
    /// bunu taşımaz). Legacy fallback'te <c>ShiftId=null</c> — motor bu pencerelerde personel kısıtını
    /// hiç uygulamaz (fail-open).
    /// </summary>
    public async Task<IReadOnlyList<MachineShiftWindowDto>> ListScenarioMachineShiftWindowsAsync(int? scenarioId, CancellationToken ct)
    {
        var companyId = _connectionFactory.ResolveCurrentCompanyId();
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);

        var resolvedScenarioId = scenarioId;
        if (resolvedScenarioId is null)
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = $"SELECT TOP 1 [Id] FROM {T("ShiftScenario")} WHERE [IsDefault] = 1 AND [IsActive] = 1;";
            var found = await cmd.ExecuteScalarAsync(ct);
            resolvedScenarioId = found is int i ? i : (int?)null;
        }

        var derived = new List<MachineShiftWindowDto>();
        if (resolvedScenarioId is > 0)
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = $"""
                SELECT sms.[MachineId], sms.[ShiftId], sms.[DaysMask], s.[StartTime], s.[EndTime], s.[IsOvernight]
                FROM {T("ScenarioMachineShift")} sms
                INNER JOIN {T("ShiftScenario")} sc ON sc.[Id] = sms.[ScenarioId] AND sc.[IsActive] = 1
                INNER JOIN {T("Shift")} s ON s.[Id] = sms.[ShiftId] AND s.[IsActive] = 1
                INNER JOIN {T("Machine")} m ON m.[Id] = sms.[MachineId] AND m.[CompanyId] = @CompanyId AND m.[IsActive] = 1
                WHERE sms.[ScenarioId] = @ScenarioId AND sms.[IsActive] = 1;
                """;
            cmd.Parameters.AddWithValue("@CompanyId", companyId);
            cmd.Parameters.AddWithValue("@ScenarioId", resolvedScenarioId.Value);

            await using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct))
            {
                var machineId = r.GetInt32(0);
                var shiftId = r.GetInt32(1);
                var daysMask = r.GetByte(2);
                var start = (TimeSpan)r.GetValue(3);
                var end = (TimeSpan)r.GetValue(4);
                var isOvernight = r.GetBoolean(5);
                var startMin = (short)start.TotalMinutes;
                var endMin = (short)end.TotalMinutes;

                for (var d = 0; d < 7; d++)
                {
                    if ((daysMask & (1 << d)) == 0) continue;
                    var day = (byte)d;

                    if (!isOvernight)
                    {
                        derived.Add(new MachineShiftWindowDto(machineId, day, startMin, endMin, shiftId));
                    }
                    else
                    {
                        derived.Add(new MachineShiftWindowDto(machineId, day, startMin, 1440, shiftId));
                        if (endMin > 0)
                        {
                            var nextDay = (byte)((day + 1) % 7);
                            derived.Add(new MachineShiftWindowDto(machineId, nextDay, 0, endMin, shiftId));
                        }
                    }
                }
            }
        }

        if (derived.Count > 0) return derived;

        // Legacy fallback — ShiftId=null (fail-open: personel kısıtı bu pencerelerde uygulanmaz).
        var legacy = await ListLegacyWorkWindowsAsync(conn, companyId, ct);
        return legacy.Select(w => new MachineShiftWindowDto(w.MachineId, w.DayOfWeek, w.StartMinute, w.EndMinute, null)).ToList();
    }

    /// <summary>
    /// (ShiftId, DayOfWeek) → aktif üretim-operatörü sayısı. <c>ShiftAssignment.DayOfWeek</c> TINYINT
    /// kolonu .NET <see cref="DayOfWeek"/> değerleriyle (0=Pazar..6=Cumartesi) BİREBİR aynı — motor
    /// tarafında ayrıca dönüşüm gerekmez.
    /// </summary>
    public async Task<IReadOnlyDictionary<(int ShiftId, byte DayOfWeek), int>> GetShiftOperatorPoolAsync(CancellationToken ct)
    {
        var companyId = _connectionFactory.ResolveCurrentCompanyId();
        await using var conn = await _connectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            SELECT sa.[ShiftId], sa.[DayOfWeek], COUNT(DISTINCT sa.[PersonnelId])
            FROM {T("ShiftAssignment")} sa
            INNER JOIN {T("Personnel")} p ON p.[Id] = sa.[PersonnelId]
                AND p.[CompanyId] = @CompanyId AND p.[IsProductionOperator] = 1 AND p.[IsActive] = 1
            WHERE sa.[IsActive] = 1
            GROUP BY sa.[ShiftId], sa.[DayOfWeek];
            """;
        cmd.Parameters.AddWithValue("@CompanyId", companyId);

        var dict = new Dictionary<(int, byte), int>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
        {
            dict[(r.GetInt32(0), r.GetByte(1))] = r.GetInt32(2);
        }
        return dict;
    }
}
