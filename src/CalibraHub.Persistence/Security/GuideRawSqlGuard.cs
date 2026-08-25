using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace CalibraHub.Persistence.Security;

/// <summary>
/// Rehber (Guide) aramalarında İSTEMCİDEN gelen ham SQL koşul fragment'ini doğrular.
///
/// <para><b>Neden gerekli (2026-08-24 güvenlik denetimi, K1):</b> Rehber "constraints"
/// parametresi HTTP query'sinden gelir. İçindeki <c>RawSql</c> alanı aslında admin'in
/// Alan Ayarları'nda yazdığı filtre fragment'idir; ancak token'ları (<c>{#fieldId}</c>)
/// tarayıcı çözdüğü için sunucuya İSTEMCİ üzerinden geri döner. Dolayısıyla sunucu, gelen
/// metnin gerçekten admin yapılandırması mı yoksa kullanıcının URL'e yazdığı bir şey mi
/// olduğunu ayırt EDEMEZ. Fragment eskiden doğrudan WHERE'e ekleniyordu → kimliği
/// doğrulanmış herhangi bir kullanıcı UNION/alt-sorgu ile <c>Users.PasswordHash</c> dahil
/// tüm şirket veritabanını okuyabiliyordu.</para>
///
/// <para><b>Yaklaşım:</b> fragment <c>SELECT 1 WHERE (&lt;fragment&gt;)</c> olarak ScriptDom ile
/// parse edilir; yalnızca TEK bir SELECT ve alt-sorgusuz/tablo referanssız bir boolean ifade
/// kabul edilir; ardından koşul AST'den YENİDEN ÜRETİLİR — böylece yorum satırı (<c>--</c>,
/// <c>/* */</c>) ve statement kırma hileleri normalize edilerek yok olur.</para>
///
/// <para><b>Fail-closed:</b> doğrulama başarısızsa <see cref="ArgumentException"/> atılır.
/// Fragment'i sessizce düşürmek KISITLAYICI bir filtreyi yok edip veriyi FAZLA gösterirdi.</para>
///
/// <para><b>Kapsam dışı:</b> <c>GuideMas.DefaultFilterJson</c> — o sunucuda saklıdır, istemciden
/// gelmez ve kullanıcı kontrolünde değildir; oradaki meşru alt-sorgular çalışmaya devam eder.</para>
/// </summary>
public static class GuideRawSqlGuard
{
    /// <summary>Fragment'i doğrular ve normalize edilmiş halini döner.</summary>
    /// <exception cref="ArgumentException">Fragment ayrıştırılamazsa veya yasak bir ifade içeriyorsa.</exception>
    public static string Sanitize(string rawSql)
    {
        if (string.IsNullOrWhiteSpace(rawSql))
            throw new ArgumentException("Rehber filtresi boş olamaz.");

        var probe = "SELECT 1 WHERE (" + rawSql + ")";
        var parser = new TSql160Parser(initialQuotedIdentifiers: true);
        TSqlFragment parsed;
        IList<ParseError> errors;
        using (var reader = new StringReader(probe))
            parsed = parser.Parse(reader, out errors);

        if (errors is { Count: > 0 })
            throw new ArgumentException($"Rehber filtresi çözümlenemedi: {errors[0].Message}");

        // Tek batch + tek statement şartı, "; DROP TABLE ..." gibi statement kırmayı keser.
        if (parsed is not TSqlScript script || script.Batches.Count != 1
            || script.Batches[0].Statements.Count != 1
            || script.Batches[0].Statements[0] is not SelectStatement select)
            throw new ArgumentException("Rehber filtresi tek bir koşul ifadesi olmalıdır.");

        if (select.QueryExpression is not QuerySpecification spec || spec.WhereClause is null)
            throw new ArgumentException("Rehber filtresi geçerli bir koşul değil.");

        var guard = new PredicateGuard();
        spec.WhereClause.Accept(guard);
        if (guard.Rejected is not null)
            throw new ArgumentException($"Rehber filtresinde izin verilmeyen ifade: {guard.Rejected}");

        var generator = new Sql160ScriptGenerator(new SqlScriptGeneratorOptions
        {
            KeywordCasing = KeywordCasing.Uppercase,
            IncludeSemicolons = false,
            NewLineBeforeFromClause = false,
        });
        generator.GenerateScript(spec.WhereClause.SearchCondition, out var normalized);
        if (string.IsNullOrWhiteSpace(normalized))
            throw new ArgumentException("Rehber filtresi boş.");

        return normalized.Trim();
    }

    /// <summary>Alt sorgu / tablo referansı / küme işlemi yasak — bunlar UNION ve blind
    /// injection'ın taşıyıcısıdır. Sıradan kolon karşılaştırmaları serbesttir.</summary>
    private sealed class PredicateGuard : TSqlFragmentVisitor
    {
        public string? Rejected { get; private set; }
        public override void Visit(QuerySpecification node) => Rejected ??= "alt sorgu (SELECT)";
        public override void Visit(BinaryQueryExpression node) => Rejected ??= "UNION/EXCEPT/INTERSECT";
        public override void Visit(NamedTableReference node) => Rejected ??= "tablo referansı";
        public override void Visit(ExecuteStatement node) => Rejected ??= "EXEC";
        public override void Visit(WaitForStatement node) => Rejected ??= "WAITFOR";
        public override void Visit(GlobalVariableExpression node) => Rejected ??= "sistem değişkeni";
    }
}
