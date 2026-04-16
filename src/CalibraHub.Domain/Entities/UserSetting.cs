using CalibraHub.Domain.Common;

namespace CalibraHub.Domain.Entities;

public sealed class UserSetting : Entity
{
    public Guid UserId { get; init; }
    public required string SettingKey { get; init; }
    public string? SettingValue { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.Now;
}
