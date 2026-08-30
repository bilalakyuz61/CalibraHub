using CalibraHub.Domain.Enums;

namespace CalibraHub.Domain.Entities;

/// <summary>
/// Gelen e-belge (e-fatura / e-irsaliye / e-arsiv) ana kaydi.
///
/// <para><b>Id neden Entity taban sinifindan ALINMIYOR (2026-08-28):</b> taban sinif
/// <c>Guid Id</c> veriyordu, ama <c>IncomingDocument</c> tablosunun PK'si
/// <c>INT IDENTITY</c>. Bu uyumsuzluk derleme zamaninda GORUNMUYOR, runtime'da kiriyordu:
/// okuma <c>reader.GetGuid(0)</c> ile int kolona gidiyor, guncelleme
/// <c>WHERE [Id] = @Id</c>'ye Guid parametresi veriyordu. Tablo bos oldugu (ve
/// CBT_EBELGE* olan kurulumlarda eski dal calistigi) icin bugune kadar patlamadi —
/// ilk gercek e-belge yerli tabloya dustugunde patlayacakti.</para>
///
/// <para>Ayrica bu uyumsuzluk BEDEL ODETMISTI: ApprovalInstance.DocumentId INT oldugu icin
/// gelen e-fatura onay akislari baglanamiyordu ve ApprovalController'da iki uc
/// "Guid PK uyumsuzlugu" gerekcesiyle devre disi birakilmisti.</para>
///
/// <para>Yon INT'tir cunku proje kurali (CLAUDE.md) "PK her zaman Id: INT IDENTITY" der;
/// istisna olan taraf Guid idi.</para>
/// </summary>
public sealed class IncomingDocument
{
    public int Id { get; init; }

    /// <summary>OFFLINE (ERP) aktarimda entegrator yoktur -> null.</summary>
    public int? IntegratorSettingsId { get; init; }

    /// <summary>Kaydin hangi yoldan geldigi. Varsayilan Online (mevcut davranis korunur).</summary>
    public EDocumentIngestSource IngestSource { get; init; } = EDocumentIngestSource.Online;
    public required string EnvelopeId { get; init; }
    public required string DocumentNumber { get; init; }
    public DocumentKind Kind { get; init; }
    public DateOnly IssueDate { get; init; }
    public required string SenderTaxNumber { get; init; }
    public string? SenderName { get; init; }
    public required string RecipientTaxNumber { get; init; }
    public required string PayloadRaw { get; init; }
    public ApprovalStatus ApprovalStatus { get; private set; } = ApprovalStatus.Pending;
    public DateTime ImportedAt { get; init; } = DateTime.Now;

    public bool IsProcessed { get; set; } = false;

    /// <summary>
    /// Belgenin baglandigi CARI (FK -> Contact.Id). Gonderen VKN/TC ile otomatik
    /// eslestirilir; BIRDEN COK aday varsa null kalir ve kullanici ekrandan secer —
    /// aday coklugunda birini varsaymak, faturayi sessizce yanlis hesaba baglardi.
    /// </summary>
    public int? ContactId { get; set; }

    public void MarkApproved() => ApprovalStatus = ApprovalStatus.Approved;
    public void MarkRejected() => ApprovalStatus = ApprovalStatus.Rejected;
    public void SetProcessed(bool processed) => IsProcessed = processed;
}
