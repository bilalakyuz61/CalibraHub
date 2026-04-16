using System.Security.Claims;
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

    public CollaborationController(
        CollaborationRuntimeStore runtimeStore,
        IHubContext<CollaborationHub> hubContext)
    {
        _runtimeStore = runtimeStore;
        _hubContext = hubContext;
    }

    [HttpPost("release")]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> Release([FromBody] CollaborationReleaseApiRequest request)
    {
        var rawUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(rawUserId, out var userId))
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
                .SendAsync("recordLockChanged", releasedEvent);
        }

        return Ok(new { released = true });
    }
}
