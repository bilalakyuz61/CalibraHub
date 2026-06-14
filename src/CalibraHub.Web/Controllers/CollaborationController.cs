using System.Security.Claims;
using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Application.Security;
using CalibraHub.Domain.Enums;
using CalibraHub.Web.Infrastructure.Collaboration;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;

namespace CalibraHub.Web.Controllers;

[Authorize]
[ApiController]
[Route("api/collaboration")]
public sealed class CollaborationController : ControllerBase
{
    private readonly CollaborationRuntimeStore _runtimeStore;
    private readonly IHubContext<CollaborationHub> _hubContext;
    private readonly ICollaborationLockRepository _lockRepository;

    public CollaborationController(
        CollaborationRuntimeStore runtimeStore,
        IHubContext<CollaborationHub> hubContext,
        ICollaborationLockRepository lockRepository)
    {
        _runtimeStore = runtimeStore;
        _hubContext = hubContext;
        _lockRepository = lockRepository;
    }

    [HttpPost("release")]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> Release([FromBody] CollaborationReleaseApiRequest request, CancellationToken ct)
    {
        var rawUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!int.TryParse(rawUserId, out var userId))
        {
            return Unauthorized();
        }

        if (string.IsNullOrWhiteSpace(request.SessionId))
        {
            return BadRequest();
        }

        var releasedLocks = _runtimeStore.ReleaseLocksForSession(userId, request.SessionId, request.Records);
        foreach (var releasedEvent in releasedLocks)
        {
            await _hubContext.Clients.Group(
                    CollaborationGroupNames.ForRecord(releasedEvent.RecordType, releasedEvent.RecordId))
                .SendAsync("recordLockChanged", releasedEvent, ct);
        }

        if (request.Records is { Count: > 0 })
        {
            foreach (var record in request.Records)
            {
                await _lockRepository.ReleaseAsync(record.RecordType, record.RecordId, request.SessionId, ct);
            }
        }
        else
        {
            await _lockRepository.ReleaseAllForSessionAsync(request.SessionId, ct);
        }

        return Ok(new { released = true });
    }

    [HttpGet("locks")]
    public IActionResult GetActiveLocks()
    {
        if (!IsSystemAdmin())
        {
            return Forbid();
        }

        var locks = _runtimeStore.GetAllActiveLocks(DateTime.Now);
        return Ok(locks);
    }

    [HttpPost("break-lock")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> BreakLock([FromBody] CollaborationBreakLockRequest request, CancellationToken ct)
    {
        if (!IsSystemAdmin())
        {
            return Forbid();
        }

        if (string.IsNullOrWhiteSpace(request.RecordType) || string.IsNullOrWhiteSpace(request.RecordId))
        {
            return BadRequest();
        }

        _runtimeStore.AdminBreakLock(request.RecordType, request.RecordId, out var releasedEvent);

        await _hubContext.Clients
            .Group(CollaborationGroupNames.ForRecord(releasedEvent.RecordType, releasedEvent.RecordId))
            .SendAsync("recordLockChanged", releasedEvent, ct);

        await _lockRepository.AdminBreakAsync(request.RecordType, request.RecordId, ct);

        return Ok(new { broken = true });
    }

    private bool IsSystemAdmin()
    {
        var roleString = User.FindFirstValue(ClaimTypes.Role);
        return UserAuthorizationCatalog.TryParseRole(roleString, out var role) &&
               role == UserRole.SystemAdmin;
    }
}
