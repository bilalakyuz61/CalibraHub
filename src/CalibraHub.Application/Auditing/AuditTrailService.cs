using System.Globalization;
using Microsoft.Extensions.Logging;

namespace CalibraHub.Application.Auditing;

/// <summary>
/// IAuditTrailService implementasyonu — context damgalar, diff hesaplar, Channel'a bırakır.
/// Singleton kaydedilir (IAuditContextProvider IHttpContextAccessor tabanlı olduğundan güvenli).
/// Hiçbir metod exception fırlatmaz — audit yazımı iş akışını asla bozamaz.
/// </summary>
public sealed class AuditTrailService : IAuditTrailService
{
    private readonly AuditTrailChannel _channel;
    private readonly IAuditContextProvider _context;
    private readonly ILogger<AuditTrailService> _logger;

    public AuditTrailService(AuditTrailChannel channel, IAuditContextProvider context,
        ILogger<AuditTrailService> logger)
    {
        _channel = channel;
        _context = context;
        _logger = logger;
    }

    public void LogInsert(string entity, object? entityId, string? title, string? detail = null,
        AuditActor? actor = null)
        => Enqueue(AuditActions.Insert, entity, entityId, title, null, detail, actor);

    public void LogUpdate(string entity, object? entityId, string? title, object? oldSnapshot,
        object? newSnapshot, string? detail = null, AuditActor? actor = null)
    {
        try
        {
            var changes = AuditDiff.Compute(oldSnapshot, newSnapshot, entity);
            if (changes.Count == 0 && string.IsNullOrEmpty(detail)) return; // değişiklik yok → log yok
            Enqueue(AuditActions.Update, entity, entityId, title, changes, detail, actor);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Audit] Update diff hesaplanamadı: {Entity}/{Id}", entity, entityId);
        }
    }

    public void LogChanges(string entity, object? entityId, string? title,
        IReadOnlyList<AuditFieldChange> changes, string? detail = null, AuditActor? actor = null)
    {
        if ((changes is null || changes.Count == 0) && string.IsNullOrEmpty(detail)) return;
        Enqueue(AuditActions.Update, entity, entityId, title,
            changes is { Count: > 0 } ? new List<AuditFieldChange>(changes) : null, detail, actor);
    }

    public void LogDelete(string entity, object? entityId, string? title, string? detail = null,
        AuditActor? actor = null)
        => Enqueue(AuditActions.Delete, entity, entityId, title, null, detail, actor);

    public void LogEvent(string action, string? detail = null, AuditActor? actor = null,
        string? entity = null, object? entityId = null, string? title = null)
        => Enqueue(action, entity, entityId, title, null, detail, actor);

    private void Enqueue(string action, string? entity, object? entityId, string? title,
        List<AuditFieldChange>? changes, string? detail, AuditActor? actor)
    {
        try
        {
            var ctx = _context.Resolve();
            var companyId = actor?.CompanyId ?? ctx.CompanyId;
            if (companyId <= 0) return; // şirket çözümlenemedi → yazacak klasör yok (setup öncesi vb.)

            var entry = new AuditEntry
            {
                Ts = DateTime.UtcNow,
                CompanyId = companyId,
                UserId = actor?.UserId ?? ctx.UserId,
                User = actor?.UserName ?? ctx.UserName ?? "SYSTEM",
                Action = action,
                Entity = entity,
                EntityId = entityId is null ? null : Convert.ToString(entityId, CultureInfo.InvariantCulture),
                Title = string.IsNullOrWhiteSpace(title) ? null : title.Trim(),
                Changes = changes,
                Detail = string.IsNullOrWhiteSpace(detail) ? null : detail,
                Ip = actor?.Ip ?? ctx.Ip,
                Src = actor?.Source ?? (ctx.UserName is null ? "System" : "Web"),
            };

            if (!_channel.TryWrite(entry))
                _logger.LogWarning("[Audit] Kuyruğa yazılamadı: {Action} {Entity}/{Id}", action, entity, entry.EntityId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Audit] Log kaydı oluşturulamadı: {Action} {Entity}", action, entity);
        }
    }
}
