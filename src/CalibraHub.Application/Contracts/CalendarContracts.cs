namespace CalibraHub.Application.Contracts;

public sealed record CalendarEventDto(
    int Id,
    string Title,
    string? Description,
    string StartDate,   // "YYYY-MM-DD"
    string? EndDate,
    bool IsAllDay,
    string? StartTime,  // "HH:mm" (only when IsAllDay=false)
    string? EndTime,
    string? Color,      // indigo | emerald | rose | amber | blue | violet | slate
    string Source       // "personal" | "work-order" | "birthday"
);

public sealed record SaveCalendarEventRequest(
    int? Id,
    string Title,
    string? Description,
    string StartDate,
    string? EndDate,
    bool IsAllDay,
    string? StartTime,
    string? EndTime,
    string? Color
);

public sealed record DeleteCalendarEventRequest(int Id);
