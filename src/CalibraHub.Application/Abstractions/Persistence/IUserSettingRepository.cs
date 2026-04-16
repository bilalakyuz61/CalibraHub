namespace CalibraHub.Application.Abstractions.Persistence;

public interface IUserSettingRepository
{
    Task<string?> GetAsync(Guid userId, string settingKey, CancellationToken cancellationToken);
    Task SetAsync(Guid userId, string settingKey, string? value, CancellationToken cancellationToken);
}
