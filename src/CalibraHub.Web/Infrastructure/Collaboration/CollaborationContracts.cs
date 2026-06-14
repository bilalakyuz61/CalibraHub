namespace CalibraHub.Web.Infrastructure.Collaboration;

public static class CollaborationRuntimeDefaults
{
    public static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(15);
    public static readonly TimeSpan PresenceTimeout = TimeSpan.FromSeconds(45);
    public static readonly TimeSpan LockTimeout = TimeSpan.FromSeconds(45);
}

public static class CollaborationGroupNames
{
    public static string ForUser(int userId) => $"collab:user:{userId}";

    public static string ForRecord(string recordType, string recordId) =>
        $"collab:record:{Normalize(recordType)}:{Normalize(recordId)}";

    public static string ForRecord(CollaborationRecordReference record) =>
        ForRecord(record.RecordType, record.RecordId);

    public static string BuildRecordKey(string recordType, string recordId) =>
        $"{Normalize(recordType)}::{Normalize(recordId)}";

    public static string Normalize(string value) =>
        string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : value.Trim().ToLowerInvariant();
}

public sealed record CollaborationRecordReference(string RecordType, string RecordId);

public sealed record CollaborationUserDescriptor(int UserId, string DisplayName);

public sealed record CollaborationPresenceUser(int UserId, string DisplayName, int ConnectionCount);

public sealed record CollaborationLockSnapshot(
    string RecordType,
    string RecordId,
    int OwnerUserId,
    string OwnerDisplayName,
    string? RecordTitle,
    string? PageUrl,
    DateTime AcquiredAt,
    DateTime LastHeartbeatAt);

public sealed record CollaborationRecordLockChangedEvent(
    string RecordType,
    string RecordId,
    CollaborationLockSnapshot? Lock);

public sealed record CollaborationLockAcquireRequest(
    string SessionId,
    string RecordType,
    string RecordId,
    string? RecordTitle,
    string? PageUrl);

public sealed record CollaborationLockAcquireResult(
    bool Granted,
    CollaborationLockSnapshot? Lock,
    string? Message);

public sealed record CollaborationHeartbeatRequest(
    string SessionId,
    IReadOnlyCollection<CollaborationRecordReference>? Records);

public sealed record CollaborationDirectMessageRequest(
    int RecipientUserId,
    string Message,
    string? RecordType,
    string? RecordId,
    string? RecordTitle);

public sealed record CollaborationDirectMessage(
    Guid MessageId,
    int SenderUserId,
    string SenderDisplayName,
    int RecipientUserId,
    string Message,
    DateTime SentAt,
    string? RecordType,
    string? RecordId,
    string? RecordTitle);

public sealed record CollaborationReleaseApiRequest(
    string SessionId,
    IReadOnlyCollection<CollaborationRecordReference>? Records);

public sealed record CollaborationExpirationResult(
    bool PresenceChanged,
    IReadOnlyCollection<CollaborationRecordLockChangedEvent> ReleasedLocks);

public sealed record CollaborationDisconnectResult(
    bool PresenceChanged,
    IReadOnlyCollection<CollaborationRecordLockChangedEvent> ReleasedLocks);

public sealed record CollaborationBreakLockRequest(string RecordType, string RecordId);
