using System.ComponentModel;

namespace CalibraHub.Domain.Entities;

[Description("Stok/malzeme kartlari. DocumentLine.ItemId, PriceList.ItemId ve stok-konfigurasyon mapping tablolari bu tabloya FK ile baglidir. Combinations = urun konfigurasyon ozelliklerinin acik oldugunu belirtir.")]
public sealed class Item
{
    public int Id { get; init; }
    public int CompanyId { get; init; }
    public required string Code { get; init; }
    public required string Name { get; init; }

    /// <summary>Barkod (opsiyonel, kullanıcı girer) — "kullanıcı kod girmez" kuralı Code
    /// alanı içindir; Barcode ayrı bir alan, doğrudan kullanıcı tarafından girilir/taranır.
    /// Boşsa arama/entegrasyon katmanları Code'a düşer (fallback), DB'de zorlanmaz.</summary>
    public string? Barcode { get; init; }
    public int? TypeId { get; init; }
    public int? UnitId { get; init; }
    public bool Combinations { get; init; } = false;
    public decimal TaxRate { get; init; } = 20m;

    /// <summary>Planlama: genel (malzeme bazında) asgari stok — ana birim. Şimdilik yalnız tanım/gösterim.</summary>
    public decimal MinStock { get; init; }

    /// <summary>Takip tipi: "None" (Yok) | "Lot" (Lot takibi) | "Serial" (Seri takibi). Varsayılan "None".</summary>
    public string? TrackingType { get; init; } = "None";

    /// <summary>Giriş serisi otomatik: seri-takipli stokta giriş belgesinde seri listesi boş
    /// bırakılırsa sunucu üretir (ItemCode-yyMMdd-NNN). Yalnız TrackingType='Serial' iken anlamlı.</summary>
    public bool AutoSerial { get; init; }
    public bool IsActive { get; private set; } = true;
    // 2026-05-26: CLAUDE.md audit standardi — Created/Updated + CreatedBy/UpdatedBy NVARCHAR(120)
    public DateTime? Created { get; init; }
    public DateTime? Updated { get; init; }
    public int? CreatedById { get; init; }
    public int? UpdatedById { get; init; }

    public void Deactivate()
    {
        IsActive = false;
    }
}
