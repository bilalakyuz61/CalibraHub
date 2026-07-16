using System;
using System.Collections.Generic;
using System.Linq;

namespace CalibraHub.Persistence.Database;

/// <summary>
/// Flat view (v_Flat_{FormCode}) FK kolonlari icin cozulmus Kod/Ad kolonlarini uretir.
///
/// AMAC: entegrasyon eslemesinde kullanicinin ham FK Id ( or. ContactId = 42) yerine,
/// referans master entity'nin insan-okur KODUNU (ContactCode) ve adini (ContactName)
/// dogrudan "Form Alani -> ContactCode" ile gonderebilmesi. Boylece kirilgan Lookup
/// (eslesme kolonu elle Id, donus Code) kurmadan cari/stok kodu tek tikla akar.
///
/// Bu mantik IKI yerde ayni sekilde kullanilmali:
///   1) CalibraDatabaseInitializer.RebuildSingleFlatViewAsync (per-company startup init)
///   2) SqlWidgetRepository.RegenerateFlattenedViewAsync (sistem DB + runtime widget/form
///      degisikligi). Ikisi de bu tek kaynaktan uretsin ki flat view her iki yolda ayni
///      kolon setini tasisin (aksi halde bir runtime widget edit'i cozulmus kolonlari
///      sessizce dusurur). Drift onlemek icin ortak yer burasi.
/// </summary>
internal static class FlatViewFkResolver
{
    /// <summary>
    /// Bilinen FK -> (referans tablo, kod kolonu, ad kolonu, cikti prefix'i) haritasi.
    /// RefKey her master entity'de "Id" (PK konvansiyonu). Yeni bir FK cozumu eklemek
    /// icin buraya tek satir ekle; referans tablo/kolonlarin gercekten var oldugu
    /// runtime'da dogrulanir (yoksa o FK sessizce atlanir, view derlemesi kirilmaz).
    ///
    /// Gercek sema adlari dogrulanmistir (CalibraDatabaseInitializer CREATE TABLE bloklari):
    ///   Contact.AccountCode/AccountTitle, Items.Code/Name, DocumentType.Code/Name,
    ///   Unit.Code/Name, Location.LocationCode/LocationName, Currency.Code/Name.
    /// DIKKAT: Contact icin kod/ad kolonu Code/Name DEGIL AccountCode/AccountTitle.
    /// </summary>
    public static readonly IReadOnlyList<(string FkColumn, string RefTable, string CodeCol, string NameCol, string OutPrefix)> Map =
        new (string, string, string, string, string)[]
        {
            ("ContactId",      "Contact",      "AccountCode",  "AccountTitle", "Contact"),
            ("ItemId",         "Items",        "Code",         "Name",         "Item"),
            ("DocumentTypeId", "DocumentType", "Code",         "Name",         "DocumentType"),
            ("UnitId",         "Unit",         "Code",         "Name",         "Unit"),
            ("LocationId",     "Location",     "LocationCode", "LocationName", "Location"),
            ("CurrencyId",     "Currency",     "Code",         "Name",         "Currency"),
        };

    /// <summary>
    /// Map'teki referans tablolarin benzersiz adlari — cagiranin kolon envanterini
    /// tek sorguda cekebilmesi icin.
    /// </summary>
    public static IReadOnlyList<string> ReferencedTables =>
        Map.Select(m => m.RefTable).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

    /// <summary>
    /// Cozulmus FK SELECT ifadelerini (<c>MAX(alias.[Kod]) AS [XxxCode]</c>) ve LEFT JOIN
    /// cumlelerini uretir.
    ///
    /// - MAX() ile sarmalanir: flat view zaten widget pivot'u icin GROUP BY base-kolon
    ///   kullaniyor; MAX sayesinde cozulmus kolonlari GROUP BY'a eklemeye gerek yok
    ///   (FK -> PK 1:1 oldugundan MAX tek matching degeri doner).
    /// - NULL-safe: LEFT JOIN kullanilir → eksik/NULL FK'de kod NULL doner, ASLA satir
    ///   kaybi olmaz (INNER JOIN kullanilmaz).
    /// - Cakisma korumasi: cikti adi (XxxCode/XxxName) mevcut base kolonu VEYA pivot
    ///   widget kolonu ile cakisiyorsa o kolon atlanir (ikisi de cakisirsa JOIN da atlanir).
    /// - <paramref name="refExists"/>: (refTable, codeCol, nameCol) => referans tablo + Id +
    ///   kod + ad kolonlari mevcut mu? false ise o FK atlanir (idempotent guard).
    /// </summary>
    /// <param name="schemaEsc">Bracket-escape edilmis sema adi (or. "dbo").</param>
    /// <param name="baseColumns">Base tablo kolon adlari (case-insensitive set).</param>
    /// <param name="pivotWidgetCodes">Pivot'a giren widget kodlari (cakisma seti icin).</param>
    /// <param name="refExists">Referans tablo+kolon varlik yoklamasi.</param>
    public static (List<string> SelectParts, List<string> JoinParts) Build(
        string schemaEsc,
        ISet<string> baseColumns,
        IEnumerable<string> pivotWidgetCodes,
        Func<string, string, string, bool> refExists)
    {
        var selectParts = new List<string>();
        var joinParts   = new List<string>();

        // Cikti-adi cakisma korumasi: base kolonlar + pivot widget kodlari rezerve edilir.
        var reserved = new HashSet<string>(baseColumns, StringComparer.OrdinalIgnoreCase);
        foreach (var wc in pivotWidgetCodes)
            if (!string.IsNullOrEmpty(wc)) reserved.Add(wc);

        foreach (var fk in Map)
        {
            // Base tabloda bu FK kolonu yoksa (or. DocumentLine'da ContactId yok) atla.
            if (!baseColumns.Contains(fk.FkColumn)) continue;

            var outCode  = fk.OutPrefix + "Code";
            var outName  = fk.OutPrefix + "Name";
            var wantCode = !reserved.Contains(outCode);
            var wantName = !reserved.Contains(outName);
            if (!wantCode && !wantName) continue;   // ikisi de cakisiyor → JOIN gereksiz

            // Referans tablo + Id + kod + ad kolonlari gercekten var mi? Yoksa atla
            // (view derlemesinin komple kirilmasini onler — base + widget kolonlari korunur).
            if (!refExists(fk.RefTable, fk.CodeCol, fk.NameCol)) continue;

            var alias    = "fk_" + fk.OutPrefix;
            var tblEsc   = fk.RefTable.Replace("]", "]]");
            var fkColEsc = fk.FkColumn.Replace("]", "]]");

            if (wantCode)
            {
                selectParts.Add($"    MAX({alias}.[{fk.CodeCol.Replace("]", "]]")}]) AS [{outCode}]");
                reserved.Add(outCode);
            }
            if (wantName)
            {
                selectParts.Add($"    MAX({alias}.[{fk.NameCol.Replace("]", "]]")}]) AS [{outName}]");
                reserved.Add(outName);
            }

            joinParts.Add($"LEFT JOIN [{schemaEsc}].[{tblEsc}] {alias} ON {alias}.[Id] = base.[{fkColEsc}]");
        }

        return (selectParts, joinParts);
    }
}
