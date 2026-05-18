using System.ComponentModel;
using CalibraHub.Domain.Enums;

namespace CalibraHub.Domain.Entities;

/// <summary>
/// Bir entegrasyonun tetikleyici konfigi. Bir Integration birden cok trigger'a sahip olabilir
/// (Manuel buton + Cron + OnSave aynı anda). Config kolonu trigger tipine özgü JSON tasar:
///
///   Manual: {"buttonLabel":"ERP'ye Aktar","color":"blue"}
///   Cron:   {"cronExpression":"0 */15 * * * *","filterUnsynced":true}
///   OnSave: {"onlyNew":false}                              (V2)
///   Event:  {"eventCode":"DOCUMENT_APPROVED"}             (V2)
/// </summary>
[Description("Entegrasyonun tetikleyici konfigi. Multi-trigger destek: bir Integration N IntegrationTrigger'a sahip olabilir.")]
public sealed class IntegrationTrigger
{
    public int Id { get; init; }

    /// <summary>FK -> Integration.Id. CASCADE DELETE.</summary>
    public int IntegrationId { get; set; }

    public IntegrationTriggerType TriggerType { get; set; }

    /// <summary>Trigger tipine ozgu JSON konfig (string olarak tutulur, runtime'da parse).</summary>
    public string? Config { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTime Created { get; init; } = DateTime.UtcNow;
}
