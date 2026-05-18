using CalibraHub.Application.Services.Integration;

namespace CalibraHub.Application.UnitTests.Integration;

/// <summary>
/// IntegrationSqlSecurity.ValidateSelectOnly — kullanici tarafindan yazilan SQL
/// snippet'lerin guvenlik kontrolu. Lookup Function ile gelen serbest SELECT'leri
/// destruktif komutlardan koruyan SECURITY-CRITICAL kod. Bu testler ihlal eklendiginde
/// kirilmasi gereken testlerdir.
/// </summary>
public sealed class IntegrationSqlSecurityTests
{
    // ── Happy path ───────────────────────────────────────────────────────

    [Fact]
    public void ValidateSelectOnly_SimpleSelect_ReturnsNull()
    {
        var sql = "SELECT TOP 1 [Code] FROM [Items] WHERE [Id] = @Key";
        IntegrationSqlSecurity.ValidateSelectOnly(sql).Should().BeNull();
    }

    [Fact]
    public void ValidateSelectOnly_SelectWithJoin_ReturnsNull()
    {
        var sql = "SELECT a.[Code], b.[Name] FROM [A] a JOIN [B] b ON a.[Id] = b.[AId]";
        IntegrationSqlSecurity.ValidateSelectOnly(sql).Should().BeNull();
    }

    [Fact]
    public void ValidateSelectOnly_SelectWithCte_ReturnsNull()
    {
        var sql = "WITH x AS (SELECT 1 AS n) SELECT n FROM x";
        IntegrationSqlSecurity.ValidateSelectOnly(sql).Should().BeNull();
    }

    // ── Yasaklananlar — DDL ─────────────────────────────────────────────

    [Theory]
    [InlineData("DROP TABLE [Items]")]
    [InlineData("CREATE TABLE Foo (Id INT)")]
    [InlineData("ALTER TABLE Foo ADD x INT")]
    [InlineData("TRUNCATE TABLE Foo")]
    public void ValidateSelectOnly_DdlCommands_AreRejected(string sql)
    {
        IntegrationSqlSecurity.ValidateSelectOnly(sql).Should().NotBeNull();
    }

    // ── Yasaklananlar — DML ─────────────────────────────────────────────

    [Theory]
    [InlineData("DELETE FROM Items")]
    [InlineData("UPDATE Items SET Code = 'x'")]
    [InlineData("INSERT INTO Items (Code) VALUES ('x')")]
    [InlineData("MERGE Items USING Source ON ... WHEN MATCHED THEN UPDATE")]
    public void ValidateSelectOnly_DmlCommands_AreRejected(string sql)
    {
        IntegrationSqlSecurity.ValidateSelectOnly(sql).Should().NotBeNull();
    }

    // ── Yasaklananlar — security-critical ───────────────────────────────

    [Theory]
    [InlineData("EXEC sp_helpdb")]
    [InlineData("EXECUTE master.dbo.xp_cmdshell 'dir'")]
    [InlineData("GRANT SELECT ON Items TO Public")]
    [InlineData("REVOKE SELECT FROM Public")]
    [InlineData("BACKUP DATABASE Foo TO DISK = 'x.bak'")]
    [InlineData("RESTORE DATABASE Foo FROM DISK = 'x.bak'")]
    [InlineData("SHUTDOWN")]
    public void ValidateSelectOnly_SecurityCriticalCommands_AreRejected(string sql)
    {
        IntegrationSqlSecurity.ValidateSelectOnly(sql).Should().NotBeNull();
    }

    // ── Comment-stripping kontrolu ──────────────────────────────────────

    [Fact]
    public void ValidateSelectOnly_LineCommentDdl_StillRejected()
    {
        // Comment icinde DROP olsa bile parse'ta degerlendirilmez ama
        // ALT satirdaki DROP yakalanir.
        var sql = "SELECT 1; -- bu yorum\nDROP TABLE X";
        IntegrationSqlSecurity.ValidateSelectOnly(sql).Should().NotBeNull();
    }

    [Fact]
    public void ValidateSelectOnly_DdlInComment_PassesIfNoActualDdl()
    {
        // Comment icindeki "DROP" ignore edilir, gercek SELECT calisir
        var sql = "SELECT 1 -- DROP TABLE X";
        IntegrationSqlSecurity.ValidateSelectOnly(sql).Should().BeNull();
    }

    [Fact]
    public void ValidateSelectOnly_DdlInBlockComment_PassesIfNoActualDdl()
    {
        var sql = "SELECT 1 /* DROP TABLE X */";
        IntegrationSqlSecurity.ValidateSelectOnly(sql).Should().BeNull();
    }

    // ── Edge cases ──────────────────────────────────────────────────────

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ValidateSelectOnly_NullOrEmpty_ReturnsErrorMessage(string? sql)
    {
        IntegrationSqlSecurity.ValidateSelectOnly(sql).Should().NotBeNull();
    }

    [Fact]
    public void ValidateSelectOnly_NoSelectStatement_ReturnsErrorMessage()
    {
        var sql = "WITH x AS (1) GO";
        IntegrationSqlSecurity.ValidateSelectOnly(sql).Should().NotBeNull();
    }

    [Fact]
    public void ValidateSelectOnly_CaseInsensitive_RejectsAllCases()
    {
        IntegrationSqlSecurity.ValidateSelectOnly("drop table x").Should().NotBeNull();
        IntegrationSqlSecurity.ValidateSelectOnly("Drop Table X").Should().NotBeNull();
        IntegrationSqlSecurity.ValidateSelectOnly("DROP TABLE X").Should().NotBeNull();
    }
}
