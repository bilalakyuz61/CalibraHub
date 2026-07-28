namespace CalibraHub.Domain.Entities;

/// <summary>
/// Yükleme Planlama / stok rezervasyonu (2026-07-27, Faz 1).
/// Bir satış siparişi kaleminin (OrderLineId) belirli bir depodaki (LocationId) stoktan
/// MANTIKSAL olarak rezerve edilen miktarını tutar — fiziksel stok azalmaz, yalnız
/// "kullanılabilir bakiye" düşer (available = fiziksel − Σ aktif rezervasyon). Fiziksel
/// çıkış ancak "Yükle" aksiyonuyla üretilen irsaliyede olur (Faz 2).
///
/// Faz 1 KAPSAMI: kit-DIŞI (Items.TypeId != 10) normal malzemeler + miktar düzeyi.
/// Kit'ler tam-set atomik rezervasyonla sonraki fazda eklenir (seti bölmemek için).
/// </summary>
public class StockReservation
{
    public int Id { get; set; }

    /// <summary>Rezervasyonun bağlı olduğu satış siparişi belgesi (Document.Id, DocumentType.Code = satis_siparisi).</summary>
    public int OrderDocumentId { get; set; }

    /// <summary>Rezerve edilen satış siparişi kalemi (DocumentLine.Id).</summary>
    public int OrderLineId { get; set; }

    /// <summary>Rezerve edilen malzeme (Items.Id). Kalemle aynı; sorgu kolaylığı için denormalize.</summary>
    public int ItemId { get; set; }

    /// <summary>Rezervasyonun yapıldığı depo/lokasyon (Location.Id).</summary>
    public int LocationId { get; set; }

    /// <summary>Varyant/kombinasyon (opsiyonel).</summary>
    public int? CombinationId { get; set; }

    /// <summary>Gösterim birimi (DocumentLine.UnitId). Kanonik karşılaştırma BaseQuantity üzerinden yapılır.</summary>
    public int? UnitId { get; set; }

    /// <summary>Gösterim birimindeki rezerve miktar (kullanıcıya gösterilen).</summary>
    public decimal Quantity { get; set; }

    /// <summary>Kanonik (baz-birim) rezerve miktar. Available bakiye ve stok karşılaştırmaları bunu kullanır.</summary>
    public decimal BaseQuantity { get; set; }

    /// <summary>1=Active (aktif rezerve), 2=Shipped (irsaliyeye dönüştü), 3=Cancelled (iptal).</summary>
    public byte Status { get; set; } = (byte)StockReservationStatus.Active;

    /// <summary>Planlanan sevk/yükleme tarihi (opsiyonel).</summary>
    public System.DateTime? PlannedShipDate { get; set; }

    /// <summary>Faz 2: rezervasyon "Yükle" ile irsaliyeye dönünce oluşan irsaliye belgesi (Document.Id).</summary>
    public int? ShippedDocumentId { get; set; }

    public string? Notes { get; set; }

    public bool IsActive { get; set; } = true;

    public int? CreatedById { get; set; }
    public System.DateTime Created { get; set; }
    public int? UpdatedById { get; set; }
    public System.DateTime? Updated { get; set; }
}

/// <summary>Stok rezervasyonu durumu. DB'de TINYINT olarak tutulur.</summary>
public enum StockReservationStatus : byte
{
    Active = 1,
    Shipped = 2,
    Cancelled = 3,
}
