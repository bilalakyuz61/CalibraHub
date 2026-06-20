using CalibraHub.Application.Contracts;

namespace CalibraHub.Application.Abstractions.Persistence;

public interface ICalendarRepository
{
    Task<IReadOnlyList<CalendarEventDto>> GetPersonalEventsAsync(int userId, string start, string end, CancellationToken ct);
    Task<IReadOnlyList<CalendarEventDto>> GetWorkOrderEventsAsync(string start, string end, CancellationToken ct);
    Task<IReadOnlyList<CalendarEventDto>> GetBirthdayEventsAsync(string start, string end, CancellationToken ct);
    Task<int> SaveAsync(int userId, SaveCalendarEventRequest req, string username, CancellationToken ct);
    Task DeleteAsync(int userId, int id, string username, CancellationToken ct);
}
