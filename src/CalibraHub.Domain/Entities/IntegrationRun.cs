using System.ComponentModel;
using CalibraHub.Domain.Enums;

namespace CalibraHub.Domain.Entities;

/// <summary>
/// Bir entegrasyon calistirmasinin audit kaydi. Her run icin 1 satir;
/// retry varsa N satir (RetryAttempt artar). BIGINT PK cunku yuksek volume
/// (cron her 15dk'da onlarca run, manuel butonlar, vb.).
///
/// Integration silinse bile audit log korunur (FK CASCADE YOK). Soft-delete
/// onerilir (Integration.IsActive = 0).
/// </summary>
[Description("Entegrasyon calistirma audit log. Her tetikleme icin 1+ satir; success/fail/retry/skip durumu + request/response body.")]
public sealed class IntegrationRun
{
    public long Id { get; set; }

    public int IntegrationId { get; set; }

    /// <summary>Hangi tetikleyici fire etti: Manual / Cron / OnSave / Event.</summary>
    public IntegrationTriggerType TriggerType { get; set; }

    /// <summary>Islenen form kaydi PK. NVARCHAR cunku farkli tablo farkli tipte olabilir
    /// (int, guid). Form-agnostik calismak icin string.</summary>
    public string? SourceRecordId { get; set; }

    public DateTime StartedAt { get; init; } = DateTime.UtcNow;
    public DateTime? FinishedAt { get; set; }
    public int? DurationMs { get; set; }

    public IntegrationRunStatus Status { get; set; }

    public int? HttpStatusCode { get; set; }
    public string? RequestBody { get; set; }
    public string? ResponseBody { get; set; }
    public string? ErrorMessage { get; set; }

    public int RetryAttempt { get; set; }

    /// <summary>Manuel icin kullanici adi/email; cron/event icin "system".</summary>
    public string? TriggeredBy { get; set; }

    /// <summary>
    /// 2026-05-22 Cascade: Bir run baska bir run tarafindan cascade yoluyla tetiklendiyse
    /// parent'in RunId'si. NULL = top-level run (manuel/cron/onsave/event).
    /// Run logunda parent expand → child run'lar agac olarak gosterilir.
    /// </summary>
    public long? ParentRunId { get; set; }
}
