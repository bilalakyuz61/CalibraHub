using CalibraHub.Persistence.Security;
using Xunit;

namespace CalibraHub.Tests.Security;

/// <summary>
/// 2026-08-24 güvenlik denetimi (ORTA) — rapor motorunda SELECT-only doğrulaması yoktu.
/// <c>/api/report/query/inline</c> ucu serbest SQL kabul ediyordu; testler hem mutasyonun
/// reddedildiğini hem de MEŞRU rapor sorgularının çalışmaya devam ettiğini doğrular
/// (fazla katı bir guard, çalışan raporları kırar — bu da bir regresyondur).
/// </summary>
public class ReadOnlySqlGuardTests
{
    [Theory]
    [InlineData("UPDATE Document SET Status = 'Approved'")]
    [InlineData("DELETE FROM Document")]
    [InlineData("INSERT INTO Document (Id) VALUES (1)")]
    [InlineData("DROP TABLE Document")]
    [InlineData("TRUNCATE TABLE Document")]
    [InlineData("EXEC sp_executesql N'SELECT 1'")]
    [InlineData("SELECT 1; DROP TABLE Document")]
    [InlineData("SELECT * INTO Kopya FROM Document")]
    [InlineData("ALTER TABLE Document ADD X INT")]
    [InlineData("WAITFOR DELAY '00:00:10'")]
    [InlineData("MERGE Document AS t USING Document AS s ON 1=1 WHEN MATCHED THEN DELETE;")]
    public void Mutasyon_iceren_sql_reddedilir(string sql)
    {
        Assert.Throws<ArgumentException>(() => ReadOnlySqlGuard.EnsureSelectOnly(sql));
    }

    [Theory]
    [InlineData("SELECT * FROM Document")]
    [InlineData("SELECT d.Id, d.GrandTotal FROM Document d WHERE d.IsActive = 1 ORDER BY d.Id")]
    [InlineData("WITH x AS (SELECT Id FROM Document) SELECT COUNT(*) FROM x")]
    [InlineData("DECLARE @d DATE = GETDATE(); SELECT * FROM Document WHERE DocumentDate >= @d")]
    [InlineData("SELECT (SELECT COUNT(*) FROM DocumentLine l WHERE l.DocumentId = d.Id) AS Adet FROM Document d")]
    public void Mesru_rapor_sorgusu_gecer(string sql)
    {
        ReadOnlySqlGuard.EnsureSelectOnly(sql);   // exception atmamali
    }

    [Fact]
    public void Bos_sql_reddedilir()
    {
        Assert.Throws<ArgumentException>(() => ReadOnlySqlGuard.EnsureSelectOnly(null));
        Assert.Throws<ArgumentException>(() => ReadOnlySqlGuard.EnsureSelectOnly("   "));
    }

    [Fact]
    public void Select_icermeyen_sql_reddedilir()
    {
        Assert.Throws<ArgumentException>(() => ReadOnlySqlGuard.EnsureSelectOnly("DECLARE @x INT = 1;"));
    }

    [Fact]
    public void Ayristirilamayan_sql_reddedilir()
    {
        Assert.Throws<ArgumentException>(() => ReadOnlySqlGuard.EnsureSelectOnly("SELECT FROM WHERE ("));
    }
}
