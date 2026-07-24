using CalibraHub.Domain.Entities;

namespace CalibraHub.Application.Abstractions.Persistence;

public interface IUserNotificationRepository
{
    Task AddAsync(UserNotification notification, CancellationToken cancellationToken);

    /// <summary>Kullanicinin son N bildirimini (once okunmamis, sonra yenilik sirasinda) doner.</summary>
    Task<IReadOnlyCollection<UserNotification>> GetRecentAsync(int userId, int take, CancellationToken cancellationToken);

    Task<int> GetUnreadCountAsync(int userId, CancellationToken cancellationToken);

    Task MarkReadAsync(int notificationId, int userId, DateTime readAt, CancellationToken cancellationToken);

    Task MarkAllReadAsync(int userId, DateTime readAt, CancellationToken cancellationToken);

    /// <summary>Sabitleme durumunu tersine cevirir (pin/unpin toggle). Yeni IsPinned degerini doner.</summary>
    Task<bool> TogglePinAsync(int notificationId, int userId, CancellationToken cancellationToken);

    /// <summary>Bildirimi kalici olarak siler. Tablo IsActive tasimadigindan hard delete uygulanir.</summary>
    Task DeleteAsync(int notificationId, int userId, CancellationToken cancellationToken);
}
