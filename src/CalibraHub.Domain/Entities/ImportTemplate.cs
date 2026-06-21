using System.ComponentModel;

namespace CalibraHub.Domain.Entities;

/// <summary>
/// İçe aktarım şablonu — bir Excel/CSV dosyasının kolonlarını bir hedef entity'nin
/// alanlarına eşleyen, tekrar kullanılabilir tanım. Yapay zeka kullanmaz; eşleme
/// deterministiktir. Pilot kapsamı: <see cref="TargetEntity"/> = "CONTACT" (Cari).
/// </summary>
[Description("İçe aktarım şablonu: kaynak dosya kolonu → hedef entity alanı eşleme tanımı. Eşleme JSON olarak MappingJson'da tutulur.")]
public sealed class ImportTemplate
{
    public int Id { get; set; }

    /// <summary>Şablon adı — benzersiz (kullanıcı kod girmez kuralı: ad üzerinden uniqueness).</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Hedef entity kodu. Pilot: "CONTACT". İleride "ITEM", "DOCUMENT" vb.</summary>
    public string TargetEntity { get; set; } = "CONTACT";

    /// <summary>Excel sayfa adı (null = ilk sayfa). CSV için yok sayılır.</summary>
    public string? SheetName { get; set; }

    /// <summary>Başlık satırının 1 tabanlı indeksi (varsayılan 1).</summary>
    public int HeaderRowIndex { get; set; } = 1;

    /// <summary>
    /// Upsert anahtarı: mevcut kaydı bulmak için kullanılan hedef alan ("AccountCode")
    /// veya null (her zaman yeni kayıt ekle).
    /// </summary>
    public string? MatchKeyField { get; set; }

    /// <summary><c>List&lt;ImportColumnMapDto&gt;</c> JSON serisi. NVARCHAR(MAX).</summary>
    public string MappingJson { get; set; } = "[]";

    public bool IsActive { get; set; } = true;

    public DateTime Created { get; set; }
    public DateTime? Updated { get; set; }
    public int? CreatedById { get; set; }
    public int? UpdatedById { get; set; }
}
