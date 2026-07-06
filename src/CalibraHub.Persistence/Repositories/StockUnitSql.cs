namespace CalibraHub.Persistence.Repositories;

/// <summary>
/// Stok miktarı ana-birim normalizasyonu — ortak SQL ifadesi (tek kaynak).
///
/// DocumentLine.BaseQuantity = miktar × birim çarpanı:
///   • Girilen birim (UnitId) NULL ise veya malzemenin ana birimi (Items.UnitId) ile
///     aynıysa → çarpan 1 (miktar zaten baz birimdedir).
///   • Aksi halde ItemUnits.Multiplier (o malzeme+birim için tanımlı dönüşüm faktörü;
///     "1 alternatif birim = Multiplier baz birim"). Tanımsızsa fallback 1.
///
/// Böylece farklı birimlerle girilen stok hareketleri (adet, metre, koli…) baz birimde
/// tutarlı toplanır. Girilen birim = ana birim olan mevcut kayıtlarda çarpan 1 olduğundan
/// davranış değişmez (BaseQuantity = Quantity).
///
/// Hem yazma yollarında (INSERT VALUES içinde skaler alt sorgu — parametreler @Qty/@ItemId/@UnitId),
/// hem de migration backfill'inde (kolonlar dl.[Quantity]/dl.[ItemId]/dl.[UnitId]) kullanılır.
/// </summary>
internal static class StockUnitSql
{
    /// <summary>
    /// Baz-birim miktar ifadesini üretir. Tablo adları TAM NİTELİKLİ verilir
    /// (örn. <c>[dbo].[Items]</c>) — şema kaçış belirsizliği çağırana bırakılır.
    /// </summary>
    /// <param name="itemsTable">Tam nitelikli Items tablosu (T("Items") veya [{s}].[Items]).</param>
    /// <param name="itemUnitsTable">Tam nitelikli ItemUnits tablosu.</param>
    /// <param name="qty">Miktar ifadesi/parametresi (@Qty, @Quantity veya dl.[Quantity]).</param>
    /// <param name="itemId">ItemId ifadesi (@ItemId veya dl.[ItemId]).</param>
    /// <param name="unitId">UnitId ifadesi (@UnitId veya dl.[UnitId]).</param>
    public static string BaseQtyExpr(string itemsTable, string itemUnitsTable, string qty, string itemId, string unitId)
        => $"({qty} * ISNULL((SELECT TOP 1 CASE WHEN {unitId} IS NULL OR i.[UnitId] = {unitId} THEN 1 " +
           $"ELSE ISNULL(iu.[Multiplier], 1) END " +
           $"FROM {itemsTable} i LEFT JOIN {itemUnitsTable} iu ON iu.[ItemId] = i.[Id] AND iu.[UnitId] = {unitId} " +
           $"WHERE i.[Id] = {itemId}), 1))";
}
