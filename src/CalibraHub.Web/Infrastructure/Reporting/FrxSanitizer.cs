using System.Xml.Linq;

namespace CalibraHub.Web.Infrastructure.Reporting;

/// <summary>
/// FastReport .frx dosyalarindan DB baglanti bilgilerini temizler ve yerine
/// sadece sema tanimi (BusinessObjectDataSource) enjekte eder.
///
/// Amac:
///   - Kullanici Designer'da .frx'i actiginda ConnectionString, Password,
///     Data Source, User ID gibi hassas alanlari GORMEMELI.
///   - Tasarim sirasinda alan listesi (sutunlar) yine de gorunmeli ki drag-drop
///     yapabilsin.
///   - Render sirasinda sunucu runtime'da report.RegisterData(dt, "Belge")
///     cagrisi ile veriyi baglar.
///
/// Kullanim:
///   var clean = FrxSanitizer.Strip(frxXml);
///   var withSchema = FrxSanitizer.InjectBusinessObject(clean, "Belge", columns);
/// </summary>
public static class FrxSanitizer
{
    /// <summary>
    /// Frx XML'inden baglanti iceren tum node ve attribute'lari kaldirir.
    /// Hedef node tiplerinin hepsi FastReport'ta baglanti tanimi olarak
    /// kullanilir (MsSql, OleDb, Odbc, XML dataconnection...).
    /// </summary>
    public static string Strip(string frxXml)
    {
        if (string.IsNullOrWhiteSpace(frxXml)) return frxXml;

        XDocument doc;
        try { doc = XDocument.Parse(frxXml); }
        catch { return frxXml; } // Parse edilemeyen xml'i dokunmadan birak

        // 1) Baglanti node'lari — hepsinde ConnectionString attribute'u var
        string[] connectionTypes =
        {
            "MsSqlDataConnection",
            "MsAccessDataConnection",
            "OleDbDataConnection",
            "OdbcDataConnection",
            "PostgresDataConnection",
            "MySqlDataConnection",
            "OracleDataConnection",
            "SqliteDataConnection",
            "FirebirdDataConnection",
            "XmlDataConnection",
            "JsonDataSourceConnection",
            "JsonDataConnection",
            "MongoDbDataConnection",
            "SqliteDbConnection",
        };

        foreach (var typeName in connectionTypes)
        {
            doc.Descendants().Where(e => e.Name.LocalName == typeName).Remove();
        }

        // 2) Hassas attribute'lari temizle (herhangi bir yerde olsa bile)
        string[] sensitiveAttrs = { "ConnectionString", "Password", "UserID", "UserId", "Pwd" };
        foreach (var el in doc.Descendants())
        {
            foreach (var attr in sensitiveAttrs)
            {
                var a = el.Attribute(attr);
                if (a != null) a.Remove();
            }
        }

        // 3) Dictionary altindaki Table/BusinessObject kaynaklarini temizle.
        //    InjectBusinessObject her acilista guncel alias listesini taze ekler;
        //    eski "Belge" gibi takip-edilmeyen kayitlar boylelikle Designer'da
        //    Veri Kaynagi panelinde fazladan gozukmez.
        var dict = doc.Root?.Elements()
            .FirstOrDefault(e => e.Name.LocalName == "Dictionary");
        if (dict is not null)
        {
            dict.Elements()
                .Where(e => e.Name.LocalName == "TableDataSource"
                         || e.Name.LocalName == "BusinessObjectDataSource")
                .Remove();
        }

        return SerializeWithDeclaration(doc);
    }

    /// <summary>
    /// Temizlenmis frx'e bir TableDataSource enjekte eder. Designer tasarim
    /// aninda bu tablo altindaki kolonlari drag-drop eder; runtime'da sunucu
    /// ayni isimli DataTable'i report.RegisterData ile baglar. Her iki tarafta
    /// da ayni TableDataSource tipi kullanildigi icin FastReport'un script
    /// compile asamasi sourcu tanir.
    /// </summary>
    public static string InjectBusinessObject(
        string frxXml, string rootName, IEnumerable<ReportColumnSchema> columns)
    {
        if (string.IsNullOrWhiteSpace(frxXml)) return frxXml;
        if (string.IsNullOrWhiteSpace(rootName)) rootName = "Belge";

        XDocument doc;
        try { doc = XDocument.Parse(frxXml); }
        catch { return frxXml; }

        var report = doc.Root;
        if (report is null) return frxXml;

        // Dictionary elementini bul veya olustur
        var dict = report.Elements().FirstOrDefault(e => e.Name.LocalName == "Dictionary");
        if (dict is null)
        {
            dict = new XElement("Dictionary");
            report.AddFirst(dict);
        }

        // Ayni isimde TableDataSource veya BusinessObject var mi? varsa kaldir
        var existing = dict.Elements()
            .Where(e => (e.Name.LocalName == "TableDataSource" || e.Name.LocalName == "BusinessObjectDataSource")
                     && (string?)e.Attribute("Name") == rootName)
            .ToList();
        foreach (var e in existing) e.Remove();

        // Yeni TableDataSource olustur — runtime RegisterData ile uyumlu.
        // DIKKAT:
        //  - Kok TableDataSource'ta DataType attribute'u KULLANMA (Column'lar icin gecerli).
        //  - ReferenceName KULLANMA — kendisine point ettiginde (self-reference)
        //    FastReport codegen source'u "proxy" sayip Belge identifier'ini emit etmez
        //    → [Belge.X] ifadeleri CS0103 uretir. ReferenceName sadece Connection altindaki
        //    tablolar icin (orn. "Conn1.Belge") anlamlidir.
        var ts = new XElement("TableDataSource",
            new XAttribute("Name",    rootName),
            new XAttribute("Enabled", "true"));

        foreach (var col in columns)
        {
            ts.Add(new XElement("Column",
                new XAttribute("Name",      col.Name),
                new XAttribute("DataType",  col.DataType ?? "System.String")));
        }

        dict.Add(ts);

        return SerializeWithDeclaration(doc);
    }

    /// <summary>
    /// Frx icindeki DataBand / DataHeader vb. node'larin DataSource attribute'unu
    /// normalize eder. Eski sablonlar `DataSource="Belge"` referansi tasiyabilir;
    /// stored sources sonradan farkli alias (orn. `vw_ReportDocument`) olarak
    /// kaydedildiginde bu eski referans kirik kalir → DataBand bos data ile
    /// 1 kez basar (tum satirlar basilmaz). Bu metod, bilinen alias listesinde
    /// olmayan DataSource referanslarini varsayilan alias ile (genelde tek source
    /// veya stored sources'in ilki) replace eder.
    /// </summary>
    public static string NormalizeDataSourceReferences(
        string frxXml, IReadOnlyCollection<string> knownAliases, string defaultAlias)
    {
        if (string.IsNullOrWhiteSpace(frxXml)) return frxXml;
        if (string.IsNullOrWhiteSpace(defaultAlias)) return frxXml;
        if (knownAliases.Count == 0) return frxXml;

        XDocument doc;
        try { doc = XDocument.Parse(frxXml); }
        catch { return frxXml; }

        var known = new HashSet<string>(knownAliases, StringComparer.OrdinalIgnoreCase);
        bool changed = false;

        foreach (var el in doc.Descendants())
        {
            var attr = el.Attribute("DataSource");
            if (attr is null) continue;
            var current = attr.Value;
            if (string.IsNullOrWhiteSpace(current)) continue;
            if (known.Contains(current)) continue;

            attr.Value = defaultAlias;
            changed = true;
        }

        return changed ? SerializeWithDeclaration(doc) : frxXml;
    }

    /// <summary>
    /// FastReport Designer .frx'i UTF-8 + XML declaration ile bekliyor.
    /// XDocument.ToString() declaration eklemiyor; elle serilize ediyoruz.
    /// </summary>
    private static string SerializeWithDeclaration(XDocument doc)
    {
        var decl = doc.Declaration ?? new XDeclaration("1.0", "utf-8", null);
        var body = doc.ToString(SaveOptions.DisableFormatting);
        return decl.ToString() + Environment.NewLine + body;
    }
}

public sealed record ReportColumnSchema(string Name, string DataType);
