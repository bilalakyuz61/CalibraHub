using System.Text;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace CalibraHub.Persistence.Security;

/// <summary>
/// Salt-okunur SQL kapısı: verilen metnin YALNIZCA veri okuduğunu ScriptDom (gerçek
/// T-SQL ayrıştırıcısı) ile kanıtlar; veri/şema değiştiren en ufak bir ifade varsa
/// <see cref="ArgumentException"/> fırlatır (fail-closed).
///
/// <para><b>Neden (2026-08-24 güvenlik denetimi, ORTA):</b> Rapor motoru
/// (<c>ReportQueryService</c>) hem kayıtlı kaynak SQL'ini hem de panel
/// tasarımcısından gelen "inline" SQL'i hiçbir doğrulama yapmadan çalıştırıyordu.
/// Inline uç yalnızca <i>rapor görüntüleme</i> yetkisi istediği için, sıradan bir
/// kullanıcı <c>UPDATE</c>/<c>DROP</c>/<c>EXEC</c> gönderip şirket veritabanını
/// değiştirebilirdi. <c>ViewBuilderService</c> aynı işi zaten doğru yapıyordu;
/// buradaki amaç o deseni tek ve yeniden kullanılabilir bir kapıya indirmek.</para>
///
/// <para><b>Politika — bilinçli olarak ViewBuilder'dan biraz daha geniş:</b>
/// ViewBuilder "tek bir SELECT" şartı koyar (view gövdesi olacağı için doğru).
/// Raporlarda ise <c>DECLARE</c>/<c>SET</c>/CTE/<c>IF</c> gibi okuma amaçlı yardımcı
/// ifadeler meşru biçimde kullanılıyor; bunları yasaklamak çalışan raporları
/// kırardı. Bu yüzden kural şudur: <b>mutasyon ve yan etki üreten hiçbir ifade
/// bulunmayacak</b> ve metin en az bir SELECT içerecek. Bilinmeyen/yeni bir ifade
/// türü çıkarsa REDDEDİLİR (allowlist mantığı) — sessizce geçmez.</para>
/// </summary>
public static class ReadOnlySqlGuard
{
    /// <summary>Salt-okunur değilse <see cref="ArgumentException"/> fırlatır.</summary>
    public static void EnsureSelectOnly(string? sql, string context = "Sorgu")
    {
        if (string.IsNullOrWhiteSpace(sql))
            throw new ArgumentException($"{context} boş olamaz.");

        var parser = new TSql160Parser(initialQuotedIdentifiers: true);
        TSqlFragment fragment;
        IList<ParseError> parseErrors;
        using (var reader = new StringReader(sql))
            fragment = parser.Parse(reader, out parseErrors);

        if (parseErrors is { Count: > 0 })
            throw new ArgumentException(
                $"{context} ayrıştırılamadı ({parseErrors[0].Line}:{parseErrors[0].Column}): {parseErrors[0].Message}");

        if (fragment is not TSqlScript script || script.Batches.Count == 0)
            throw new ArgumentException($"{context} çalıştırılabilir bir SQL içermiyor.");

        var hasSelect = false;
        foreach (var batch in script.Batches)
        {
            foreach (var statement in batch.Statements)
            {
                switch (statement)
                {
                    case SelectStatement:
                        hasSelect = true;
                        break;
                    case DeclareVariableStatement:
                    case SetVariableStatement:
                    case IfStatement:
                    case BeginEndBlockStatement:
                    case PrintStatement:
                        break;
                    default:
                        throw new ArgumentException(
                            $"{context} yalnızca veri okuyabilir. İzin verilmeyen ifade: {Describe(statement)}.");
                }
            }
        }

        if (!hasSelect)
            throw new ArgumentException($"{context} en az bir SELECT içermelidir.");

        // İç içe (alt sorgu / IF gövdesi / CTE içi) mutasyonları da yakala:
        // yukarıdaki tür kontrolü yalnızca ÜST seviyeye bakar.
        var visitor = new MutationVisitor();
        fragment.Accept(visitor);
        if (visitor.Found is not null)
            throw new ArgumentException($"{context} yalnızca veri okuyabilir. İzin verilmeyen ifade: {visitor.Found}.");
    }

    private static string Describe(TSqlStatement s) => s.GetType().Name
        .Replace("Statement", string.Empty, StringComparison.Ordinal);

    /// <summary>Herhangi bir derinlikte mutasyon / yan etki üreten ifadeleri arar.</summary>
    private sealed class MutationVisitor : TSqlFragmentVisitor
    {
        public string? Found { get; private set; }
        private void Flag(string what) => Found ??= what;

        public override void Visit(InsertStatement node) => Flag("INSERT");
        public override void Visit(UpdateStatement node) => Flag("UPDATE");
        public override void Visit(DeleteStatement node) => Flag("DELETE");
        public override void Visit(MergeStatement node) => Flag("MERGE");
        public override void Visit(TruncateTableStatement node) => Flag("TRUNCATE");
        public override void Visit(ExecuteStatement node) => Flag("EXEC");
        public override void Visit(ExecuteAsStatement node) => Flag("EXECUTE AS");
        public override void Visit(DropTableStatement node) => Flag("DROP");
        public override void Visit(DropIndexStatement node) => Flag("DROP INDEX");
        public override void Visit(DropObjectsStatement node) => Flag("DROP");
        public override void Visit(CreateTableStatement node) => Flag("CREATE TABLE");
        public override void Visit(CreateViewStatement node) => Flag("CREATE VIEW");
        public override void Visit(CreateProcedureStatement node) => Flag("CREATE PROCEDURE");
        public override void Visit(AlterTableStatement node) => Flag("ALTER TABLE");
        public override void Visit(SecurityStatement node) => Flag("GRANT/DENY/REVOKE");
        public override void Visit(BackupStatement node) => Flag("BACKUP");
        public override void Visit(WaitForStatement node) => Flag("WAITFOR");   // zaman tabanlı DoS/blind
        public override void Visit(BeginTransactionStatement node) => Flag("BEGIN TRAN");
        public override void Visit(SelectStatement node)
        {
            // SELECT ... INTO yeni tablo yaratir → mutasyon.
            if (node.Into is not null) Flag("SELECT INTO");
            base.Visit(node);
        }
    }
}
