using System.Text.RegularExpressions;
using CalibraHub.Application.Abstractions.DesignProvider;
using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Application.Abstractions.Services;
using CalibraHub.Application.Contracts;
using CalibraHub.Domain.Entities;
using CalibraHub.Domain.Enums;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace CalibraHub.Application.Services;

public sealed partial class DocDesignerService : IDocDesignerService
{
    // Ad-hoc SQL güvenlik: sadece SELECT ile başlayan sorgulara izin ver,
    // noktalı virgül ile birden fazla ifadeyi engelle.
    [GeneratedRegex(@"^\s*SELECT\s", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex StartsWithSelect();

    private readonly IDocLayoutRepository _repo;
    private readonly IRptViewRepository _views;
    private readonly IReportQueryExecutor _executor;
    private readonly IDocLayoutRenderer _renderer;
    private readonly IMemoryCache _cache;
    private readonly ILogger<DocDesignerService> _logger;

    public DocDesignerService(
        IDocLayoutRepository repo,
        IRptViewRepository views,
        IReportQueryExecutor executor,
        IDocLayoutRenderer renderer,
        IMemoryCache cache,
        ILogger<DocDesignerService> logger)
    {
        _repo = repo;
        _views = views;
        _executor = executor;
        _renderer = renderer;
        _cache = cache;
        _logger = logger;
    }

    public Task<IReadOnlyCollection<DocLayoutSummaryDto>> ListAsync(string? docType, CancellationToken ct)
        => _repo.ListAsync(docType, ct);

    public async Task<DocLayoutDetailDto?> GetAsync(int id, CancellationToken ct)
    {
        var layout = await _repo.GetByIdAsync(id, ct);
        if (layout == null) return null;

        var sources = await _repo.GetDataSourcesAsync(id, ct);
        return ToDetailDto(layout, sources);
    }

    public async Task<int> SaveAsync(SaveDocLayoutRequest req, ReportCallerContext caller, CancellationToken ct)
    {
        var id = await _repo.UpsertAsync(req, caller.UserId, ct);
        await _repo.ReplaceDataSourcesAsync(id, req.DataSources, ct);

        // IsDefault singleton kuralı: kullanıcı bu tasarımı "Varsayılan" işaretlediyse
        // aynı DocType'taki DİĞER TÜM tasarımların IsDefault'ı false yapılır.
        // Aksi halde her save'de var sayılan bayrakları birden fazla layout'a yapışır
        // ve PrintDispatcher hangisini seçeceğini OrderBy UpdatedAt'a göre kararlar →
        // kullanıcı editliyor ama print farklı tasarımı kullanıyor (yaygın yanılgı).
        if (req.IsDefault)
            await _repo.SetDefaultAsync(id, ct);

        _cache.Remove(DesignProviderCacheKeys.Default(req.DocType));
        return id;
    }

    public async Task DeleteAsync(int id, CancellationToken ct)
    {
        // Layout silindiğinde hem default fallback hem de bu layout'a referans veren
        // rule'ların cache'i bayatlamış olabilir. DocType bilgisi gerekli.
        var existing = await _repo.GetByIdAsync(id, ct);
        await _repo.SoftDeleteAsync(id, ct);

        if (existing != null)
        {
            _cache.Remove(DesignProviderCacheKeys.Default(existing.DocType));
            _cache.Remove(DesignProviderCacheKeys.Rules(existing.DocType));
        }
    }

    public async Task SetDefaultAsync(int id, CancellationToken ct)
    {
        var existing = await _repo.GetByIdAsync(id, ct);
        await _repo.SetDefaultAsync(id, ct);

        // Bu DocType'taki tüm layoutların IsDefault bayrağı güncellendi → fallback cache temizle
        if (existing != null)
            _cache.Remove(DesignProviderCacheKeys.Default(existing.DocType));
    }

    /// <summary>
    /// 2026-06-03 — Mevcut layout'un kopyasını oluşturur. Yeni layout'ta:
    ///   - Name = "{original.Name} (Kopya)"
    ///   - Code = "{original.Code}_kopya_{yyyyMMddHHmmss}"
    ///   - IsDefault = false (klon her zaman kullanıcı tasarımı)
    ///   - OwnerUserId = 0 (persistence katmanı gerçek user'ı doldurur)
    ///   - Aynı bantlar (layoutJson) + aynı dataSources (yeni id ile)
    /// Kullanıcı klonu açıp özelleştirir; orijinal sistem varsayılanı korunur.
    /// </summary>
    public async Task<int> CloneAsync(int sourceId, CancellationToken ct)
    {
        var source = await _repo.GetByIdAsync(sourceId, ct);
        if (source == null)
            throw new InvalidOperationException($"Kopyalanacak tasarım bulunamadı (Id={sourceId}).");

        var sourceDs = await _repo.GetDataSourcesAsync(sourceId, ct);
        // Domain DocLayoutDs → SaveRequest DTO (Id ve LayoutId sıfır: yeni layout için)
        var cloneDsList = sourceDs.Select(ds => new DocLayoutDsDto(
            Id: 0, LayoutId: 0,
            Alias: ds.Alias, Role: ds.Role,
            ViewId: ds.ViewId, AdHocSql: ds.AdHocSql,
            JoinOn: ds.JoinOn, ParentAlias: ds.ParentAlias,
            Ordinal: ds.Ordinal)).ToList();

        var timestamp = DateTime.UtcNow.ToString("yyyyMMddHHmmss");
        var cloneReq = new SaveDocLayoutRequest(
            Id:           0,
            Code:         $"{source.Code}_kopya_{timestamp}",
            Name:         $"{source.Name} (Kopya)",
            DocType:      source.DocType,
            Description:  source.Description,
            LayoutJson:   source.LayoutJson,
            PageW:        source.PageW,
            PageH:        source.PageH,
            MarginTop:    source.MarginTop,
            MarginBot:    source.MarginBot,
            MarginLeft:   source.MarginLeft,
            MarginRight:  source.MarginRight,
            IsDefault:    false,
            DataSources:  cloneDsList,
            DocumentTypeId: source.DocumentTypeId,
            OutputFormat: source.OutputFormat,
            DefaultSubject:        source.DefaultSubject,
            DefaultBody:           source.DefaultBody,
            DefaultsViewName:      source.DefaultsViewName,
            DefaultsSubjectColumn: source.DefaultsSubjectColumn,
            DefaultsBodyColumn:    source.DefaultsBodyColumn,
            DefaultsWhere:         source.DefaultsWhere,
            UseAsMailTemplate:     source.UseAsMailTemplate
        );

        var newId = await _repo.UpsertAsync(cloneReq, 0, ct);
        await _repo.ReplaceDataSourcesAsync(newId, cloneReq.DataSources, ct);
        return newId;
    }

    public async Task<byte[]> RenderPdfAsync(DocLayoutRunRequest req, CancellationToken ct)
    {
        var (layout, data, meta) = await LoadLayoutWithDataAsync(req, ct);
        return _renderer.RenderPdf(layout.LayoutJson, data, meta);
    }

    public async Task<string> RenderHtmlPreviewAsync(DocLayoutRunRequest req, CancellationToken ct)
    {
        var (layout, data, meta) = await LoadLayoutWithDataAsync(req, ct);
        return _renderer.RenderHtml(layout.LayoutJson, data, meta);
    }

    // ── Internals ──────────────────────────────────────────────────────────────

    private async Task<(DocLayout layout, IReadOnlyDictionary<string, ReportRawResult> data, IReadOnlyList<DataSourceMeta> meta)>
        LoadLayoutWithDataAsync(DocLayoutRunRequest req, CancellationToken ct)
    {
        var layout = await _repo.GetByIdAsync(req.LayoutId, ct)
            ?? throw new InvalidOperationException($"DocLayout {req.LayoutId} bulunamadı.");

        var sources = await _repo.GetDataSourcesAsync(req.LayoutId, ct);
        var data = new Dictionary<string, ReportRawResult>(StringComparer.OrdinalIgnoreCase);
        // Renderer master-detail nesting için her kaynağın parent + join kolonunu
        // bilmeli. UI'dan gelen bu metadata'yı renderer'a iletiyoruz.
        var meta = sources.Select(s => new DataSourceMeta(s.Alias, s.ParentAlias, s.JoinOn)).ToList();

        // Run isteğindeki DocumentId tüm veri kaynaklarına @DocumentId parametresi olarak
        // geçirilir. Önizleme (preview) için DocumentId null gelir; bu durumda en son
        // aktif belgeyi fallback olarak kullan ki kullanıcı boş preview yerine gerçek
        // veriyle tasarımı görebilsin. Print akışı her zaman gerçek id verir.
        int? effectiveDocId = req.DocumentId;
        if (!effectiveDocId.HasValue)
        {
            try
            {
                var fallback = await _executor.ExecuteAsync(
                    "SELECT TOP 1 [id] FROM [dbo].[Document] WHERE [IsActive] = 1 ORDER BY [id] DESC",
                    Array.Empty<ReportSqlParameter>(), ct);
                if (fallback.Rows.Count > 0 && fallback.Rows[0].Count > 0 && fallback.Rows[0][0] != null)
                    effectiveDocId = Convert.ToInt32(fallback.Rows[0][0]);
            }
            catch { /* fallback bulunamazsa null kalsın, SQL'ler boş döner */ }
        }

        var globalParams = new List<ReportSqlParameter>();
        if (effectiveDocId.HasValue)
            globalParams.Add(new ReportSqlParameter("@DocumentId", ReportDataType.Integer, effectiveDocId.Value));

        _logger.LogInformation(
            "[DocDesigner] LayoutId={LayoutId} req.DocumentId={ReqDocId} effectiveDocId={EffectiveId} sources={SourceCount}",
            req.LayoutId, req.DocumentId, effectiveDocId, sources.Count);

        foreach (var src in sources.OrderBy(s => s.Ordinal))
        {
            var sql = await BuildSqlAsync(src, ct);

            // ParamOverrides: alias → JSON parametreleri (gelecekte genişletilebilir)
            var sqlParams = globalParams.Count > 0 ? globalParams : (IReadOnlyList<ReportSqlParameter>)Array.Empty<ReportSqlParameter>();

            var result = await _executor.ExecuteAsync(sql, sqlParams, ct);
            data[src.Alias] = result;

            // DIAGNOSTIC: kullanıcı "farklı belgelerin kalemleri geliyor" diye raporladı.
            // Her veri kaynağı için: çalıştırılan SQL, parametre değeri, dönen satır sayısı
            // ve özel olarak Kalem alias'ı için ilk birkaç satırın BelgeId değerleri loglanır.
            // Tek BelgeId değeri varsa filtreleme doğru; birden fazla varsa view veya WHERE bozuk.
            var belgeIdCol = -1;
            for (int i = 0; i < result.ColumnNames.Count; i++)
            {
                if (string.Equals(result.ColumnNames[i], "BelgeId", StringComparison.OrdinalIgnoreCase))
                {
                    belgeIdCol = i;
                    break;
                }
            }
            var distinctBelgeIds = belgeIdCol >= 0
                ? string.Join(",", result.Rows.Select(r => r.Count > belgeIdCol ? r[belgeIdCol]?.ToString() ?? "null" : "?").Distinct().Take(10))
                : "(BelgeId kolonu yok)";

            _logger.LogInformation(
                "[DocDesigner] alias={Alias} role={Role} rows={Rows} distinctBelgeIds=[{BelgeIds}] sql={Sql}",
                src.Alias, src.Role, result.Rows.Count, distinctBelgeIds, sql);
        }

        return (layout, data, meta);
    }

    private async Task<string> BuildSqlAsync(DocLayoutDs src, CancellationToken ct)
    {
        if (src.ViewId.HasValue)
        {
            var view = await _views.GetByIdAsync(src.ViewId.Value, ct)
                ?? throw new InvalidOperationException($"RptView {src.ViewId} bulunamadı.");
            return $"SELECT * FROM [{EscapeIdentifier(view.SqlObjectName)}]";
        }

        if (!string.IsNullOrWhiteSpace(src.AdHocSql))
        {
            // Güvenlik: ad-hoc SQL sadece admin tarafından kaydedilmiş olmalı.
            // SELECT ile başlamayan ve noktalı virgül içeren ifadeleri reddet.
            var sql = src.AdHocSql.Trim();
            if (!StartsWithSelect().IsMatch(sql))
                throw new InvalidOperationException($"DocLayoutDs alias '{src.Alias}' için sadece SELECT sorgusu izinlidir.");
            if (sql.Contains(';', StringComparison.Ordinal))
                throw new InvalidOperationException($"DocLayoutDs alias '{src.Alias}' için tek sorgu gereklidir (noktalı virgül yasak).");
            return sql;
        }

        throw new InvalidOperationException($"DocLayoutDs alias '{src.Alias}' için ne ViewId ne de AdHocSql tanımlı.");
    }

    private static string EscapeIdentifier(string name)
        => name.Replace("]", "]]");

    private static DocLayoutDetailDto ToDetailDto(DocLayout layout, IReadOnlyCollection<DocLayoutDs> sources)
        => new(
            Id:          layout.Id,
            Code:        layout.Code,
            Name:        layout.Name,
            DocType:     layout.DocType,
            Description: layout.Description,
            LayoutJson:  layout.LayoutJson,
            PageW:       layout.PageW,
            PageH:       layout.PageH,
            MarginTop:   layout.MarginTop,
            MarginBot:   layout.MarginBot,
            MarginLeft:  layout.MarginLeft,
            MarginRight: layout.MarginRight,
            IsDefault:   layout.IsDefault,
            OwnerUserId: layout.OwnerUserId,
            DataSources: sources.Select(s => new DocLayoutDsDto(
                s.Id, s.LayoutId, s.Alias, s.Role,
                s.ViewId, s.AdHocSql, s.JoinOn, s.ParentAlias, s.Ordinal
            )).ToList(),
            DocumentTypeId: layout.DocumentTypeId,
            OutputFormat:   layout.OutputFormat,
            DefaultSubject: layout.DefaultSubject,
            DefaultBody:    layout.DefaultBody,
            DefaultsViewName:      layout.DefaultsViewName,
            DefaultsSubjectColumn: layout.DefaultsSubjectColumn,
            DefaultsBodyColumn:    layout.DefaultsBodyColumn,
            DefaultsWhere:         layout.DefaultsWhere,
            UseAsMailTemplate:     layout.UseAsMailTemplate);
}
