using System.Security.Claims;
using System.Text.Json;
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
/// E-BELGE ICE AKTARIMI UCTAN UCA: AddAsync -> ana kayit + kalem + vergi + tasima satirlari.
///
/// <para><b>Neden gerekli:</b> bu yolda derleme temizken RUNTIME'DA kirik IKI hata bulundu —
/// (1) entity Id'si Guid iken tablo PK'si INT IDENTITY, (2) INSERT'in [Id] kolonunu acikca
/// yazmasi ("Cannot insert explicit value for identity column"). Ikisi de `dotnet build`
/// ciktisinda GORUNMEZ. Tablo bos oldugu icin yillarca fark edilmediler. Bu test o sinifin
/// tekrarini engeller: kod calisiyor mu diye VERIYE bakar.</para>
///
/// <para>Baglanti dizesi uygulamanin kendi appsettings'inden sürec icinde cozulur; cozulemezse
/// test sessizce gecmez, Skip ile ATLANIR.</para>
///
/// <para>Test kendi fikstürünü kurar (IntegratorSetting satiri zorunlu FK) ve <c>finally</c>
/// icinde URETTIGI HER SATIRI siler — kalici iz birakmaz.</para>
/// </summary>
public sealed class EDocumentImportRoundTripTests
{
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

    private const string DespatchXml = """
        <DespatchAdvice xmlns:cac="urn:cac" xmlns:cbc="urn:cbc">
          <cac:Shipment>
            <cac:ShipmentStage>
              <cac:TransportMeans>
                <cac:RoadTransport><cbc:LicensePlateID>06TEST34</cbc:LicensePlateID></cac:RoadTransport>
              </cac:TransportMeans>
              <cac:DriverPerson>
                <cbc:FirstName>Test</cbc:FirstName><cbc:FamilyName>Surucu</cbc:FamilyName>
                <cbc:NationalityID>11111111111</cbc:NationalityID>
              </cac:DriverPerson>
            </cac:ShipmentStage>
          </cac:Shipment>
          <cac:TaxTotal>
            <cac:TaxSubtotal>
              <cbc:TaxableAmount>100.00</cbc:TaxableAmount>
              <cbc:TaxAmount>20.00</cbc:TaxAmount>
              <cbc:Percent>20</cbc:Percent>
              <cac:TaxCategory><cac:TaxScheme><cbc:Name>KDV</cbc:Name></cac:TaxScheme></cac:TaxCategory>
            </cac:TaxSubtotal>
          </cac:TaxTotal>
          <cac:DespatchLine>
            <cbc:ID>1</cbc:ID>
            <cbc:DeliveredQuantity unitCode="KGM">2.5</cbc:DeliveredQuantity>
            <cbc:LineExtensionAmount currencyID="TRY">100.00</cbc:LineExtensionAmount>
            <cac:TaxTotal>
              <cac:TaxSubtotal>
                <cbc:TaxAmount>20.00</cbc:TaxAmount><cbc:Percent>20</cbc:Percent>
                <cac:TaxCategory><cac:TaxScheme><cbc:Name>KDV</cbc:Name></cac:TaxScheme></cac:TaxCategory>
              </cac:TaxSubtotal>
            </cac:TaxTotal>
            <cac:Item>
              <cbc:Name>Test Malzeme</cbc:Name>
              <cac:SellersItemIdentification><cbc:ID>TST-1</cbc:ID></cac:SellersItemIdentification>
            </cac:Item>
            <cac:Price><cbc:PriceAmount currencyID="TRY">40.00</cbc:PriceAmount></cac:Price>
          </cac:DespatchLine>
        </DespatchAdvice>
        """;

    [SkippableFact]
    public async Task Ice_aktarim_kalem_vergi_ve_tasima_satirlarini_yazar()
    {
        Skip.If(string.IsNullOrWhiteSpace(Conn), "Baglanti dizesi cozulemedi.");

        var envelopeId = $"TEST-ENV-{Guid.NewGuid():N}";
        var opts = new CalibraDatabaseOptions { ConnectionString = Conn!, Schema = "dbo" };
        var accessor = new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                    new[] { new Claim("company_id", "1") }, "TestAuth"))
            }
        };
        var factory = new SqlServerConnectionFactory(opts, new CompanyConnectionRegistry(), accessor);
        var repo = new SqlIncomingDocumentRepository(factory, opts);

        var integratorId = await CreateIntegratorAsync();
        try
        {
            await repo.AddAsync(new IncomingDocument
            {
                IntegratorSettingsId = integratorId,
                EnvelopeId = envelopeId,
                DocumentNumber = $"IRS{DateTime.Now:HHmmss}",
                Kind = DocumentKind.EDispatch,
                IssueDate = DateOnly.FromDateTime(DateTime.Today),
                SenderTaxNumber = "1111111111",
                SenderName = "Test Gonderen",
                RecipientTaxNumber = "2222222222",
                PayloadRaw = DespatchXml
            }, CancellationToken.None);

            var docId = await ScalarAsync<int?>(
                "SELECT [Id] FROM dbo.IncomingDocument WHERE [EnvelopeId] = @p", envelopeId);
            Assert.NotNull(docId);   // IDENTITY INSERT hatasi olsaydi satir HIC olusmazdi

            // Kalem
            var lineCount = await ScalarAsync<int>(
                "SELECT COUNT(*) FROM dbo.IncomingDocumentLine WHERE [IncomingDocumentId] = @p", docId!.Value);
            Assert.Equal(1, lineCount);

            var qty = await ScalarAsync<decimal>(
                "SELECT [Quantity] FROM dbo.IncomingDocumentLine WHERE [IncomingDocumentId] = @p", docId.Value);
            Assert.Equal(2.5m, qty);

            var unitPrice = await ScalarAsync<decimal>(
                "SELECT [UnitPrice] FROM dbo.IncomingDocumentLine WHERE [IncomingDocumentId] = @p", docId.Value);
            Assert.Equal(40.00m, unitPrice);

            // CompanyId EBEVEYNDEN turetildi mi (oturumdan degil)
            var mismatched = await ScalarAsync<int>(
                """
                SELECT COUNT(*) FROM dbo.IncomingDocumentLine l
                JOIN dbo.IncomingDocument d ON d.[Id] = l.[IncomingDocumentId]
                WHERE l.[IncomingDocumentId] = @p AND (l.[CompanyId] IS NULL OR l.[CompanyId] <> d.[CompanyId])
                """, docId.Value);
            Assert.Equal(0, mismatched);

            // Vergi: biri kalem seviyesi (LineId dolu), biri belge seviyesi (LineId NULL)
            var lineTax = await ScalarAsync<int>(
                "SELECT COUNT(*) FROM dbo.IncomingDocumentTax WHERE [IncomingDocumentId] = @p AND [IncomingDocumentLineId] IS NOT NULL", docId.Value);
            var docTax = await ScalarAsync<int>(
                "SELECT COUNT(*) FROM dbo.IncomingDocumentTax WHERE [IncomingDocumentId] = @p AND [IncomingDocumentLineId] IS NULL", docId.Value);
            Assert.Equal(1, lineTax);
            Assert.Equal(1, docTax);

            // Tasima: sabit surucu/plaka kolonlari
            var plate = await ScalarAsync<string?>(
                "SELECT [LicensePlate] FROM dbo.IncomingDocumentShipment WHERE [IncomingDocumentId] = @p", docId.Value);
            Assert.Equal("06TEST34", plate);

            var driver = await ScalarAsync<string?>(
                "SELECT [Driver1FirstName] FROM dbo.IncomingDocumentShipment WHERE [IncomingDocumentId] = @p", docId.Value);
            Assert.Equal("Test", driver);

            // Okuma yolu: GetByIdAsync artik INT kimlikle calisiyor (eskiden GetGuid ile patlardi)
            var reread = await repo.GetByIdAsync(docId.Value, CancellationToken.None);
            Assert.NotNull(reread);
            Assert.Equal(envelopeId, reread!.EnvelopeId);
            Assert.Equal(docId.Value, reread.Id);
        }
        finally
        {
            await CleanupAsync(envelopeId, integratorId);
        }
    }

    // ── fikstür ve temizlik ──────────────────────────────────────────────────────
    private static async Task<int> CreateIntegratorAsync()
    {
        await using var conn = new SqlConnection(Conn);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO dbo.IntegratorSetting
                ([CompanyId],[Provider],[Name],[BaseUrl],[CompanyTaxNumber],[Username],[Secret],
                 [PollingIntervalSeconds],[MaxRecordsPerPull],[LogRetentionDays],
                 [IncludeReceivedDocumentsInPull],[MarkDownloadedDocumentsAsReceived],
                 [IncludeIssuedEInvoiceInPull],[IncludeIssuedEArchiveInPull],[IncludeIssuedEDispatchInPull],
                 [IsActive],[Created])
            VALUES (1, N'Test', N'ROUNDTRIP-TEST', N'http://localhost', N'1111111111', N'u', N's',
                    60, 10, 7, 0, 0, 0, 0, 0, 0, SYSUTCDATETIME());
            SELECT CAST(SCOPE_IDENTITY() AS INT);
            """;
        return Convert.ToInt32(await cmd.ExecuteScalarAsync());
    }

    private static async Task CleanupAsync(string envelopeId, int integratorId)
    {
        await using var conn = new SqlConnection(Conn);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        // Cocuktan ebeveyne dogru: FK'ler silmeyi aksi sirada ENGELLER.
        cmd.CommandText = """
            DECLARE @d INT = (SELECT [Id] FROM dbo.IncomingDocument WHERE [EnvelopeId] = @env);
            IF @d IS NOT NULL
            BEGIN
                DELETE FROM dbo.IncomingDocumentTax      WHERE [IncomingDocumentId] = @d;
                DELETE FROM dbo.IncomingDocumentShipment WHERE [IncomingDocumentId] = @d;
                DELETE FROM dbo.IncomingDocumentLine     WHERE [IncomingDocumentId] = @d;
                DELETE FROM dbo.IncomingDocument         WHERE [Id] = @d;
            END;
            DELETE FROM dbo.IntegratorSetting WHERE [Id] = @int;
            """;
        cmd.Parameters.Add(new SqlParameter("@env", envelopeId));
        cmd.Parameters.Add(new SqlParameter("@int", integratorId));
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task<T> ScalarAsync<T>(string sql, object parameter)
    {
        await using var conn = new SqlConnection(Conn);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.Add(new SqlParameter("@p", parameter));
        var v = await cmd.ExecuteScalarAsync();
        if (v is null || v == DBNull.Value) return default!;
        var target = Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T);
        return (T)Convert.ChangeType(v, target);
    }
}
