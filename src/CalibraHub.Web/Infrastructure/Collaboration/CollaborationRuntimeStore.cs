namespace CalibraHub.Web.Infrastructure.Collaboration;

public sealed class CollaborationRuntimeStore
{
    private readonly object _gate = new();
    private readonly Dictionary<string, ConnectionState> _connectionsById = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, RecordLockState> _locksByRecordKey = new(StringComparer.OrdinalIgnoreCase);

    public void RegisterConnection(
        string connectionId,
        string sessionId,
        CollaborationUserDescriptor user,
        DateTime now)
    {
        lock (_gate)
        {
            _connectionsById[connectionId] = new ConnectionState(
                connectionId,
                sessionId,
                user.UserId,
                user.DisplayName,
                now,
                now);
        }
    }

    public CollaborationDisconnectResult UnregisterConnection(string connectionId)
    {
        lock (_gate)
        {
            var presenceChanged = _connectionsById.Remove(connectionId);
            var releasedLocks = ReleaseLocksUnsafe(lockState =>
                string.Equals(lockState.ConnectionId, connectionId, StringComparison.OrdinalIgnoreCase));

            return new CollaborationDisconnectResult(presenceChanged, releasedLocks);
        }
    }

    public IReadOnlyCollection<CollaborationPresenceUser> GetPresenceSnapshot()
    {
        lock (_gate)
        {
            return _connectionsById.Values
                .GroupBy(x => x.UserId)
                .Select(group =>
                {
                    var displayName = group
                        .OrderByDescending(x => x.LastSeenAt)
                        .Select(x => x.DisplayName)
                        .FirstOrDefault() ?? "Kullanici";

                    return new CollaborationPresenceUser(group.Key, displayName, group.Count());
                })
                .OrderBy(x => x.DisplayName, StringComparer.CurrentCultureIgnoreCase)
                .ToArray();
        }
    }

    public CollaborationRecordLockChangedEvent GetRecordState(string recordType, string recordId, DateTime now)
    {
        lock (_gate)
        {
            var normalizedType = CollaborationGroupNames.Normalize(recordType);
            var normalizedId = CollaborationGroupNames.Normalize(recordId);
            var recordKey = CollaborationGroupNames.BuildRecordKey(normalizedType, normalizedId);

            if (!_locksByRecordKey.TryGetValue(recordKey, out var lockState))
            {
                return new CollaborationRecordLockChangedEvent(normalizedType, normalizedId, null);
            }

            if (IsExpired(lockState, now))
            {
                _locksByRecordKey.Remove(recordKey);
                return new CollaborationRecordLockChangedEvent(normalizedType, normalizedId, null);
            }

            return ToLockChangedEvent(lockState);
        }
    }

    public CollaborationLockAcquireResult TryAcquireLock(
        CollaborationUserDescriptor user,
        string connectionId,
        CollaborationLockAcquireRequest request,
        DateTime now)
    {
        lock (_gate)
        {
            TouchConnectionUnsafe(connectionId, now);

            var normalizedType = CollaborationGroupNames.Normalize(request.RecordType);
            var normalizedId = CollaborationGroupNames.Normalize(request.RecordId);
            var normalizedSessionId = NormalizeSessionId(request.SessionId);
            var recordKey = CollaborationGroupNames.BuildRecordKey(normalizedType, normalizedId);

            if (_locksByRecordKey.TryGetValue(recordKey, out var existing) && IsExpired(existing, now))
            {
                _locksByRecordKey.Remove(recordKey);
                existing = null;
            }

            if (existing is null)
            {
                var lockState = new RecordLockState(
                    recordKey,
                    normalizedType,
                    normalizedId,
                    user.UserId,
                    user.DisplayName,
                    normalizedSessionId,
                    connectionId,
                    string.IsNullOrWhiteSpace(request.RecordTitle) ? null : request.RecordTitle.Trim(),
                    string.IsNullOrWhiteSpace(request.PageUrl) ? null : request.PageUrl.Trim(),
                    now,
                    now);

                _locksByRecordKey[recordKey] = lockState;
                return new CollaborationLockAcquireResult(true, ToSnapshot(lockState), null);
            }

            if (existing.OwnerUserId == user.UserId ||
                string.Equals(existing.SessionId, normalizedSessionId, StringComparison.Ordinal))
            {
                existing = existing with
                {
                    OwnerDisplayName = user.DisplayName,
                    SessionId = normalizedSessionId,
                    ConnectionId = connectionId,
                    RecordTitle = string.IsNullOrWhiteSpace(request.RecordTitle) ? existing.RecordTitle : request.RecordTitle.Trim(),
                    PageUrl = string.IsNullOrWhiteSpace(request.PageUrl) ? existing.PageUrl : request.PageUrl.Trim(),
                    LastHeartbeatAt = now
                };

                _locksByRecordKey[recordKey] = existing;
                return new CollaborationLockAcquireResult(true, ToSnapshot(existing), null);
            }

            return new CollaborationLockAcquireResult(false, ToSnapshot(existing), "Kayit baska bir kullanici tarafindan duzenleniyor.");
        }
    }

    public bool ReleaseLock(
        Guid userId,
        string sessionId,
        CollaborationRecordReference record,
        out CollaborationRecordLockChangedEvent releasedEvent)
    {
        lock (_gate)
        {
            var normalizedType = CollaborationGroupNames.Normalize(record.RecordType);
            var normalizedId = CollaborationGroupNames.Normalize(record.RecordId);
            var recordKey = CollaborationGroupNames.BuildRecordKey(normalizedType, normalizedId);

            if (_locksByRecordKey.TryGetValue(recordKey, out var lockState) &&
                lockState.OwnerUserId == userId &&
                string.Equals(lockState.SessionId, NormalizeSessionId(sessionId), StringComparison.Ordinal))
            {
                _locksByRecordKey.Remove(recordKey);
                releasedEvent = new CollaborationRecordLockChangedEvent(normalizedType, normalizedId, null);
                return true;
            }

            releasedEvent = new CollaborationRecordLockChangedEvent(normalizedType, normalizedId, null);
            return false;
        }
    }

    public IReadOnlyCollection<CollaborationRecordLockChangedEvent> ReleaseLocksForSession(
        Guid userId,
        string sessionId,
        IReadOnlyCollection<CollaborationRecordReference>? records)
    {
        lock (_gate)
        {
            var normalizedSessionId = NormalizeSessionId(sessionId);
            if (records is { Count: > 0 })
            {
                var released = new List<CollaborationRecordLockChangedEvent>();
                foreach (var record in records)
                {
                    var recordKey = CollaborationGroupNames.BuildRecordKey(record.RecordType, record.RecordId);
                    if (_locksByRecordKey.TryGetValue(recordKey, out var lockState) &&
                        lockState.OwnerUserId == userId &&
                        string.Equals(lockState.SessionId, normalizedSessionId, StringComparison.Ordinal))
                    {
                        _locksByRecordKey.Remove(recordKey);
                        released.Add(new CollaborationRecordLockChangedEvent(lockState.RecordType, lockState.RecordId, null));
                    }
                }

                return released;
            }

            return ReleaseLocksUnsafe(lockState =>
                lockState.OwnerUserId == userId &&
                string.Equals(lockState.SessionId, normalizedSessionId, StringComparison.Ordinal));
        }
    }

    public void Heartbeat(
        Guid userId,
        string connectionId,
        string sessionId,
        IReadOnlyCollection<CollaborationRecordReference>? activeRecords,
        DateTime now)
    {
        lock (_gate)
        {
            TouchConnectionUnsafe(connectionId, now);
            var normalizedSessionId = NormalizeSessionId(sessionId);

            if (activeRecords is null)
            {
                return;
            }

            foreach (var record in activeRecords)
            {
                var recordKey = CollaborationGroupNames.BuildRecordKey(record.RecordType, record.RecordId);
                if (!_locksByRecordKey.TryGetValue(recordKey, out var lockState))
                {
                    continue;
                }

                if (lockState.OwnerUserId != userId ||
                    !string.Equals(lockState.SessionId, normalizedSessionId, StringComparison.Ordinal))
                {
                    continue;
                }

                _locksByRecordKey[recordKey] = lockState with
                {
                    ConnectionId = connectionId,
                    LastHeartbeatAt = now
                };
            }
        }
    }

    public CollaborationExpirationResult ExpireStaleEntries(DateTime now)
    {
        lock (_gate)
        {
            var staleConnections = _connectionsById.Values
                .Where(x => now - x.LastSeenAt > CollaborationRuntimeDefaults.PresenceTimeout)
                .Select(x => x.ConnectionId)
                .ToArray();

            var presenceChanged = staleConnections.Length > 0;
            foreach (var staleConnectionId in staleConnections)
            {
                _connectionsById.Remove(staleConnectionId);
            }

            var releasedByTimeout = ReleaseLocksUnsafe(lockState =>
                now - lockState.LastHeartbeatAt > CollaborationRuntimeDefaults.LockTimeout ||
                staleConnections.Contains(lockState.ConnectionId, StringComparer.OrdinalIgnoreCase));

            return new CollaborationExpirationResult(presenceChanged, releasedByTimeout);
        }
    }

    private static bool IsExpired(RecordLockState lockState, DateTime now) =>
        now - lockState.LastHeartbeatAt > CollaborationRuntimeDefaults.LockTimeout;

    private IReadOnlyCollection<CollaborationRecordLockChangedEvent> ReleaseLocksUnsafe(Func<RecordLockState, bool> predicate)
    {
        var releasedKeys = _locksByRecordKey
            .Where(x => predicate(x.Value))
            .Select(x => x.Key)
            .ToArray();

        if (releasedKeys.Length == 0)
        {
            return Array.Empty<CollaborationRecordLockChangedEvent>();
        }

        var released = new List<CollaborationRecordLockChangedEvent>(releasedKeys.Length);
        foreach (var recordKey in releasedKeys)
        {
            if (!_locksByRecordKey.Remove(recordKey, out var lockState))
            {
                continue;
            }

            released.Add(new CollaborationRecordLockChangedEvent(lockState.RecordType, lockState.RecordId, null));
        }

        return released;
    }

    private void TouchConnectionUnsafe(string connectionId, DateTime now)
    {
        if (!_connectionsById.TryGetValue(connectionId, out var connection))
        {
            return;
        }

        _connectionsById[connectionId] = connection with
        {
            LastSeenAt = now
        };
    }

    private static string NormalizeSessionId(string sessionId) =>
        string.IsNullOrWhiteSpace(sessionId)
            ? string.Empty
            : sessionId.Trim();

    private static CollaborationLockSnapshot ToSnapshot(RecordLockState lockState) =>
        new(
            lockState.RecordType,
            lockState.RecordId,
            lockState.OwnerUserId,
            lockState.OwnerDisplayName,
            lockState.RecordTitle,
            lockState.PageUrl,
            lockState.AcquiredAt,
            lockState.LastHeartbeatAt);

    private static CollaborationRecordLockChangedEvent ToLockChangedEvent(RecordLockState lockState) =>
        new(lockState.RecordType, lockState.RecordId, ToSnapshot(lockState));

    private sealed record ConnectionState(
        string ConnectionId,
        string SessionId,
        Guid UserId,
        string DisplayName,
        DateTime ConnectedAt,
        DateTime LastSeenAt);

    private sealed record RecordLockState(
        string RecordKey,
        string RecordType,
        string RecordId,
        Guid OwnerUserId,
        string OwnerDisplayName,
        string SessionId,
        string ConnectionId,
        string? RecordTitle,
        string? PageUrl,
        DateTime AcquiredAt,
        DateTime LastHeartbeatAt);
}
