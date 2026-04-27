using System.Data;
using System.Text;
using System.Xml.Linq;
using CalibraHub.Application.Abstractions.Services;
using FastReport;
using FastReport.Data;
using FastReport.Export.Html;
using FastReport.Export.PdfSimple;
using Microsoft.AspNetCore.Hosting;

namespace CalibraHub.Infrastructure.Reporting;

public sealed class FastReportService : IReportService
{
    private readonly string _webRootPath;

    public FastReportService(IWebHostEnvironment env)
    {
        _webRootPath = env.WebRootPath;
    }

    public async Task<byte[]> ExportPdfAsync(string frxFilePath, DataTable data, CancellationToken ct = default)
    {
        var fullPath = ResolvePath(frxFilePath);

        EnsureLegacyAliasColumns(data);

        var frxBytes = await File.ReadAllBytesAsync(fullPath, ct);
        var cleanFrx = FixSelfReferencingDataSources(frxBytes);

        using var report = new Report();
        using (var loadStream = new MemoryStream(cleanFrx))
        {
            report.Load(loadStream);
        }
        // Iki isimle de kaydet — yeni BusinessObject tasarimlari ([Belge.X]) ve
        // eski sablonlar ([Data.X]) ayni verileri ayni tablodan okur.
        // Dictionary'deki BO ile cakismasin diye BO'yu kaldirip TableDataSource ekliyoruz,
        // sonra Enable + FastReport script'inin erisebilmesi icin dictionary'ye add.
        PrepareDataSource(report, data, "Belge");
        if (report.GetDataSource("Data") == null) PrepareDataSource(report, data, "Data");

        report.Prepare();

        using var ms = new MemoryStream();
        var export = new PDFSimpleExport();
        report.Export(export, ms);
        return ms.ToArray();
    }

    public async Task<string> ExportHtmlAsync(string frxFilePath, DataTable data, CancellationToken ct = default)
    {
        var fullPath = ResolvePath(frxFilePath);

        EnsureLegacyAliasColumns(data);

        var frxBytes = await File.ReadAllBytesAsync(fullPath, ct);
        var cleanFrx = FixSelfReferencingDataSources(frxBytes);

        using var report = new Report();
        using (var loadStream = new MemoryStream(cleanFrx))
        {
            report.Load(loadStream);
        }
        // Iki isimle de kaydet — yeni BusinessObject tasarimlari ([Belge.X]) ve
        // eski sablonlar ([Data.X]) ayni verileri ayni tablodan okur.
        // Dictionary'deki BO ile cakismasin diye BO'yu kaldirip TableDataSource ekliyoruz,
        // sonra Enable + FastReport script'inin erisebilmesi icin dictionary'ye add.
        PrepareDataSource(report, data, "Belge");
        if (report.GetDataSource("Data") == null) PrepareDataSource(report, data, "Data");

        report.Prepare();

        using var ms = new MemoryStream();
        var export = new HTMLExport
        {
            SinglePage = true,
            Navigator = false,
            EmbedPictures = true,
        };
        report.Export(export, ms);
        ms.Position = 0;
        using var reader = new StreamReader(ms);
        return await reader.ReadToEndAsync(ct);
    }

    public Task<byte[]> ExportPdfFromBytesAsync(byte[] frxContent, DataTable data, CancellationToken ct = default)
    {
        // Geriye uyumlu single-source cagrisi — [Belge.X] + [Data.X] ikisi de ayni tabloyu gosterir.
        var map = new Dictionary<string, DataTable>(StringComparer.OrdinalIgnoreCase)
        {
            ["Belge"] = data,
            ["Data"]  = data,
        };
        return ExportPdfFromBytesAsync(frxContent, (IReadOnlyDictionary<string, DataTable>)map, ct);
    }

    public Task<byte[]> ExportPdfFromBytesAsync(byte[] frxContent, IReadOnlyDictionary<string, DataTable> sources, CancellationToken ct = default)
    {
        if (frxContent is null || frxContent.Length == 0)
            throw new InvalidOperationException("FRX icerigi bos.");
        if (sources is null || sources.Count == 0)
            throw new InvalidOperationException("En az bir data source gerekli.");

        foreach (var kv in sources) EnsureLegacyAliasColumns(kv.Value);

        var cleanFrx = FixSelfReferencingDataSources(frxContent);

        // Frx'ten DataBand.Name → DataSource.Name eslemesini topla;
        // Load sonrasi DataBand.DataSource null / yanlis baglanirsa manuel rebind icin.
        var bandToSource = ExtractBandDataSourceMap(cleanFrx);

        using var report = new Report();
        using (var loadStream = new MemoryStream(cleanFrx))
        {
            report.Load(loadStream);
        }

        foreach (var kv in sources)
        {
            PrepareDataSource(report, kv.Value, kv.Key);
        }

        // RegisterData sonrasi: frx'teki her DataBand'in DataSource'unu yeni
        // TableDataSource instance'ina point et. PrepareDataSource'un kendi
        // rebind'i existing null ise atlaniyor; burasi guvence olarak calisir.
        RebindDataBandsByName(report, bandToSource);

        report.Prepare();

        using var ms = new MemoryStream();
        var export = new PDFSimpleExport();
        report.Export(export, ms);
        return Task.FromResult(ms.ToArray());
    }

    public Task<string> ExportHtmlFromBytesAsync(byte[] frxContent, DataTable data, CancellationToken ct = default)
    {
        var map = new Dictionary<string, DataTable>(StringComparer.OrdinalIgnoreCase)
        {
            ["Belge"] = data,
            ["Data"]  = data,
        };
        return ExportHtmlFromBytesAsync(frxContent, (IReadOnlyDictionary<string, DataTable>)map, ct);
    }

    public async Task<string> ExportHtmlFromBytesAsync(byte[] frxContent, IReadOnlyDictionary<string, DataTable> sources, CancellationToken ct = default)
    {
        if (frxContent is null || frxContent.Length == 0)
            throw new InvalidOperationException("FRX icerigi bos.");
        if (sources is null || sources.Count == 0)
            throw new InvalidOperationException("En az bir data source gerekli.");

        foreach (var kv in sources) EnsureLegacyAliasColumns(kv.Value);

        var cleanFrx = FixSelfReferencingDataSources(frxContent);
        var bandToSource = ExtractBandDataSourceMap(cleanFrx);

        using var report = new Report();
        using (var loadStream = new MemoryStream(cleanFrx))
        {
            report.Load(loadStream);
        }

        foreach (var kv in sources)
        {
            PrepareDataSource(report, kv.Value, kv.Key);
        }

        RebindDataBandsByName(report, bandToSource);

        report.Prepare();

        using var ms = new MemoryStream();
        var export = new HTMLExport
        {
            SinglePage = true,
            Navigator = false,
            EmbedPictures = true,
        };
        report.Export(export, ms);
        ms.Position = 0;
        using var reader = new StreamReader(ms);
        return await reader.ReadToEndAsync(ct);
    }

    /// <summary>
    /// Runtime'da veriyi TableDataSource'a bind eder.
    /// Frx design-time'da TableDataSource "Belge" olarak tanimli (Column listesi de icinde)
    /// fakat Table=null — bu yuzden Prepare asamasinda "Table is not connected" hatasi cikar.
    ///
    /// KRITIK: Report.Load() sirasinda DataBand.DataSource property'si str "Belge"
    /// adindan TableDataSource nesnesine resolve edilir (direct reference saklanir).
    /// Bu referansi KALDIRIRSAK DataBand.DataSource null olur → NullReferenceException.
    /// O yuzden source'u kaldirmiyoruz; sadece .Table property'sini set ediyoruz.
    ///
    /// Eger source mevcut degilse (yeni / bos frx) RegisterData ile olusturuyoruz.
    /// </summary>
    private static void PrepareDataSource(Report report, DataTable data, string sourceName)
    {
        // STRATEJI: Design-time TableDataSource'un internal state'i (_table field,
        // Connection parent, column meta) bozulmus olabilir; sadece .Table = data
        // atamasi LoadDataShared'de NRE atar. En guvenilir yol:
        //   1) DataBand'lerin mevcut source'a olan reference'ini KAYDET
        //   2) Mevcut source'u Dictionary'den kaldir
        //   3) RegisterData ile fresh bir TableDataSource + Connection olustur
        //   4) DataBand'leri yeni source'a yeniden bagla
        // Bu sayede codegen yeni source'u Dictionary'de bulur (CS0103 yok), LoadData
        // calisir (Table bagli), DataBand'ler dogru source'a point eder.

        // frx'te tanimli ama DataTable'da olmayan kolonlari ekle (DBNull placeholder).
        // Eski sablonda var olup view'da artik bulunmayan widget kolonlari bozmasin.
        var existing = report.GetDataSource(sourceName);
        if (existing is FastReport.Data.TableDataSource existingTs)
        {
            EnsureColumnsExistInData(existingTs, data);
        }

        // ADIM 1: DataBand'leri kaydet
        var bandsToRebind = new List<FastReport.DataBand>();
        if (existing != null)
        {
            foreach (var obj in report.AllObjects)
            {
                if (obj is FastReport.DataBand db && ReferenceEquals(db.DataSource, existing))
                {
                    bandsToRebind.Add(db);
                }
            }
        }

        // ADIM 2: Mevcut source'u Dictionary'den kaldir (Parent null'lama yerine
        // resmi yol: collection'dan Remove). Dispose CAGIRMA — Dictionary'yi
        // bozar. Sadece Dictionary.DataSources.Remove yeter.
        if (existing != null)
        {
            try
            {
                if (report.Dictionary.DataSources.Contains(existing))
                {
                    report.Dictionary.DataSources.Remove(existing);
                }
                else
                {
                    // Connection altindaysa: Parent ile gezin ve Remove
                    var parent = existing.Parent;
                    if (parent is FastReport.Base parentBase)
                    {
                        try
                        {
                            // Base.Remove cocugu parent'tan kaldirir
                            parentBase.GetType()
                                .GetMethod("RemoveChild", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?
                                .Invoke(parentBase, new object[] { existing });
                        }
                        catch { /* swallow */ }
                    }
                }
            }
            catch { /* swallow */ }
        }

        // ADIM 3: Fresh RegisterData
        report.RegisterData(data, sourceName);
        var ds = report.GetDataSource(sourceName);
        if (ds == null) return;

        ds.Enabled = true;
        foreach (var col in ds.Columns)
        {
            try { ((dynamic)col).Enabled = true; } catch { /* swallow */ }
        }

        // ADIM 4: DataBand'leri yeni source'a rebind et
        foreach (var band in bandsToRebind)
        {
            band.DataSource = ds;
        }
    }

    private sealed record DataBandMeta(string DataSource, string? MasterData, string? GroupHeader);

    /// <summary>
    /// Frx XML'inden her DataBand'in Name → (DataSource, MasterData, GroupHeader)
    /// eslemesini cikartir. Load sonrasi bu referanslar null / yanlis
    /// olabilir; bu map ile manuel bagliyoruz.
    /// </summary>
    private static Dictionary<string, DataBandMeta> ExtractBandDataSourceMap(byte[] frxBytes)
    {
        var map = new Dictionary<string, DataBandMeta>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var xml = XDocument.Parse(Encoding.UTF8.GetString(frxBytes));
            foreach (var el in xml.Descendants().Where(e => e.Name.LocalName == "DataBand"))
            {
                var name   = el.Attribute("Name")?.Value;
                var ds     = el.Attribute("DataSource")?.Value;
                var master = el.Attribute("MasterData")?.Value;
                var gh     = el.Attribute("GroupHeader")?.Value;
                if (!string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(ds))
                    map[name] = new DataBandMeta(
                        ds,
                        string.IsNullOrWhiteSpace(master) ? null : master,
                        string.IsNullOrWhiteSpace(gh) ? null : gh);
            }
        }
        catch { /* swallow — XML parse hatasi */ }
        return map;
    }

    /// <summary>
    /// Her DataBand'i adiyla bulup DataSource, MasterData ve GroupHeader
    /// referanslarini yeniden baglar. FastReport OpenSource bazi property'leri
    /// Load sirasinda resolve etmiyor; reflection ile guvenli set ederiz.
    /// </summary>
    private static void RebindDataBandsByName(Report report, Dictionary<string, DataBandMeta> map)
    {
        if (map.Count == 0) return;

        static void TrySetProperty(object target, string[] candidateNames, object value)
        {
            var t = target.GetType();
            foreach (var name in candidateNames)
            {
                var prop = t.GetProperty(name);
                if (prop is null) continue;
                try { prop.SetValue(target, value); return; }
                catch { /* try next */ }
            }
        }

        foreach (var kv in map)
        {
            if (report.FindObject(kv.Key) is not FastReport.DataBand band) continue;

            var ds = report.GetDataSource(kv.Value.DataSource);
            if (ds is not null) band.DataSource = ds;

            if (!string.IsNullOrWhiteSpace(kv.Value.MasterData)
                && report.FindObject(kv.Value.MasterData) is FastReport.DataBand masterBand)
            {
                TrySetProperty(band, new[] { "MasterData", "Master" }, masterBand);
            }

            if (!string.IsNullOrWhiteSpace(kv.Value.GroupHeader)
                && report.FindObject(kv.Value.GroupHeader) is object groupHeader)
            {
                TrySetProperty(band, new[] { "GroupHeader", "GroupHeaderBand" }, groupHeader);
            }
        }
    }

    /// <summary>
    /// TableDataSource'taki kolonlar ile DataTable kolonlarini senkron tut.
    /// frx'te olup veri setinde olmayan kolonlari DataTable'a DBNull placeholder
    /// olarak ekler. Bu, eski tasarimda var olup daha sonra view'dan silinen/yeniden
    /// adlandirilan widget kolonlarinin LoadDataShared NRE'sini onler.
    /// </summary>
    private static void EnsureColumnsExistInData(FastReport.Data.TableDataSource ts, DataTable data)
    {
        foreach (var col in ts.Columns)
        {
            string colName;
            try { colName = ((dynamic)col).Name as string ?? string.Empty; }
            catch { continue; }

            if (string.IsNullOrEmpty(colName)) continue;
            if (data.Columns.Contains(colName)) continue;

            Type colType = typeof(string);
            try
            {
                var dt = ((dynamic)col).DataType as Type;
                if (dt != null) colType = dt;
            }
            catch { /* default string */ }

            // Nullable reference — DataColumn default olarak AllowDBNull=true.
            // Row eklenmez; sadece sema genisler, mevcut satirlar DBNull olarak gorunur.
            try
            {
                data.Columns.Add(colName, colType);
            }
            catch { /* dupe, invalid name vs. — sessizce gec */ }
        }
    }

    /// <summary>
    /// FastReport OpenSource'un bilinen bir sorunu: TableDataSource'un
    /// <c>ReferenceName</c> attribute'u kendi <c>Name</c>'ine esitse
    /// (ornek: <c>Name="Belge" ReferenceName="Belge"</c>) source "proxy"
    /// sayilir ve script codegen'de identifier emit edilmez. Bu durumda
    /// [Belge.X] ifadeleri <c>CS0103: 'Belge' does not exist</c> uretir.
    /// Load oncesi bu attribute'u cikartiyoruz.
    /// </summary>
    private static byte[] FixSelfReferencingDataSources(byte[] frxBytes)
    {
        try
        {
            var frxText = Encoding.UTF8.GetString(frxBytes);
            var doc = XDocument.Parse(frxText);

            var changed = false;
            foreach (var ts in doc.Descendants().Where(e =>
                         e.Name.LocalName == "TableDataSource" ||
                         e.Name.LocalName == "BusinessObjectDataSource"))
            {
                var name = (string?)ts.Attribute("Name");
                var refName = ts.Attribute("ReferenceName");
                if (refName == null) continue;
                // Self-reference — kaldir
                if (string.Equals(refName.Value, name, StringComparison.Ordinal))
                {
                    refName.Remove();
                    changed = true;
                }
            }

            if (!changed) return frxBytes;

            var decl = doc.Declaration ?? new XDeclaration("1.0", "utf-8", null);
            var cleanText = decl + Environment.NewLine + doc.ToString(SaveOptions.DisableFormatting);
            return Encoding.UTF8.GetBytes(cleanText);
        }
        catch
        {
            return frxBytes;
        }
    }

    /// <summary>
    /// Load'dan ONCE .frx xml'den belirtilen isimli TableDataSource /
    /// BusinessObjectDataSource / BusinessObject dugumlerini strip eder.
    /// Runtime'da RegisterData tamamen temiz bir source yaratir; FastReport
    /// script compile'in "Belge" adini tanimasi garanti olur.
    /// </summary>
    private static byte[] StripDesignTimeSchemaSources(byte[] frxBytes, params string[] sourceNames)
    {
        try
        {
            var frxText = Encoding.UTF8.GetString(frxBytes);
            var doc = XDocument.Parse(frxText);
            var targetNames = new HashSet<string>(sourceNames, StringComparer.OrdinalIgnoreCase);

            var nodesToRemove = doc.Descendants()
                .Where(e => e.Name.LocalName is "TableDataSource" or "BusinessObjectDataSource" or "BusinessObject"
                         && targetNames.Contains((string?)e.Attribute("Name") ?? string.Empty))
                .ToList();
            foreach (var n in nodesToRemove) n.Remove();

            var decl = doc.Declaration ?? new XDeclaration("1.0", "utf-8", null);
            var cleanText = decl + Environment.NewLine + doc.ToString(SaveOptions.DisableFormatting);
            return Encoding.UTF8.GetBytes(cleanText);
        }
        catch
        {
            // XML parse edilemezse dokunmadan geri don
            return frxBytes;
        }
    }

    /// <summary>
    /// Legacy template'lerde [Data.ProductCode], [Data.ProductName] gibi
    /// artik view'da bulunmayan alan referansi olursa FastReport compile
    /// hatasi verir (CS0234). Burada DataTable'a bos/alias kolon ekleyerek
    /// eski sablonlarin kirilmadan render edilmesini sagliyoruz.
    /// </summary>
    private static void EnsureLegacyAliasColumns(DataTable dt)
    {
        // Bilinen alias -> gercek kolon haritasi (vw_ReportDocument uyumu)
        var aliases = new (string Alias, string? SourceColumn)[]
        {
            ("ProductCode",  "MalzemeKodu"),
            ("ProductName",  "MalzemeAdi"),
            ("BarcodeValue", "MalzemeKodu"),
        };
        foreach (var (alias, sourceColumn) in aliases)
        {
            if (dt.Columns.Contains(alias)) continue;
            var col = dt.Columns.Add(alias, typeof(string));
            if (sourceColumn != null && dt.Columns.Contains(sourceColumn))
            {
                foreach (DataRow row in dt.Rows)
                {
                    var val = row[sourceColumn];
                    row[alias] = val == DBNull.Value ? (object)DBNull.Value : val.ToString() ?? string.Empty;
                }
            }
        }
    }

    private string ResolvePath(string frxFilePath)
    {
        var fullPath = Path.IsPathRooted(frxFilePath)
            ? frxFilePath
            : Path.Combine(_webRootPath, "Document", frxFilePath);

        if (!File.Exists(fullPath))
            throw new FileNotFoundException($"FRX sablon dosyasi bulunamadi: {fullPath}");

        return fullPath;
    }
}
