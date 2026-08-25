using CalibraHub.Persistence.Security;
using Xunit;

namespace CalibraHub.Tests.Security;

/// <summary>
/// 2026-08-24 güvenlik denetimi K1 (SQL injection) regresyon testleri.
///
/// Rehber aramalarında istemciden gelen ham SQL koşul fragment'i doğrudan WHERE'e
/// ekleniyordu; kimliği doğrulanmış herhangi bir kullanıcı UNION/alt-sorgu ile tüm
/// şirket veritabanını okuyabiliyordu. <see cref="GuideRawSqlGuard"/> bu yolu kapatır.
///
/// Testler iki yönlüdür: (1) saldırı biçimleri REDDEDİLMELİ, (2) admin'in yazdığı
/// meşru filtreler ÇALIŞMAYA DEVAM ETMELİ (aksi halde cascading rehber filtresi kırılır).
/// </summary>
public class GuideRawSqlGuardTests
{
    // ── (1) Saldırı biçimleri reddedilmeli ──────────────────────────────────

    [Theory]
    // Denetimde raporlanan asıl istismar: UNION ile parola özetlerini sızdırma.
    [InlineData("1=1)) UNION SELECT Email, PasswordHash, 1, 1, 1 FROM Users--")]
    // Blind/boolean tabanlı sızdırma — alt sorgu taşıyıcısı.
    [InlineData("1=1 AND (SELECT TOP 1 PasswordHash FROM Users) LIKE 'a%'")]
    // Statement kırma + DDL.
    [InlineData("1=1); DROP TABLE Users--")]
    // Zaman tabanlı blind.
    [InlineData("1=1; WAITFOR DELAY '00:00:05'")]
    // Saklı yordam çağrısı.
    [InlineData("1=1); EXEC xp_cmdshell 'whoami'--")]
    // Küme işlemiyle kolon sızdırma.
    [InlineData("1=1)) EXCEPT SELECT 1,2,3,4,5 FROM Users--")]
    public void Saldiri_fragmentleri_reddedilir(string kotucul)
    {
        Assert.Throws<ArgumentException>(() => GuideRawSqlGuard.Sanitize(kotucul));
    }

    [Fact]
    public void Yorum_ile_sorgu_kirma_normalize_edilir_veya_reddedilir()
    {
        // "--" ile sorgunun kalanını yorum yapma denemesi: ya reddedilir ya da AST'den
        // yeniden üretim sırasında yorum tamamen düşer. Her iki sonuç da güvenlidir;
        // KABUL EDİLEMEZ olan, yorumun çıktıda korunup sonraki SQL'i etkisizleştirmesidir.
        string? uretilen = null;
        try { uretilen = GuideRawSqlGuard.Sanitize("Id = 1 --"); }
        catch (ArgumentException) { return; } // reddedildi → güvenli

        Assert.DoesNotContain("--", uretilen);
    }

    // ── (2) Meşru admin filtreleri çalışmaya devam etmeli ───────────────────

    [Theory]
    [InlineData("TYPID IN (2,3)")]                       // CLAUDE.md'deki gerçek örnek
    [InlineData("IsActive = 1")]
    [InlineData("CategoryId = 5 AND IsActive = 1")]
    [InlineData("[Code] LIKE 'EG%'")]
    [InlineData("Price > 100 OR Stock < 10")]
    [InlineData("ISNULL(Discontinued, 0) = 0")]          // fonksiyon çağrısı serbest
    [InlineData("GroupCode = 'HAMMADDE'")]               // token çözülmüş hali
    public void Mesru_filtreler_gecer(string mesru)
    {
        var sonuc = GuideRawSqlGuard.Sanitize(mesru);
        Assert.False(string.IsNullOrWhiteSpace(sonuc));
    }

    [Fact]
    public void Bos_fragment_reddedilir()
    {
        Assert.Throws<ArgumentException>(() => GuideRawSqlGuard.Sanitize("   "));
    }

    [Fact]
    public void Bozuk_sozdizimi_reddedilir()
    {
        Assert.Throws<ArgumentException>(() => GuideRawSqlGuard.Sanitize("Id = = 1"));
    }
}
