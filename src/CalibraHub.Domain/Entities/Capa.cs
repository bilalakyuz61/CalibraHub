using System.ComponentModel;
using CalibraHub.Domain.Enums;

namespace CalibraHub.Domain.Entities;

/// <summary>
/// DÖF (Düzeltici/Önleyici Faaliyet — CAPA) kaydı companion — 'dof' tipinde bir Document'a
/// 1-1 bağlı (DocumentId UNIQUE). Numara (DÖF-...) + audit Document altyapısından gelir;
/// Document.Status hep Draft, otorite companion.Status'te (QualityInspection deseni).
/// Kapanış (Status=Kapali) yalnızca tüm CapaAction satırları tamamlanmış/iptal VE
/// EffectivenessVerified=1 ise izinlidir (server-side guard, CapaService.ChangeStatusAsync).
/// </summary>
[Description("DÖF kaydı (Document 'dof' ile 1-1). Kapanış yalnızca tüm aksiyonlar bitmiş + etkinlik doğrulanmışsa izinli.")]
public sealed class Capa
{
    public int Id { get; init; }

    [Description("Bağlı DÖF belgesi. FK -> Document.Id (1-1, UNIQUE).")]
    public int DocumentId { get; init; }

    public CapaType CapaType { get; set; } = CapaType.Duzeltici;

    [Description("Kaynak kaydı türü (örn. 'QualityInspection', 'Complaint'). Salt-okunur izlenebilirlik.")]
    public string? SourceKind { get; set; }

    [Description("Kaynak kaydı Id'si.")]
    public int? SourceId { get; set; }

    [Description("DÖF konusu / başlığı.")]
    public string Title { get; set; } = string.Empty;

    public string? ProblemDescription { get; set; }

    [Description("Hata kodu. FK -> QualityDefectCode.Id.")]
    public int? DefectCodeId { get; set; }

    public CapaSeverity Severity { get; set; } = CapaSeverity.Orta;

    public RootCauseMethod? RootCauseMethod { get; set; }

    public string? RootCause { get; set; }

    [Description("Sorumlu personel. FK -> Personnel.Id.")]
    public int? ResponsiblePersonnelId { get; set; }

    public DateTime? DueDate { get; set; }

    [Description("Yaşam döngüsü durumu (tek otorite).")]
    public CapaStatus Status { get; set; } = CapaStatus.Acik;

    [Description("Etkinlik doğrulaması yapıldı mı — kapanış guard'ının bir parçası.")]
    public bool EffectivenessVerified { get; set; }

    [Description("Etkinliği doğrulayan personel. FK -> Personnel.Id.")]
    public int? VerifiedByPersonnelId { get; set; }

    public DateTime? VerifiedAt { get; set; }

    public string? EffectivenessNote { get; set; }

    public DateTime? ClosedAt { get; set; }

    public int? CreatedById { get; init; }
    public DateTime Created { get; init; } = DateTime.UtcNow;
    public int? UpdatedById { get; set; }
    public DateTime? Updated { get; set; }
}
