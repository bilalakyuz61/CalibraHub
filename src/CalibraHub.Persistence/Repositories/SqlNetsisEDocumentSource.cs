using System.Text.Json;
using CalibraHub.Application.Abstractions.Services;
using CalibraHub.Application.Services.EDocument;
using CalibraHub.Domain.Entities;
using CalibraHub.Domain.Enums;
using CalibraHub.Persistence.Services;
using Microsoft.Data.SqlClient;

namespace CalibraHub.Persistence.Repositories;

/// <summary>
/// NETSIS cevrimdisi e-belge kaynagi — ERP veritabanindan SALT-OKUNUR okur.
///
/// <para><b>Okunan tablolar</b> (adlar ve kolonlar canli semadan dogrulandi):
/// e-fatura  : TBLEFATMAS + TBLEFATKALEM + TBLEFATMASTAX + TBLEFATKALEMTAX
/// e-irsaliye: TBLEIRSMAS + TBLEIRSKALEM (+ tasima bilgisi ana tabloda SHIP_* kolonlari)</para>
///
/// <para><b>Neden zarf XML'i okunmuyor:</b> ilk tasarim TBLEFATZARF.XMLVERI'yi ayristirmakti.
/// Olcum curuttu — 14.382 zarf satirinin TAMAMINDA bu kolon 1 bayt, yani bos. Veri iliskisel
/// tablolarda duruyor; buradan okunup dogrudan yapisal hale getiriliyor.</para>
///
/// <para><b>PayloadRaw ne olur:</b> offline kayitta UBL XML YOKTUR. Ana kayittaki PayloadRaw'a
/// kaynak satirin JSON izdusumu yazilir — "ne aldik" sorusu denetlenebilir kalir. Kalem/vergi
/// verisi zaten IncomingDocumentLine/Tax tablolarina yazilir, dolayisiyla ekran icin XML'e
/// bagimlilik kalmaz.</para>
///
/// <para>Yalnizca SELECT calistirir; kaynak veritabanina hicbir sey yazmaz.</para>
/// </summary>
public sealed class SqlNetsisEDocumentSource : IOfflineEDocumentSource
{
    private const string AppName = "CalibraHub-EBelgeOffline";

    private readonly Database.SqlServerConnectionFactory _connectionFactory;

    public SqlNetsisEDocumentSource(Database.SqlServerConnectionFactory connectionFactory)
        => _connectionFactory = connectionFactory;

    public async Task<IReadOnlyList<OfflineEDocument>> ReadAsync(
        ExternalDbConnection connection, DateTime since, int maxRows,
        OfflineSourceWatermark afterSourceKey, CancellationToken ct)
    {
        if (maxRows <= 0) maxRows = 200;

        var hostConn = _connectionFactory.ResolveConnectionStringForCompany(
            _connectionFactory.ResolveEffectiveCompanyId());
        var connStr = ExternalDbConnectionStringBuilder.Build(connection, AppName, hostConn);

        await using var conn = new SqlConnection(connStr);
        await conn.OpenAsync(ct);

        var result = new List<OfflineEDocument>();
        result.AddRange(await ReadInvoicesAsync(conn, since, maxRows, afterSourceKey.LastInvoiceKey, ct));
        result.AddRange(await ReadDespatchesAsync(conn, since, maxRows, afterSourceKey.LastDespatchKey, ct));
        return result;
    }

    // ── e-FATURA ────────────────────────────────────────────────────────────────
    private static async Task<List<OfflineEDocument>> ReadInvoicesAsync(
        SqlConnection conn, DateTime since, int maxRows, int afterKey, CancellationToken ct)
    {
        var headers = new List<(int Inc, IncomingDocument Doc)>();

        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = """
                SELECT TOP (@Max)
                       m.[INCKEYNO], m.[FATIRS_NO], m.[UUID], m.[TARIH], m.[TIPI],
                       ISNULL(m.[SENDERVNO], '')            AS SenderVkn,
                       ISNULL(m.[SENDERNAME], '')           AS SenderName,
                       ISNULL(m.[CARI_VERGINUMARASI], '')   AS CariVkn,
                       ISNULL(m.[CARI_TCKIMLIKNO], '')      AS CariTckn,
                       ISNULL(m.[CARI_ISIM], '')            AS CariIsim,
                       ISNULL(m.[DOVIZTIP], 0)              AS DovizTip,
                       ISNULL(m.[GENELTOPLAM], 0)           AS GenelToplam
                  FROM [dbo].[TBLEFATMAS] m
                 WHERE m.[TARIH] >= @Since AND m.[INCKEYNO] > @AfterKey
                 -- ARTAN sira + ilerleme isareti: azalan sirada her tur ayni en yeni
                 -- kayitlar okunur, hepsi dedup'a takilir ve eski belgeler siraya HIC gelmezdi.
                 ORDER BY m.[INCKEYNO];
                """;
            cmd.Parameters.Add(new SqlParameter("@Max", maxRows));
            cmd.Parameters.Add(new SqlParameter("@Since", since));
            cmd.Parameters.Add(new SqlParameter("@AfterKey", afterKey));

            await using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct))
            {
                var inc = Convert.ToInt32(r["INCKEYNO"]);
                var uuid = Str(r["UUID"]);
                var docNo = Str(r["FATIRS_NO"]);
                var tarih = r["TARIH"] as DateTime? ?? DateTime.Today;

                headers.Add((inc, new IncomingDocument
                {
                    IntegratorSettingsId = null,                 // OFFLINE: entegrator YOK
                    IngestSource = EDocumentIngestSource.Offline,
                    // EnvelopeId dedup anahtaridir; ERP'nin kendi UUID'si BENZERSIZ DEGIL
                    // (olculdu: TBLEFATMAS'ta 40, TBLEIRSMAS'ta 138 tekrar eden UUID). UUID'yi
                    // anahtar yapmak cakisan belgeleri SESSIZCE atlatirdi — veri kaybi. Anahtar
                    // ERP birincil anahtarina baglanir: benzersiz ve tekrar okumada AYNI kalir
                    // (idempotent). UUID izlenebilirlik icin PayloadRaw'da durur.
                    EnvelopeId = $"NETSIS-EFAT-{inc}",
                    DocumentNumber = string.IsNullOrWhiteSpace(docNo) ? $"EFAT-{inc}" : docNo,
                    Kind = DocumentKind.EInvoice,
                    IssueDate = DateOnly.FromDateTime(tarih),
                    SenderTaxNumber = FirstNonEmpty(Str(r["SenderVkn"]), Str(r["CariVkn"]), Str(r["CariTckn"])),
                    SenderName = NullIfEmpty(FirstNonEmpty(Str(r["SenderName"]), Str(r["CariIsim"]))),
                    RecipientTaxNumber = FirstNonEmpty(Str(r["CariVkn"]), Str(r["CariTckn"])),
                    PayloadRaw = JsonSerializer.Serialize(new
                    {
                        source = "Netsis",
                        table = "TBLEFATMAS",
                        incKeyNo = inc,
                        documentNumber = docNo,
                        uuid,
                        date = tarih,
                        currencyType = Convert.ToInt32(r["DovizTip"]),
                        grandTotal = Convert.ToDecimal(r["GenelToplam"]),
                    }),
                }));
            }
        }

        var docs = new List<OfflineEDocument>(headers.Count);
        foreach (var (inc, header) in headers)
        {
            var lines = await ReadInvoiceLinesAsync(conn, inc, ct);
            var docTaxes = await ReadTaxesAsync(conn, "TBLEFATMASTAX", "EFATMASINC", inc, ct);
            docs.Add(new OfflineEDocument(header, new EDocumentDetails(lines, docTaxes, Shipment: null)));
        }
        return docs;
    }

    private static async Task<List<EDocumentLineData>> ReadInvoiceLinesAsync(
        SqlConnection conn, int masterInc, CancellationToken ct)
    {
        var lines = new List<EDocumentLineData>();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT k.[STOK_KODU], k.[STOK_ADI], k.[ALICI_STOK_KODU], k.[URETICI_KODU],
                   ISNULL(k.[STRA_GCMIK], 0) AS Miktar, k.[OLCUBR],
                   ISNULL(k.[STRA_BF], 0)    AS BirimFiyat,
                   ISNULL(k.[KDV], 0)        AS KdvOran,
                   ISNULL(k.[ISKTUT], 0)     AS IskontoTutar,
                   k.[ISKACIK], k.[ACIKLAMA],
                   k.[IRSALIYENO], k.[IRSALIYE_TARIH], k.[SIPARISNO], k.[SIPARIS_TARIH]
              FROM [dbo].[TBLEFATKALEM] k
             WHERE k.[EFATMASINC] = @Inc
             -- ORDER BY zorunlu: TBLEFATKALEM'de satir SIRA kolonu YOKTUR (irsaliye
             -- tablosunda var, faturada yok). ORDER BY olmadan SQL Server satir sirasini
             -- GARANTI ETMEZ; LineNumber tekrar okumalarda kayabilir ve ayni belge farkli
             -- numaralarla yazilabilirdi. Bu siralama kaynagin ORIJINAL fatura sirasini
             -- yansitmaz (kaynak onu saklamiyor) ama DETERMINISTIKTIR.
             ORDER BY k.[STOK_KODU], k.[STRA_GCMIK], k.[STRA_BF];
            """;
        cmd.Parameters.Add(new SqlParameter("@Inc", masterInc));

        var no = 0;
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
        {
            no++;
            var qty = Convert.ToDecimal(r["Miktar"]);
            var price = Convert.ToDecimal(r["BirimFiyat"]);
            lines.Add(new EDocumentLineData(
                LineNumber: no,
                ItemCode: NullIfEmpty(Str(r["STOK_KODU"])),
                ItemName: NullIfEmpty(Str(r["STOK_ADI"])),
                BuyerItemCode: NullIfEmpty(Str(r["ALICI_STOK_KODU"])),
                ManufacturerCode: NullIfEmpty(Str(r["URETICI_KODU"])),
                Quantity: qty,
                UnitCode: NullIfEmpty(Str(r["OLCUBR"])),
                UnitPrice: price,
                CurrencyCode: null,
                DiscountAmount: Convert.ToDecimal(r["IskontoTutar"]),
                VatRate: Convert.ToDecimal(r["KdvOran"]),
                LineAmount: qty * price,
                Description: NullIfEmpty(FirstNonEmpty(Str(r["ACIKLAMA"]), Str(r["ISKACIK"]))),
                Taxes: Array.Empty<EDocumentTaxData>()));
        }
        return lines;
    }

    // ── e-IRSALIYE ──────────────────────────────────────────────────────────────
    private static async Task<List<OfflineEDocument>> ReadDespatchesAsync(
        SqlConnection conn, DateTime since, int maxRows, int afterKey, CancellationToken ct)
    {
        var headers = new List<(int Inc, IncomingDocument Doc, EDocumentShipmentData Ship)>();

        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = """
                SELECT TOP (@Max)
                       m.[INCKEYNO], m.[FATIRS_NO], m.[UUID], m.[TARIH],
                       ISNULL(m.[SUPP_CARI_VERGINUMARASI], '') AS SuppVkn,
                       ISNULL(m.[SUPP_CARI_ISIM], '')          AS SuppIsim,
                       ISNULL(m.[CUST_CARI_VERGINUMARASI], '') AS CustVkn,
                       ISNULL(m.[CUST_CARI_TCKIMLIKNO], '')    AS CustTckn,
                       m.[SHIP_SEVKTAR], m.[SHIP_LICENSEPLATEID],
                       m.[SHIPDORSEPLAKA1], m.[SHIPDORSEPLAKA2], m.[SHIPDORSEPLAKA3],
                       m.[SHIP_CARRIERVKN], m.[SHIP_CARRIERNAME],
                       m.[SHIP_CARRIERCITY], m.[SHIP_CARRIERSUBCITY],
                       m.[SHIP_CARRIERCOUNTRY], m.[SHIP_CARRIERPOSTAL],
                       m.[SHIP_DPERSON1FIRSTNAME], m.[SHIP_DPERSON1FAMILYNAME], m.[SHIP_DPERSON1NID],
                       m.[SHIP_DPERSON2FIRSTNAME], m.[SHIP_DPERSON2FAMILYNAME], m.[SHIP_DPERSON2NID],
                       m.[SHIP_DPERSON3FIRSTNAME], m.[SHIP_DPERSON3FAMILYNAME], m.[SHIP_DPERSON3NID]
                  FROM [dbo].[TBLEIRSMAS] m
                 WHERE m.[TARIH] >= @Since AND m.[INCKEYNO] > @AfterKey
                 ORDER BY m.[INCKEYNO];
                """;
            cmd.Parameters.Add(new SqlParameter("@Max", maxRows));
            cmd.Parameters.Add(new SqlParameter("@Since", since));
            cmd.Parameters.Add(new SqlParameter("@AfterKey", afterKey));

            await using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct))
            {
                var inc = Convert.ToInt32(r["INCKEYNO"]);
                var uuid = Str(r["UUID"]);
                var docNo = Str(r["FATIRS_NO"]);
                var tarih = r["TARIH"] as DateTime? ?? DateTime.Today;

                var ship = new EDocumentShipmentData(
                    DespatchDate: r["SHIP_SEVKTAR"] as DateTime?,
                    LicensePlate: NullIfEmpty(Str(r["SHIP_LICENSEPLATEID"])),
                    TrailerPlate1: NullIfEmpty(Str(r["SHIPDORSEPLAKA1"])),
                    TrailerPlate2: NullIfEmpty(Str(r["SHIPDORSEPLAKA2"])),
                    TrailerPlate3: NullIfEmpty(Str(r["SHIPDORSEPLAKA3"])),
                    CarrierTaxNumber: NullIfEmpty(Str(r["SHIP_CARRIERVKN"])),
                    CarrierName: NullIfEmpty(Str(r["SHIP_CARRIERNAME"])),
                    CarrierCity: NullIfEmpty(Str(r["SHIP_CARRIERCITY"])),
                    CarrierDistrict: NullIfEmpty(Str(r["SHIP_CARRIERSUBCITY"])),
                    CarrierCountry: NullIfEmpty(Str(r["SHIP_CARRIERCOUNTRY"])),
                    CarrierPostalCode: NullIfEmpty(Str(r["SHIP_CARRIERPOSTAL"])),
                    Driver1FirstName: NullIfEmpty(Str(r["SHIP_DPERSON1FIRSTNAME"])),
                    Driver1LastName: NullIfEmpty(Str(r["SHIP_DPERSON1FAMILYNAME"])),
                    Driver1NationalId: NullIfEmpty(Str(r["SHIP_DPERSON1NID"])),
                    Driver2FirstName: NullIfEmpty(Str(r["SHIP_DPERSON2FIRSTNAME"])),
                    Driver2LastName: NullIfEmpty(Str(r["SHIP_DPERSON2FAMILYNAME"])),
                    Driver2NationalId: NullIfEmpty(Str(r["SHIP_DPERSON2NID"])),
                    Driver3FirstName: NullIfEmpty(Str(r["SHIP_DPERSON3FIRSTNAME"])),
                    Driver3LastName: NullIfEmpty(Str(r["SHIP_DPERSON3FAMILYNAME"])),
                    Driver3NationalId: NullIfEmpty(Str(r["SHIP_DPERSON3NID"])));

                headers.Add((inc, new IncomingDocument
                {
                    IntegratorSettingsId = null,
                    IngestSource = EDocumentIngestSource.Offline,
                    // Bkz. e-fatura tarafindaki not: UUID benzersiz degil, anahtar ERP PK'si.
                    EnvelopeId = $"NETSIS-EIRS-{inc}",
                    DocumentNumber = string.IsNullOrWhiteSpace(docNo) ? $"EIRS-{inc}" : docNo,
                    Kind = DocumentKind.EDispatch,
                    IssueDate = DateOnly.FromDateTime(tarih),
                    SenderTaxNumber = FirstNonEmpty(Str(r["SuppVkn"]), Str(r["CustVkn"])),
                    SenderName = NullIfEmpty(Str(r["SuppIsim"])),
                    RecipientTaxNumber = FirstNonEmpty(Str(r["CustVkn"]), Str(r["CustTckn"])),
                    PayloadRaw = JsonSerializer.Serialize(new
                    {
                        source = "Netsis",
                        table = "TBLEIRSMAS",
                        incKeyNo = inc,
                        documentNumber = docNo,
                        uuid,
                        date = tarih,
                    }),
                }, ship));
            }
        }

        var docs = new List<OfflineEDocument>(headers.Count);
        foreach (var (inc, header, ship) in headers)
        {
            var lines = await ReadDespatchLinesAsync(conn, inc, ct);
            docs.Add(new OfflineEDocument(
                header, new EDocumentDetails(lines, Array.Empty<EDocumentTaxData>(), ship)));
        }
        return docs;
    }

    private static async Task<List<EDocumentLineData>> ReadDespatchLinesAsync(
        SqlConnection conn, int masterInc, CancellationToken ct)
    {
        var lines = new List<EDocumentLineData>();
        await using var cmd = conn.CreateCommand();
        // TBLEIRSKALEM sade bir tablodur: iskonto/KDV/aciklama kolonlari YOKTUR.
        // Olmayan kolonu SELECT'e yazmak "Invalid column name" ile kirardi.
        cmd.CommandText = """
            SELECT k.[STOK_KODU], k.[STOK_ADI], ISNULL(k.[STRA_GCMIK], 0) AS Miktar,
                   k.[OLCUBR], ISNULL(k.[STRA_BF], 0) AS BirimFiyat, k.[SIRA]
              FROM [dbo].[TBLEIRSKALEM] k
             WHERE k.[EIRSMASINC] = @Inc
             ORDER BY k.[SIRA];
            """;
        cmd.Parameters.Add(new SqlParameter("@Inc", masterInc));

        var no = 0;
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
        {
            no++;
            var qty = Convert.ToDecimal(r["Miktar"]);
            var price = Convert.ToDecimal(r["BirimFiyat"]);
            lines.Add(new EDocumentLineData(
                LineNumber: no,
                ItemCode: NullIfEmpty(Str(r["STOK_KODU"])),
                ItemName: NullIfEmpty(Str(r["STOK_ADI"])),
                BuyerItemCode: null, ManufacturerCode: null,
                Quantity: qty,
                UnitCode: NullIfEmpty(Str(r["OLCUBR"])),
                UnitPrice: price,
                CurrencyCode: null, DiscountAmount: null, VatRate: null,
                LineAmount: qty * price,
                Description: null,
                Taxes: Array.Empty<EDocumentTaxData>()));
        }
        return lines;
    }

    // ── ortak ───────────────────────────────────────────────────────────────────
    private static async Task<List<EDocumentTaxData>> ReadTaxesAsync(
        SqlConnection conn, string table, string keyColumn, int masterInc, CancellationToken ct)
    {
        var taxes = new List<EDocumentTaxData>();
        await using var cmd = conn.CreateCommand();
        // Tablo/kolon adlari SABIT literal — istemci girdisi DEGIL (injection yolu yok).
        cmd.CommandText = $"""
            SELECT [TAXTYPECODE], [NAME], [TAXABLEAMOUNT], [TAXAMOUNT], [TAXPERCENT],
                   [TRANSACTIONCURRENCYTAXAMOUNT], [TAXEXEMPTIONREASON], [TAXEXEMPTIONREASONCODE]
              FROM [dbo].[{table}]
             WHERE [{keyColumn}] = @Inc;
            """;
        cmd.Parameters.Add(new SqlParameter("@Inc", masterInc));

        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
        {
            taxes.Add(new EDocumentTaxData(
                TaxTypeCode: NullIfEmpty(Str(r["TAXTYPECODE"])),
                Name: NullIfEmpty(Str(r["NAME"])),
                TaxableAmount: Dec(r["TAXABLEAMOUNT"]),
                TaxAmount: Dec(r["TAXAMOUNT"]),
                TaxPercent: Dec(r["TAXPERCENT"]),
                CurrencyTaxAmount: Dec(r["TRANSACTIONCURRENCYTAXAMOUNT"]),
                ExemptionReason: NullIfEmpty(Str(r["TAXEXEMPTIONREASON"])),
                ExemptionReasonCode: r["TAXEXEMPTIONREASONCODE"] is int i ? i : null));
        }
        return taxes;
    }

    /// <summary>
    /// Kaynaktan gelen HER metin bu noktadan gecer; Turkce karakter duzeltmesi burada
    /// uygulanir (bkz. NetsisTextDecoder). Tek nokta olmasi onemli: sorgu bazinda
    /// tekrarlansaydi yeni bir alan eklendiginde duzeltmeyi unutmak sessizce bozuk
    /// metin uretirdi.
    /// </summary>
    private static string Str(object? v) =>
        v is null or DBNull ? string.Empty : NetsisTextDecoder.Fix(v.ToString()!.Trim()) ?? string.Empty;
    private static string? NullIfEmpty(string s) => string.IsNullOrWhiteSpace(s) ? null : s;
    private static decimal? Dec(object? v) => v is null or DBNull ? null : Convert.ToDecimal(v);

    private static string FirstNonEmpty(params string[] values)
        => values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v)) ?? string.Empty;
}
