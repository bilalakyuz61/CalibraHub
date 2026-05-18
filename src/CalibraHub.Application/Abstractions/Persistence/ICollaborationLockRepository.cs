namespace CalibraHub.Application.Abstractions.Persistence;

public interface ICollaborationLockRepository
{
    /// <summary>Startup'ta DB'den aktif kilitleri yükler.</summary>
    Task<IReadOnlyCollection<GlobalLockRecord>> GetActiveAsync(CancellationToken ct);

    /// <summary>Background servis tarafından periyodik olarak çağrılır — memory snapshot → DB MERGE.</summary>
    Task FullSyncAsync(IReadOnlyCollection<GlobalLockRecord> activeLocks, CancellationToken ct);

    /// <summary>Kullanıcı sayfayı kapattığında anlık bırakma.</summary>
    Task ReleaseAsync(string recordType, string recordId, string sessionId, CancellationToken ct);

    /// <summary>Session bazlı tüm kilitleri bırakma.</summary>
    Task ReleaseAllForSessionAsync(string sessionId, CancellationToken ct);

    /// <summary>Admin kilidi kırma.</summary>
    Task AdminBreakAsync(string recordType, string recordId, CancellationToken ct);

    /// <summary>Süre dolmuş kayıtları temizle.</summary>
    Task CleanExpiredAsync(TimeSpan timeout, CancellationToken ct);
}

public sealed record GlobalLockRecord(
    string RecordType,
    string RecordId,
    string UserId,
    string UserName,
    string SessionId,
    string? RecordTitle,
    string? PageUrl,
    DateTime AcquiredAt,
    DateTime LastHeartbeatAt);
