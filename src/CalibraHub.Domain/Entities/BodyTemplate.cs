namespace CalibraHub.Domain.Entities;

/// <summary>
/// Endpoint Body Schema icin hazır JSON sablonu.
///
/// Endpoint Edit modal'da "Şablon Galerisi" butonundan kullanici
/// tek tikla bir sablonu Body Schema textarea'sina yukleyebilir.
/// 5 baslangic sablonu (Netsis sıkça kullanilanlar) seed edilmistir
/// (CalibraDatabaseInitializer.SeedBuiltInBodyTemplatesAsync).
///
/// Kullanici kendi sablonunu da kaydedebilir (V2 — su an sadece okuma).
/// </summary>
public sealed class BodyTemplate
{
    public int Id { get; set; }
    public required string Category { get; set; }      // 'Sales' | 'Purchase' | 'Customer' | 'Stock' | 'EDocument'
    public required string Name { get; set; }
    public string? DocType { get; set; }               // "ftSSip" gibi
    public string? ProviderHint { get; set; }          // 'Netsis' | 'Logo' | 'Custom'
    public string? UrlPattern { get; set; }            // "/api/v2/ItemSlips" — eslesme onerisi icin
    public string? HttpMethod { get; set; }            // "POST" — eslesme filtresi
    public required string BodyJson { get; set; }      // Korlestirilmis JSON sablonu
    public string? Description { get; set; }
    public string? Tags { get; set; }                  // "satis,siparis,netsis" — comma separated
    public int UsageCount { get; set; }
    public bool IsBuiltIn { get; set; }
    public bool IsActive { get; set; } = true;
    public int? CreatedById { get; set; }
    public DateTime Created { get; set; } = DateTime.UtcNow;
    public int? UpdatedById { get; set; }
    public DateTime? Updated { get; set; }
}
