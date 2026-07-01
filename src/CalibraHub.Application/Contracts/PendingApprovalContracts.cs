namespace CalibraHub.Application.Contracts;

/// <summary>
/// 2026-05-26 — Onayda Bekleyenler ekrani DTO'lari.
///
/// Kapsam: ApprovalInstance.Status = 'Pending' kayitlarda
/// suanki adim (CurrentStep) icin ApproverId mevcut kullaniciyi gosteren
/// (veya scope yetkisine gore departman/tumu) kayitlar.
///
/// Yetki scope (ileride Forms/Permissions ile genisletilecek):
///   - "mine"       — sadece bana atananlar (default)
///   - "department" — departmanima atananlar
///   - "all"        — tum bekleyenler (admin)
/// </summary>

/// <summary>Belge turune gore gruplandirilmis sayim (sol panel kart listesi).</summary>
public sealed record PendingApprovalGroupDto(
    int? DocumentTypeId,         // NULL = belirsiz tur
    string? DocumentTypeCode,
    string DocumentTypeName,     // "Belirsiz" fallback
    int Count                     // bu grupta kac bekleyen kayit
);

/// <summary>Liste icin tek satir (orta panel tablo).</summary>
public sealed record PendingApprovalItemDto(
    int InstanceId,
    int StepRecordId,
    int StepOrder,
    int StepPosition,  // rank among step records (1-based): meaningful display for ADIM column
    int TotalSteps,
    string StepName,
    string FlowName,
    int FlowId,
    string EntityKind,             // "Document", "WorkOrder", "StockCard" vb. — hangi entity türü
    int? DocumentId,
    int? DocumentInternalId,     // Document.id (int) — eger varsa (geriye donuk uyumluluk icin)
    string DocumentNumber,
    System.DateTime DocumentDate,
    int? DocumentTypeId,
    string? DocumentTypeName,
    int? ContactId,
    string? ContactName,
    decimal GrandTotal,
    string? CurrencyCode,
    string? ApproverId,
    string? ApproverName,
    System.DateTime StepCreated,
    System.DateTime? DueDate,
    System.DateTime InstanceStarted,
    string? InstanceStatus = null  // null = "Pending"; "Approved" / "Rejected" tamamlananlar için
);

/// <summary>Ek sutun metadata — FlowExtraColumns endpoint'inden donerler.</summary>
public sealed record ExtraColumnMetaDto(
    string Key,        // kolon adi (kucuk harf, saflandirilmis)
    string Label,      // kullaniciya gosterilecek baslik
    string DataType    // 'text' | 'numeric' | 'date'
);

/// <summary>Çoklu onay seçeneği: adımın ek 'out' kollarından türetilir.</summary>
public sealed record ChoiceArmDto(string ArmId, string Label);

/// <summary>Modal detayi (belge baslik + tum adimlar).</summary>
public sealed record PendingApprovalDetailDto(
    PendingApprovalItemDto Header,
    System.Collections.Generic.IReadOnlyList<ApprovalStepRecordDto> Steps,
    System.Collections.Generic.IReadOnlyList<ChoiceArmDto>? ChoiceArms = null
);

/// <summary>Yetki scope enum'u — string'le tasiniyor, kontrolu service'de.</summary>
public static class PendingApprovalScope
{
    public const string Mine       = "mine";
    public const string Department = "department";
    public const string All        = "all";
}
