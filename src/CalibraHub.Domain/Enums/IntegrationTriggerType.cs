namespace CalibraHub.Domain.Enums;

/// <summary>
/// Entegrasyon Wizard'in tetikleme tipleri. DB'de NVARCHAR(20) olarak saklanir;
/// runtime'da bu enum ile parse edilir.
///
/// V1 destekli: Manual, Cron.
/// V2'de aktif olacak: OnSave, Event. Schema gun 1'de hazirdir, dispatcher V2'de eklenir.
/// </summary>
public enum IntegrationTriggerType
{
    /// <summary>Form ekranindaki butona tiklanarak elle tetiklenir.</summary>
    Manual = 0,

    /// <summary>Cron ifadesi ile periyodik calisir. Config JSON'inda cron expression.</summary>
    Cron = 1,

    /// <summary>(V2) Form kaydi INSERT/UPDATE oldugunda arka planda fire eder.</summary>
    OnSave = 2,

    /// <summary>(V2) IntegrationEvents tablosundan ozel event geldiginde fire eder.</summary>
    Event = 3,
}
