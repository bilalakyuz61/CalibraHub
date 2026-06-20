using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Application.Contracts;

namespace CalibraHub.Application.Services.Calendar;

public sealed class CalendarService
{
    private readonly ICalendarRepository _repo;

    public CalendarService(ICalendarRepository repo)
    {
        _repo = repo;
    }

    public async Task<IEnumerable<CalendarEventDto>> GetEventsAsync(
        int userId, string start, string end, CancellationToken ct)
    {
        var personal    = await _repo.GetPersonalEventsAsync(userId, start, end, ct);
        var workOrders  = await _repo.GetWorkOrderEventsAsync(start, end, ct);
        var birthdays   = await _repo.GetBirthdayEventsAsync(start, end, ct);
        var holidays    = TurkishHolidayProvider.GetForRange(start, end);
        return personal.Concat(workOrders).Concat(birthdays).Concat(holidays);
    }

    public Task<int> SaveEventAsync(int userId, SaveCalendarEventRequest req, string username, CancellationToken ct)
        => _repo.SaveAsync(userId, req, username, ct);

    public Task DeleteEventAsync(int userId, int id, string username, CancellationToken ct)
        => _repo.DeleteAsync(userId, id, username, ct);
}
