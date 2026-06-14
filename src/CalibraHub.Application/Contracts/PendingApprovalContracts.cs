namespace CalibraHub.Application.Contracts;

/// <summary>
/// 2026-05-26 — Onayda Bekleyenler ekrani DTO'lari.
///
/// Kapsam: DocumentApprovalInstance.Status = 'Pending' kayitlarda
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
    int TotalSteps,
    string StepName,
    string FlowName,
    System.Guid DocumentId,
    int? DocumentInternalId,     // Document.id (int) — eger varsa
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
    System.DateTime InstanceStarted
);

/// <summary>Modal detayi (belge baslik + tum adimlar).</summary>
public sealed record PendingApprovalDetailDto(
    PendingApprovalItemDto Header,
    System.Collections.Generic.IReadOnlyList<ApprovalStepRecordDto> Steps
);

/// <summary>Yetki scope enum'u — string'le tasiniyor, kontrolu service'de.</summary>
public static class PendingApprovalScope
{
    public const string Mine       = "mine";
    public const string Department = "department";
    public const string All        = "all";
}
