namespace CalibraHub.Application.Abstractions.Persistence;

public interface IUserSettingRepository
{
    Task<string?> GetAsync(int userId, string settingKey, CancellationToken cancellationToken);
    Task SetAsync(int userId, string settingKey, string? value, CancellationToken cancellationToken);
}
