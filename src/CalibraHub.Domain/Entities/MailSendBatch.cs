using System.ComponentModel;

namespace CalibraHub.Domain.Entities;

/// <summary>
/// Toplu mail gonderim baslik kaydi — her "Toplu Gonder" tiklamasi bir batch
/// satiri olusturur. Detay (kisi-bazli durum + hata mesaji) MailSendLogItem'da.
/// </summary>
[Description("Toplu mail gonderim baslik kaydi (layout, subject, body preview, alici sayilari, kimin tarafindan ve ne zaman gonderildi).")]
public sealed class MailSendBatch
{
    public int Id { get; init; }

    /// <summary>FK -> DocLayout.Id (mail sablonu).</summary>
    public int LayoutId { get; init; }

    /// <summary>Denormalized layout name — sablon silinse de gecmis kayit anlasilir kalsin.</summary>
    public string? LayoutName { get; init; }

    public string? Subject { get; init; }

    /// <summary>Mail govdesinin ilk 500 karakteri (uzun icerik DocLayout body'de zaten).</summary>
    public string? BodyPreview { get; init; }

    /// <summary>JSON array, ornek: "[1,4,7]" — secilen unvan id'leri.</summary>
    public string? TitleIdsJson { get; init; }

    /// <summary>JSON array of names, denormalized — geriye donuk goruntuleme icin.</summary>
    public string? TitleNamesJson { get; init; }

    public int TotalCount { get; init; }
    public int SentCount { get; init; }
    public int FailCount { get; init; }

    public string? SentBy { get; init; }
    public DateTime SentAt { get; init; }

    public int CompanyId { get; init; }
}
