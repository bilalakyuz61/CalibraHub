using System.ComponentModel;
using CalibraHub.Domain.Enums;

namespace CalibraHub.Domain.Entities;

/// <summary>
/// Bir içe aktarım işinin tek çalıştırma kaydı — ne zaman, ne kadar okundu, kaç kayıt
/// eklendi/güncellendi/hata verdi ve prosedürler ne yaptı. Teşhis buradan yapılır.
/// </summary>
[Description("İçe aktarım çalıştırma logu: okunan/eklenen/güncellenen/hatalı satır sayıları + prosedür sonuçları.")]
public sealed class DataImportRun
{
    public long Id { get; set; }

    public int JobId { get; set; }

    public DataImportTriggerType TriggerType { get; set; } = DataImportTriggerType.Manual;

    public DateTime StartedAt { get; set; }
    public DateTime? FinishedAt { get; set; }
    public int? DurationMs { get; set; }

    public DataImportRunStatus Status { get; set; } = DataImportRunStatus.Failed;

    /// <summary>Kaynaktan okunan satır sayısı.</summary>
    public int RowsRead { get; set; }

    public int RowsInserted { get; set; }
    public int RowsUpdated { get; set; }
    public int RowsFailed { get; set; }

    /// <summary>Kaynakta bulunmadığı için pasife alınan kayıt sayısı (politika açıkken).</summary>
    public int RowsDeactivated { get; set; }

    /// <summary>İş politikası gereği atlanan satır (ekleme/güncelleme kapalı).</summary>
    public int RowsSkipped { get; set; }

    /// <summary>Aktarımı durduran hata (varsa).</summary>
    public string? ErrorMessage { get; set; }

    /// <summary>Ön prosedür sonucu — çalışmadıysa null.</summary>
    public string? PreProcedureResult { get; set; }

    /// <summary>Son prosedür sonucu — çalışmadıysa null.</summary>
    public string? PostProcedureResult { get; set; }

    /// <summary>Kullanıcı e-postası veya "scheduled:{görev adı}".</summary>
    public string? TriggeredBy { get; set; }
}
