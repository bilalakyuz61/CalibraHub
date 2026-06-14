using System.ComponentModel;
using CalibraHub.Domain.Enums;

namespace CalibraHub.Domain.Entities;

/// <summary>
/// Bir entegrasyon ile bir kaynak kayit arasindaki "Aktarim Kuyrugu" durumu.
/// (IntegrationId, RecordId) ciftine birebir esit, en son durumu tutar.
///
/// IntegrationRun audit log'undan farki:
/// - IntegrationRun her tetikleme icin 1 satir (history)
/// - IntegrationRecordStatus tek satir (mevcut durum) — kuyruk sorgusu icin
/// </summary>
[Description("Aktarim Kuyrugu durum tablosu — (IntegrationId, RecordId) ciftine birebir, Status filtresi ile kuyruk listelenir.")]
public sealed class IntegrationRecordStatus
{
    public int Id { get; set; }

    public int IntegrationId { get; set; }

    /// <summary>Kaynak kayit PK — form-agnostik string (Document.Id, Item.Id, vb.).</summary>
    public required string RecordId { get; set; }

    public IntegrationRecordStatusType Status { get; set; } = IntegrationRecordStatusType.Pending;

    /// <summary>Son denemenin IntegrationRun.Id'si (audit log'a baglanti).</summary>
    public long? LastRunId { get; set; }

    public DateTime? LastSentAt { get; set; }

    /// <summary>Son hata mesaji (Status=Failed icin). Detay modal'inda gosterilir.</summary>
    public string? LastError { get; set; }

    public int AttemptCount { get; set; }

    // ── "Haric Tut" (Skipped) audit alanlari ───────────────────────────────
    public string? SkippedBy { get; set; }
    public DateTime? SkippedAt { get; set; }
    public string? SkipReason { get; set; }

    public bool IsActive { get; set; } = true;

    public int? CreatedById { get; set; }
    public DateTime Created { get; set; } = DateTime.UtcNow;
    public int? UpdatedById { get; set; }
    public DateTime? Updated { get; set; }
}
