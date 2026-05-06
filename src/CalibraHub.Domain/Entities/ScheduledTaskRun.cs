using CalibraHub.Domain.Enums;

namespace CalibraHub.Domain.Entities;

/// <summary>
/// Bir zamanlanmis gorevin tek bir calistirma (run) kaydi. History/audit icin;
/// UI'da "Calistirma Gecmisi" modalinde gosterilir.
/// </summary>
public sealed class ScheduledTaskRun
{
    public int Id { get; init; }

    /// <summary>Parent ScheduledTask.Id. Task silinince cascade ile temizlenir.</summary>
    public int TaskId { get; init; }

    /// <summary>ScheduledTask.Code (ayri index icin duplike tutulur).</summary>
    public required string TaskCode { get; init; }

    public DateTime StartedAt { get; init; }
    public DateTime? CompletedAt { get; set; }

    /// <summary>0=success, 1=error, 2=running, 3=cancelled.</summary>
    public int Status { get; set; }

    public string? Message { get; set; }
    public int? DurationMs { get; set; }

    /// <summary>
    /// Calisan gercek komut (SQL prosedur cagrisi, HTTP istegi vb.) — debug ve audit icin.
    /// SQL prosedur turu icin "EXEC [dbo].[sp_X] @p=N'value'" formatinda, parametre tokenleri
    /// resolve edilmis halde tutulur. Ham/buyuk olabilir, NVARCHAR(MAX) olarak saklanir.
    /// </summary>
    public string? ExecutedCommand { get; set; }

    /// <summary>Nasil tetiklendi — SCHEDULE / MANUAL / SYSTEM.</summary>
    public RunTrigger Trigger { get; init; }
}
