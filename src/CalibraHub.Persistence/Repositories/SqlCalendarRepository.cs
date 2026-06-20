using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Application.Contracts;
using CalibraHub.Persistence.Database;

namespace CalibraHub.Persistence.Repositories;

public sealed class SqlCalendarRepository : ICalendarRepository
{
    private readonly SqlServerConnectionFactory _factory;

    public SqlCalendarRepository(SqlServerConnectionFactory factory)
    {
        _factory = factory;
    }

    public async Task<IReadOnlyList<CalendarEventDto>> GetPersonalEventsAsync(
        int userId, string start, string end, CancellationToken ct)
    {
        await using var conn = await _factory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT Id, Title, Description,
                   CONVERT(nvarchar(10), StartDate, 120) AS StartDate,
                   CASE WHEN EndDate IS NULL THEN NULL ELSE CONVERT(nvarchar(10), EndDate, 120) END AS EndDate,
                   IsAllDay,
                   CASE WHEN IsAllDay = 0 THEN FORMAT(StartDate, 'HH:mm') ELSE NULL END AS StartTime,
                   CASE WHEN IsAllDay = 0 AND EndDate IS NOT NULL THEN FORMAT(EndDate, 'HH:mm') ELSE NULL END AS EndTime,
                   Color
            FROM dbo.CalendarEvent
            WHERE UserId = @UserId
              AND IsActive = 1
              AND CONVERT(date, StartDate) <= CONVERT(date, @End)
              AND CONVERT(date, ISNULL(EndDate, StartDate)) >= CONVERT(date, @Start)
            ORDER BY StartDate";
        cmd.Parameters.AddWithValue("@UserId", userId);
        cmd.Parameters.AddWithValue("@Start", start);
        cmd.Parameters.AddWithValue("@End", end);

        var list = new List<CalendarEventDto>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
        {
            list.Add(new CalendarEventDto(
                Id: r.GetInt32(0),
                Title: r.GetString(1),
                Description: r.IsDBNull(2) ? null : r.GetString(2),
                StartDate: r.GetString(3),
                EndDate: r.IsDBNull(4) ? null : r.GetString(4),
                IsAllDay: r.GetBoolean(5),
                StartTime: r.IsDBNull(6) ? null : r.GetString(6),
                EndTime: r.IsDBNull(7) ? null : r.GetString(7),
                Color: r.IsDBNull(8) ? null : r.GetString(8),
                Source: "personal"
            ));
        }
        return list;
    }

    public async Task<int> SaveAsync(int userId, SaveCalendarEventRequest req, string username, CancellationToken ct)
    {
        await using var conn = await _factory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();

        var startDt = ParseDateTime(req.StartDate, req.IsAllDay ? null : req.StartTime);
        var endDt = string.IsNullOrEmpty(req.EndDate) ? (object)DBNull.Value
            : ParseDateTime(req.EndDate, req.IsAllDay ? null : req.EndTime);

        if (req.Id.HasValue && req.Id.Value > 0)
        {
            cmd.CommandText = @"
                UPDATE dbo.CalendarEvent
                SET Title = @Title, Description = @Description,
                    StartDate = @StartDate, EndDate = @EndDate,
                    IsAllDay = @IsAllDay, Color = @Color,
                    UpdatedBy = @User, Updated = SYSUTCDATETIME()
                WHERE Id = @Id AND UserId = @UserId AND IsActive = 1;
                SELECT @Id;";
            cmd.Parameters.AddWithValue("@Id", req.Id.Value);
        }
        else
        {
            cmd.CommandText = @"
                INSERT INTO dbo.CalendarEvent
                    (Title, Description, StartDate, EndDate, IsAllDay, Color, UserId, IsActive, CreatedBy, Created)
                VALUES
                    (@Title, @Description, @StartDate, @EndDate, @IsAllDay, @Color, @UserId, 1, @User, SYSUTCDATETIME());
                SELECT CAST(SCOPE_IDENTITY() AS INT);";
        }

        cmd.Parameters.AddWithValue("@Title", req.Title);
        cmd.Parameters.AddWithValue("@Description", (object?)req.Description ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@StartDate", startDt);
        cmd.Parameters.AddWithValue("@EndDate", endDt);
        cmd.Parameters.AddWithValue("@IsAllDay", req.IsAllDay);
        cmd.Parameters.AddWithValue("@Color", (object?)req.Color ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@UserId", userId);
        cmd.Parameters.AddWithValue("@User", username);

        return Convert.ToInt32(await cmd.ExecuteScalarAsync(ct));
    }

    public async Task<IReadOnlyList<CalendarEventDto>> GetWorkOrderEventsAsync(
        string start, string end, CancellationToken ct)
    {
        try
        {
            var companyId = _factory.ResolveCurrentCompanyId();
            await using var conn = await _factory.OpenConnectionAsync(ct);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT wo.Id,
                       wo.OrderNumber + ISNULL(N' — ' + i.Name, N'') AS Title,
                       CONVERT(nvarchar(10), wo.PlannedStartDate, 120) AS StartDate,
                       CASE WHEN wo.PlannedEndDate IS NULL THEN NULL
                            ELSE CONVERT(nvarchar(10), wo.PlannedEndDate, 120) END AS EndDate
                FROM dbo.WorkOrder wo
                LEFT JOIN dbo.Items i ON i.Id = wo.ItemId
                WHERE wo.CompanyId = @CompanyId
                  AND wo.IsActive = 1
                  AND wo.PlannedStartDate IS NOT NULL
                  AND CONVERT(date, wo.PlannedStartDate) <= CONVERT(date, @End)
                  AND CONVERT(date, ISNULL(wo.PlannedEndDate, wo.PlannedStartDate)) >= CONVERT(date, @Start)
                ORDER BY wo.PlannedStartDate";
            cmd.Parameters.AddWithValue("@CompanyId", companyId);
            cmd.Parameters.AddWithValue("@Start", start);
            cmd.Parameters.AddWithValue("@End", end);

            var list = new List<CalendarEventDto>();
            await using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct))
            {
                var endDate = r.IsDBNull(3) ? r.GetString(2) : r.GetString(3);
                list.Add(new CalendarEventDto(
                    Id: r.GetInt32(0),
                    Title: r.GetString(1),
                    Description: null,
                    StartDate: r.GetString(2),
                    EndDate: endDate,
                    IsAllDay: true,
                    StartTime: null,
                    EndTime: null,
                    Color: "amber",
                    Source: "work-order"
                ));
            }
            return list;
        }
        catch
        {
            return [];
        }
    }

    public async Task<IReadOnlyList<CalendarEventDto>> GetBirthdayEventsAsync(
        string start, string end, CancellationToken ct)
    {
        try
        {
            var companyId = _factory.ResolveCurrentCompanyId();
            await using var conn = await _factory.OpenConnectionAsync(ct);
            await using var cmd = conn.CreateCommand();
            // Her yıl yinelenen doğum günlerini bul: aralıkta hangi yıl(lar)da gün/ay eşleşiyor?
            cmd.CommandText = @"
                WITH YearsInRange AS (
                    SELECT YEAR(CONVERT(date, @Start)) AS Y
                    UNION
                    SELECT YEAR(CONVERT(date, @End))
                )
                SELECT
                    p.[Id],
                    p.[FullName] + N' — Doğum Günü' AS Title,
                    CONVERT(nvarchar(10),
                        DATEFROMPARTS(yr.Y, MONTH(p.[BirthDate]), DAY(p.[BirthDate])),
                        120) AS StartDate
                FROM dbo.Personnel p
                CROSS JOIN YearsInRange yr
                WHERE p.[BirthDate] IS NOT NULL
                  AND p.[IsActive] = 1
                  AND p.[CompanyId] = @CompanyId
                  AND DATEFROMPARTS(yr.Y, MONTH(p.[BirthDate]), DAY(p.[BirthDate]))
                      BETWEEN CONVERT(date, @Start) AND CONVERT(date, @End)
                ORDER BY StartDate";
            cmd.Parameters.AddWithValue("@CompanyId", companyId);
            cmd.Parameters.AddWithValue("@Start", start);
            cmd.Parameters.AddWithValue("@End", end);

            var list = new List<CalendarEventDto>();
            await using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct))
            {
                list.Add(new CalendarEventDto(
                    Id: r.GetInt32(0),
                    Title: r.GetString(1),
                    Description: null,
                    StartDate: r.GetString(2),
                    EndDate: null,
                    IsAllDay: true,
                    StartTime: null,
                    EndTime: null,
                    Color: "emerald",
                    Source: "birthday"
                ));
            }
            return list;
        }
        catch
        {
            return [];
        }
    }

    public async Task DeleteAsync(int userId, int id, string username, CancellationToken ct)
    {
        await using var conn = await _factory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            UPDATE dbo.CalendarEvent
            SET IsActive = 0, UpdatedBy = @User, Updated = SYSUTCDATETIME()
            WHERE Id = @Id AND UserId = @UserId;";
        cmd.Parameters.AddWithValue("@Id", id);
        cmd.Parameters.AddWithValue("@UserId", userId);
        cmd.Parameters.AddWithValue("@User", username);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static DateTime ParseDateTime(string date, string? time)
    {
        var d = DateTime.Parse(date, null, System.Globalization.DateTimeStyles.None);
        if (!string.IsNullOrEmpty(time) && time.Length >= 5)
        {
            var h = int.Parse(time[..2]);
            var m = int.Parse(time[3..5]);
            d = d.AddHours(h).AddMinutes(m);
        }
        return d;
    }
}
