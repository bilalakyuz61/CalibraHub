using System.Text.RegularExpressions;
using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Application.Abstractions.Services;
using CalibraHub.Application.Contracts;
using CalibraHub.Domain.Entities;

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

    public DocDesignerService(
        IDocLayoutRepository repo,
        IRptViewRepository views,
        IReportQueryExecutor executor,
        IDocLayoutRenderer renderer)
    {
        _repo = repo;
        _views = views;
        _executor = executor;
        _renderer = renderer;
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
        return id;
    }

    public Task DeleteAsync(int id, CancellationToken ct)
        => _repo.SoftDeleteAsync(id, ct);

    public async Task<byte[]> RenderPdfAsync(DocLayoutRunRequest req, CancellationToken ct)
    {
        var (layout, data) = await LoadLayoutWithDataAsync(req, ct);
        return _renderer.RenderPdf(layout.LayoutJson, data);
    }

    public async Task<string> RenderHtmlPreviewAsync(DocLayoutRunRequest req, CancellationToken ct)
    {
        var (layout, data) = await LoadLayoutWithDataAsync(req, ct);
        return _renderer.RenderHtml(layout.LayoutJson, data);
    }

    // ── Internals ──────────────────────────────────────────────────────────────

    private async Task<(DocLayout layout, IReadOnlyDictionary<string, ReportRawResult> data)>
        LoadLayoutWithDataAsync(DocLayoutRunRequest req, CancellationToken ct)
    {
        var layout = await _repo.GetByIdAsync(req.LayoutId, ct)
            ?? throw new InvalidOperationException($"DocLayout {req.LayoutId} bulunamadı.");

        var sources = await _repo.GetDataSourcesAsync(req.LayoutId, ct);
        var data = new Dictionary<string, ReportRawResult>(StringComparer.OrdinalIgnoreCase);

        foreach (var src in sources.OrderBy(s => s.Ordinal))
        {
            var sql = await BuildSqlAsync(src, ct);

            // ParamOverrides: alias → JSON parametreleri (gelecekte genişletilebilir)
            var sqlParams = Array.Empty<ReportSqlParameter>();

            var result = await _executor.ExecuteAsync(sql, sqlParams, ct);
            data[src.Alias] = result;
        }

        return (layout, data);
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
            )).ToList());
}
