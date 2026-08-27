using System.Security.Claims;
using System.Text.Json;
using CalibraHub.Application.Security;
using CalibraHub.Persistence.Database;
using CalibraHub.Persistence.Options;
using CalibraHub.Persistence.Repositories;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.SqlClient;
using Xunit;

namespace CalibraHub.Tests.Persistence;

/// <summary>
/// AYNI VERITABANINDA IKI GERCEK SIRKET — uctan uca kiraci izolasyonu kaniti (2026-08-27).
///
/// <para>Statik tarama (preflight/check-tenant-filter.ps1) yalnizca "sorguda CompanyId gecti mi"
/// sorusunu yanitlar. Gecmesi DOGRU degeri kullandigi anlamina GELMEZ. Bu test zinciri gercek
/// veritabani uzerinde bastan sona yurutur: oturumun <c>company_id</c> claim'i -&gt;
/// ResolveEffectiveCompanyId -&gt; repository SQL'i -&gt; donen satirlar.</para>
///
/// <para>Test canli bir SQL Server ister. Baglanti dizesi uygulamanin KENDI appsettings'inden
/// ve KENDI cozucusuyle (DpapiSecretDecryptor) sürec icinde okunur — parola hicbir zaman diske
/// ya da ortam degiskenine yazilmaz. Dosya/DB yoksa (CI, baska makine) test sessizce gecmez,
/// Skip ile ATLANIR; boylece "yesil" gorunup aslinda hic calismamis olmaz.</para>
/// </summary>
public sealed class TenantIsolationTests
{
    private const int OwnerCompany = 1;    // Calibra Merkez — 77 belge, 39.803 malzeme
    private const int OtherCompany = 40;   // TEST_2708261415 — verisi YOK

    private static readonly Lazy<string?> LazyConn = new(ResolveConnectionString);
    private static string? Conn => LazyConn.Value;

    private static string? ResolveConnectionString()
    {
        // Repo kokunu test cikti klasorunden yukari yurüyerek bul (bin/Debug/net10.0 -> ...).
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

    private static (SqlServerConnectionFactory factory, CalibraDatabaseOptions opts) Build(int? companyId)
    {
        var opts = new CalibraDatabaseOptions { ConnectionString = Conn!, Schema = "dbo" };
        var accessor = new HttpContextAccessor();

        // DIKKAT: HttpContextAccessor verisini STATIK bir AsyncLocal'da tutar; yeni bir ornek
        // olusturmak onceki baglami TEMIZLEMEZ. "Oturum yok" durumunu acikca null'a cekmezsek
        // bir onceki testin sirketi sizar ve test YANLIS sonucu dogrulamis olur (bu tam olarak
        // yasandi: oturumsuz cagri 1 yerine 40 dondu).
        if (companyId is int id)
        {
            var identity = new ClaimsIdentity(
                new[] { new Claim("company_id", id.ToString()), new Claim(ClaimTypes.Name, "tenant-test") },
                authenticationType: "TestAuth");   // authenticationType sart: yoksa IsAuthenticated false
            accessor.HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identity) };
        }
        else
        {
            accessor.HttpContext = null;
        }

        return (new SqlServerConnectionFactory(opts, new CompanyConnectionRegistry(), accessor), opts);
    }

    [SkippableFact]
    public void Oturumun_sirketi_suzgec_degerini_belirler()
    {
        Skip.If(string.IsNullOrWhiteSpace(Conn), "Baglanti dizesi cozulemedi (appsettings yok / DPAPI baska makine).");

        Assert.Equal(OwnerCompany, Build(OwnerCompany).factory.ResolveEffectiveCompanyId());
        Assert.Equal(OtherCompany, Build(OtherCompany).factory.ResolveEffectiveCompanyId());

        // Oturum YOKSA sahip sirkete duser (arka plan isleri bos veri gormesin diye).
        // TEST_ onekli sirketler elenir, bu yuzden 40 DEGIL 1 beklenir.
        Assert.Equal(OwnerCompany, Build(null).factory.ResolveEffectiveCompanyId());
    }

    /// <summary>
    /// EN KRITIK: WhatsAppConfig eskiden sabit [Id]=1 ile anahtarlaniyordu, yani ayni
    /// veritabanindaki TUM sirketler ayni satiri paylasiyordu. Ikinci sirket, birincinin
    /// SIFRELENMIS ERISIM JETONUNU okuyabiliyordu. Anahtar CompanyId'ye tasindi.
    /// </summary>
    [SkippableFact]
    public async Task WhatsApp_ayarlari_sirketler_arasi_sizmaz()
    {
        Skip.If(string.IsNullOrWhiteSpace(Conn), "Baglanti dizesi cozulemedi (appsettings yok / DPAPI baska makine).");

        var (f1, o1) = Build(OwnerCompany);
        var owner = await new SqlWhatsAppConfigRepository(f1, o1).GetAsync(default);

        var (f2, o2) = Build(OtherCompany);
        var other = await new SqlWhatsAppConfigRepository(f2, o2).GetAsync(default);

        Assert.NotNull(owner);   // sahip sirket kendi kaydini gorur
        Assert.Null(other);      // diger sirket sahip sirketin jetonunu GOREMEZ
    }

    [SkippableFact]
    public async Task Belge_ve_malzeme_sayilari_sirkete_gore_ayrisir()
    {
        Skip.If(string.IsNullOrWhiteSpace(Conn), "Baglanti dizesi cozulemedi (appsettings yok / DPAPI baska makine).");

        Assert.True(await CountAsync("Document", OwnerCompany) > 0);
        Assert.Equal(0, await CountAsync("Document", OtherCompany));

        Assert.True(await CountAsync("Items", OwnerCompany) > 0);
        Assert.Equal(0, await CountAsync("Items", OtherCompany));

        // CompanyId = 0 olu yigini geri gelmemeli (39.796 satirlik regresyon).
        Assert.Equal(0, await CountAsync("Items", 0));
    }

    private static async Task<int> CountAsync(string table, int companyId)
    {
        await using var conn = new SqlConnection(Conn);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT COUNT(*) FROM [dbo].[{table}] WHERE [CompanyId] = @c;";
        cmd.Parameters.Add(new SqlParameter("@c", companyId));
        return Convert.ToInt32(await cmd.ExecuteScalarAsync());
    }
}
