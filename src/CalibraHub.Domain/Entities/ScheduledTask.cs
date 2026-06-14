using CalibraHub.Domain.Enums;

namespace CalibraHub.Domain.Entities;

/// <summary>
/// Zamanlanmis gorev tanimi. Worker'daki scheduler bu tabloyu polling ile okur,
/// NextRunAt gelen kayitlari ilgili executor'a dispatch eder (TaskType'a gore).
/// </summary>
public sealed class ScheduledTask
{
    public int Id { get; init; }

    public required string Name { get; init; }
    public string? Description { get; init; }

    /// <summary>
    /// Bu gorevi tanimlayan sirketin id'si. UI'dan kayit edildiginde HttpContext'teki
    /// company_id claim'inden set edilir; runtime'da executor token resolver bu deger
    /// uzerinden sirket bilgilerini ({COMPANY_ID}, {INTEGRATION_DB} vb.) cozer.
    /// Builtin/legacy kayitlar icin null olabilir.
    /// </summary>
    public int? CompanyId { get; set; }

    /// <summary>Task turunu belirler — BUILTIN / SQL_PROCEDURE / HTTP_API / FILE_TRANSFER.</summary>
    public ScheduledTaskType TaskType { get; set; } = ScheduledTaskType.Builtin;

    /// <summary>
    /// Executor icin JSON config. Format TaskType'a gore degisir:
    /// - BUILTIN: {} (worker kendi yapilandirir)
    /// - SQL_PROCEDURE: {"procedureName":"sp_X","parameters":{"p1":"v1"}}
    /// - HTTP_API: {"url":"https://...","method":"POST","headers":{},"body":"..."}
    /// </summary>
    public string? ParametersJson { get; set; }

    /// <summary>Zamanlama ifadesi turu — INTERVAL / CRON / DAILY_AT / ONCE / MANUAL.</summary>
    public ScheduleType ScheduleType { get; set; } = ScheduleType.Interval;

    /// <summary>
    /// ScheduleType'a gore parse edilen expression:
    /// - INTERVAL: saniye integer ("60", "3600")
    /// - CRON: "0 9 * * *"
    /// - DAILY_AT: "09:00"
    /// - ONCE: "2026-05-01T09:00:00"
    /// - MANUAL: bos
    /// </summary>
    public string? ScheduleExpression { get; set; }

    /// <summary>Insan okunabilir zamanlama aciklamasi (UI'da gosterim icin).</summary>
    public string? ScheduleDescription { get; set; }

    public bool IsEnabled { get; set; } = true;

    /// <summary>
    /// Oncul gorev id'si (self-FK). Bu gorev calistirilmadan once oncul gorev calistirilir;
    /// oncul basarili olursa ana gorev calisir, basarisiz olursa ana gorev iptal edilir.
    /// Tek seviyeli alan ama zincirleme calisir (A→B, B→C tanimlanabilir; cycle runtime'da
    /// visit-set ile yakalanir). NULL = on kosul yok.
    /// </summary>
    public int? PrerequisiteTaskId { get; set; }

    /// <summary>Son calistirma zamani (UTC).</summary>
    public DateTime? LastRunAt { get; set; }

    /// <summary>Son calistirmanin durumu: 0=success, 1=error, 2=running.</summary>
    public int? LastRunStatus { get; set; }

    /// <summary>Son calistirmanin mesaji/log ozeti.</summary>
    public string? LastRunMessage { get; set; }

    /// <summary>Son calistirmanin suresi (milisaniye).</summary>
    public int? LastRunDurationMs { get; set; }

    /// <summary>Bir sonraki calistirma zamani (UTC). Scheduler her turda gunceller.</summary>
    public DateTime? NextRunAt { get; set; }

    /// <summary>
    /// Olay tetiklendiginde calisan "in-flight" isaretleme — scheduler asynchrony
    /// dispatch sirasinda iki kez tetiklenmesini engeller (lock flag).
    /// </summary>
    public bool IsRunning { get; set; }

    public DateTime Created { get; init; } = DateTime.UtcNow;
    public DateTime Updated { get; set; } = DateTime.UtcNow;
}
