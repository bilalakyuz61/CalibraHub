using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace CalibraHub.Web.Infrastructure.Collaboration;

[Authorize]
public sealed class CollaborationHub : Hub
{
    private readonly CollaborationRuntimeStore _runtimeStore;

    public CollaborationHub(CollaborationRuntimeStore runtimeStore)
    {
        _runtimeStore = runtimeStore;
    }

    public override async Task OnConnectedAsync()
    {
        if (!TryResolveUser(out var user))
        {
            Context.Abort();
            return;
        }

        _runtimeStore.RegisterConnection(
            Context.ConnectionId,
            ResolveSessionId(),
            user,
            DateTime.Now);

        await Groups.AddToGroupAsync(Context.ConnectionId, CollaborationGroupNames.ForUser(user.UserId));
        await Clients.All.SendAsync("presenceUpdated", _runtimeStore.GetPresenceSnapshot());
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var result = _runtimeStore.UnregisterConnection(Context.ConnectionId);
        await BroadcastDisconnectResultAsync(result);
        await base.OnDisconnectedAsync(exception);
    }

    public async Task<CollaborationRecordLockChangedEvent> WatchRecord(CollaborationRecordReference record)
    {
        EnsureRecord(record);
        TouchPresence();
        await Groups.AddToGroupAsync(Context.ConnectionId, CollaborationGroupNames.ForRecord(record));
        return _runtimeStore.GetRecordState(record.RecordType, record.RecordId, DateTime.Now);
    }

    public async Task<CollaborationLockAcquireResult> AcquireRecordLock(CollaborationLockAcquireRequest request)
    {
        var user = RequireUser();
        EnsureRequest(request);

        var result = _runtimeStore.TryAcquireLock(user, Context.ConnectionId, request, DateTime.Now);
        await Clients.Group(CollaborationGroupNames.ForRecord(request.RecordType, request.RecordId))
            .SendAsync("recordLockChanged", _runtimeStore.GetRecordState(request.RecordType, request.RecordId, DateTime.Now));
        return result;
    }

    public async Task ReleaseRecordLock(CollaborationHeartbeatRequest request)
    {
        var user = RequireUser();
        if (request.Records is not { Count: > 0 })
        {
            return;
        }

        var released = _runtimeStore.ReleaseLocksForSession(user.UserId, request.SessionId, request.Records);
        foreach (var releasedEvent in released)
        {
            await Clients.Group(CollaborationGroupNames.ForRecord(releasedEvent.RecordType, releasedEvent.RecordId))
                .SendAsync("recordLockChanged", releasedEvent);
        }
    }

    public Task Heartbeat(CollaborationHeartbeatRequest request)
    {
        var user = RequireUser();
        _runtimeStore.Heartbeat(user.UserId, Context.ConnectionId, request.SessionId, request.Records, DateTime.Now);
        return Task.CompletedTask;
    }

    public async Task SendDirectMessage(CollaborationDirectMessageRequest request)
    {
        var user = RequireUser();
        var messageText = request.Message?.Trim();
        if (request.RecipientUserId == Guid.Empty || string.IsNullOrWhiteSpace(messageText))
        {
            throw new HubException("Mesaj gonderilemedi.");
        }

        var message = new CollaborationDirectMessage(
            Guid.NewGuid(),
            user.UserId,
            user.DisplayName,
            request.RecipientUserId,
            messageText,
            DateTime.Now,
            string.IsNullOrWhiteSpace(request.RecordType) ? null : request.RecordType.Trim(),
            string.IsNullOrWhiteSpace(request.RecordId) ? null : request.RecordId.Trim(),
            string.IsNullOrWhiteSpace(request.RecordTitle) ? null : request.RecordTitle.Trim());

        await Clients.Group(CollaborationGroupNames.ForUser(request.RecipientUserId))
            .SendAsync("chatMessageReceived", message);
        await Clients.Caller.SendAsync("chatMessageReceived", message);
    }

    private async Task BroadcastDisconnectResultAsync(CollaborationDisconnectResult result)
    {
        if (result.PresenceChanged)
        {
            await Clients.All.SendAsync("presenceUpdated", _runtimeStore.GetPresenceSnapshot());
        }

        foreach (var releasedEvent in result.ReleasedLocks)
        {
            await Clients.Group(CollaborationGroupNames.ForRecord(releasedEvent.RecordType, releasedEvent.RecordId))
                .SendAsync("recordLockChanged", releasedEvent);
        }
    }

    private void EnsureRequest(CollaborationLockAcquireRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.SessionId) ||
            string.IsNullOrWhiteSpace(request.RecordType) ||
            string.IsNullOrWhiteSpace(request.RecordId))
        {
            throw new HubException("Gecersiz kilit istegi.");
        }
    }

    private static void EnsureRecord(CollaborationRecordReference record)
    {
        if (string.IsNullOrWhiteSpace(record.RecordType) || string.IsNullOrWhiteSpace(record.RecordId))
        {
            throw new HubException("Gecersiz kayit bilgisi.");
        }
    }

    private void TouchPresence()
    {
        if (!TryResolveUser(out var user))
        {
            return;
        }

        _runtimeStore.Heartbeat(user.UserId, Context.ConnectionId, ResolveSessionId(), null, DateTime.Now);
    }

    private CollaborationUserDescriptor RequireUser()
    {
        if (!TryResolveUser(out var user))
        {
            throw new HubException("Oturum bilgisi bulunamadi.");
        }

        return user;
    }

    private bool TryResolveUser(out CollaborationUserDescriptor user)
    {
        var rawUserId = Context.User?.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(rawUserId, out var userId))
        {
            user = default!;
            return false;
        }

        user = new CollaborationUserDescriptor(
            userId,
            Context.User?.Identity?.Name?.Trim() ?? "Kullanici");
        return true;
    }

    private string ResolveSessionId()
    {
        var rawSessionId = Context.GetHttpContext()?.Request.Query["sessionId"].ToString();
        return string.IsNullOrWhiteSpace(rawSessionId)
            ? Context.ConnectionId
            : rawSessionId.Trim();
    }
}
