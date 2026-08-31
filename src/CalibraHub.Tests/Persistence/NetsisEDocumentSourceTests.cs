using System.Security.Claims;
using System.Text.Json;
using CalibraHub.Application.Abstractions.Services;
using CalibraHub.Application.Security;
using CalibraHub.Domain.Entities;
using CalibraHub.Domain.Enums;
using CalibraHub.Persistence.Database;
using CalibraHub.Persistence.Options;
using CalibraHub.Persistence.Repositories;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.SqlClient;
using Xunit;

namespace CalibraHub.Tests.Persistence;

/// <summary>
/// NETSIS cevrimdisi kaynagi GERCEK ERP veritabanina karsi okur.
///
/// <para><b>Neden gerekli:</b> bu ozellikte iki kez "derleme temiz, runtime kirik" hatasi
/// cikti (Guid/INT kimlik uyumsuzlugu ve IDENTITY kolonuna acik INSERT). Kolon adlari
/// da dis semadan geliyor — yanlis yazilan bir kolon yalnizca CALISMA ZAMANINDA
/// "Invalid column name" verir. Bu test o sinifi kapatir: kod calisiyor mu diye VERIYE bakar.</para>
///
/// <para>ERP veritabani (EYS) ayni SQL Server ornegindedir; baglanti
/// <c>UseHostServer</c> ile CalibraHub'in kendi kimlik bilgilerini devralir. Kaynak
/// cozulemezse test sessizce gecmez, Skip ile ATLANIR. Hicbir sey YAZILMAZ (salt-okunur).</para>
/// </summary>
public sealed class NetsisEDocumentSourceTests
{
    private const string ErpDatabase = "EYS";

    private static readonly Lazy<string?> LazyConn = new(ResolveConnectionString);
    private static string? Conn => LazyConn.Value;

    private static string? ResolveConnectionString()
    {
        var dir = AppContext.BaseDirectory;
        for (var i = 0; i < 8 && dir is not null; i++)
        {
            var candidate = Path.Combine(dir, "src", "CalibraHub.Web", "appsettings.json");
            if (File.Exists(candidate))
            {
                try
                {
                    using var doc = JsonDocument.Parse(File.ReadAllText(candidate));
                    var raw = doc.RootElement.GetProperty("CalibraDatabase")
                                             .GetProperty("ConnectionString").GetString();
                    var plain = DpapiSecretDecryptor.DecryptIfNeeded(raw);
                    return string.IsNullOrWhiteSpace(plain) ? null : plain;
                }
                catch { return null; }
            }
            dir = Path.GetDirectoryName(dir);
        }
        return null;
    }

    private static bool ErpReachable()
    {
        if (string.IsNullOrWhiteSpace(Conn)) return false;
        try
        {
            var b = new SqlConnectionStringBuilder(Conn) { InitialCatalog = ErpDatabase, ConnectTimeout = 5 };
            using var c = new SqlConnection(b.ConnectionString);
            c.Open();
            using var cmd = c.CreateCommand();
            cmd.CommandText = "SELECT CASE WHEN OBJECT_ID('dbo.TBLEFATMAS','U') IS NULL THEN 0 ELSE 1 END;";
            return Convert.ToInt32(cmd.ExecuteScalar() ?? 0) == 1;
        }
        catch { return false; }
    }

    private static SqlNetsisEDocumentSource BuildSource()
    {
        var opts = new CalibraDatabaseOptions { ConnectionString = Conn!, Schema = "dbo" };
        var accessor = new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                    new[] { new Claim("company_id", "1") }, "TestAuth"))
            }
        };
        return new SqlNetsisEDocumentSource(
            new SqlServerConnectionFactory(opts, new CompanyConnectionRegistry(), accessor));
    }

    private static ExternalDbConnection ErpConnection() => new()
    {
        Name = "ERP-TEST",
        UseHostServer = true,          // ayni SQL Server ornegi, yalniz veritabani farkli
        DatabaseName = ErpDatabase,
        ConnectTimeoutSeconds = 15,
    };

    [SkippableFact]
    public async Task Netsis_kaynagi_fatura_ve_irsaliyeleri_okur()
    {
        Skip.IfNot(ErpReachable(), $"ERP veritabani ({ErpDatabase}) erisilemedi.");

        var docs = await BuildSource().ReadAsync(
            ErpConnection(), new DateTime(2000, 1, 1), maxRows: 25,
            afterSourceKey: new OfflineSourceWatermark(0, 0), ct: CancellationToken.None);

        Assert.NotEmpty(docs);

        // Her belge OFFLINE isaretli ve entegratorsuz olmali.
        Assert.All(docs, d =>
        {
            Assert.Equal(EDocumentIngestSource.Offline, d.Header.IngestSource);
            Assert.Null(d.Header.IntegratorSettingsId);
            Assert.False(string.IsNullOrWhiteSpace(d.Header.EnvelopeId));
            Assert.False(string.IsNullOrWhiteSpace(d.Header.DocumentNumber));
            // PayloadRaw sozlesmesi (2026-08-30'da genisletildi): zarf UBL'i bulunabiliyorsa
            // ONU tasir (resmi GIB goruntusu icin), bulunamiyorsa kaynak satirin JSON
            // izdusumunu. Test ESKIDEN yalniz JSON bekliyordu; o dar beklenti artik
            // gecerli tasarimi degil, eski bir ani dondurur.
            // Onemli olan BOS/BOZUK olmamasi:
            var payload = (d.Header.PayloadRaw ?? "").TrimStart();
            Assert.True(payload.StartsWith('<') || payload.StartsWith('{'),
                $"PayloadRaw ne UBL ne JSON: '{payload[..Math.Min(40, payload.Length)]}'");
        });

        var invoices = docs.Where(d => d.Header.Kind == DocumentKind.EInvoice).ToList();
        var despatches = docs.Where(d => d.Header.Kind == DocumentKind.EDispatch).ToList();

        Assert.NotEmpty(invoices);
        Assert.NotEmpty(despatches);

        // Kalemler gercekten okunuyor mu (bos donerse eslesme/kolon adi yanlis demektir).
        Assert.Contains(invoices, d => d.Details.Lines.Count > 0);
        Assert.Contains(despatches, d => d.Details.Lines.Count > 0);

        // Fatura tarafinda belge seviyesi vergi kirilimi (TBLEFATMASTAX) gelmeli.
        Assert.Contains(invoices, d => d.Details.DocumentTaxes.Count > 0);

        // Irsaliyede tasima kaydi olmali (sabit surucu/plaka kolonlari).
        Assert.All(despatches, d => Assert.NotNull(d.Details.Shipment));
    }

    [SkippableFact]
    public async Task Envelope_kimlikleri_benzersiz_uretilir()
    {
        Skip.IfNot(ErpReachable(), $"ERP veritabani ({ErpDatabase}) erisilemedi.");

        var docs = await BuildSource().ReadAsync(
            ErpConnection(), new DateTime(2000, 1, 1), maxRows: 40,
            afterSourceKey: new OfflineSourceWatermark(0, 0), ct: CancellationToken.None);

        // EnvelopeId dedup anahtaridir (ExistsByEnvelopeIdAsync). Cakisirsa ikinci belge
        // sessizce ATLANIR — yani ayni okumada tekrar eden anahtar VERI KAYBI demektir.
        var ids = docs.Select(d => d.Header.EnvelopeId).ToList();
        Assert.Equal(ids.Count, ids.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    /// <summary>
    /// Ilerleme isareti gercekten ILERLETIYOR mu.
    ///
    /// <para>Bu testin sebebi canli dogrulamada gorulen davranis: okuyucu azalan sirada
    /// calisirken her turda AYNI en yeni kayitlari getiriyor, hepsi dedup'a takiliyor ve
    /// eski belgeler siraya HIC gelmiyordu (her tur "0 eklendi, 400 atlandi"). Isaret
    /// ilerlemezse sistem sessizce ilk tavanda takili kalir.</para>
    /// </summary>
    [SkippableFact]
    public async Task Ilerleme_isareti_sonraki_kayitlari_getirir()
    {
        Skip.IfNot(ErpReachable(), $"ERP veritabani ({ErpDatabase}) erisilemedi.");

        var src = BuildSource();
        var first = await src.ReadAsync(ErpConnection(), new DateTime(2000, 1, 1), 5,
            new OfflineSourceWatermark(0, 0), CancellationToken.None);
        Assert.NotEmpty(first);

        // Ilk turdaki en buyuk fatura anahtarindan devam et.
        var lastInvoiceKey = first
            .Where(d => d.Header.Kind == DocumentKind.EInvoice)
            .Select(d => int.Parse(d.Header.EnvelopeId.Replace("NETSIS-EFAT-", "")))
            .DefaultIfEmpty(0).Max();
        Assert.True(lastInvoiceKey > 0);

        // Pencere fatura VE irsaliye ile paylasilir. Kucuk bir pencere (5) ikinci
        // okumada tamamen irsaliyeye denk gelebilir; "fatura gelmedi" bunu ilerleme
        // hatasi saymak YANLIS olurdu — test veri karisimina bagimli hale gelir.
        // Pencere buyutuldu ve asil ozellik ayrica dogrulanir.
        const int window = 40;
        var second = await src.ReadAsync(ErpConnection(), new DateTime(2000, 1, 1), window,
            new OfflineSourceWatermark(lastInvoiceKey, 0), CancellationToken.None);

        var secondInvoiceKeys = second
            .Where(d => d.Header.Kind == DocumentKind.EInvoice)
            .Select(d => int.Parse(d.Header.EnvelopeId.Replace("NETSIS-EFAT-", "")))
            .ToList();

        // ASIL OZELLIK: isaretten ONCEKI hicbir kayit tekrar GELMEMELI.
        // Orijinal hata buydu — her tur ayni kayitlar donuyor, kuyruk hic ilerlemiyordu.
        Assert.All(secondInvoiceKeys, k => Assert.True(k > lastInvoiceKey,
            $"Ilerleme isareti calismiyor: {k} <= {lastInvoiceKey}"));

        // Bos donmek yalniz KAYNAK TUKENDIYSE mesrudur. Bunu VARSAYMIYORUZ, olcuyoruz:
        // isaretten sonra gercekten fatura kalmis mi diye genis bir pencereyle bakariz.
        if (secondInvoiceKeys.Count == 0)
        {
            var probe = await src.ReadAsync(ErpConnection(), new DateTime(2000, 1, 1), 500,
                new OfflineSourceWatermark(lastInvoiceKey, 0), CancellationToken.None);
            var remaining = probe.Count(d => d.Header.Kind == DocumentKind.EInvoice);
            Assert.True(remaining == 0,
                $"Isaretten sonra {remaining} fatura var ama {window}'lik pencerede hicbiri gelmedi.");
        }
    }
}
