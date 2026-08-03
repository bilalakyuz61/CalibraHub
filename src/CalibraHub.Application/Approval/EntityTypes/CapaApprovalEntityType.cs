using CalibraHub.Application.Abstractions.Persistence;

namespace CalibraHub.Application.Approval.EntityTypes;

/// <summary>
/// DÖF (Düzeltici/Önleyici Faaliyet — CAPA) kapanış onayı entity tipi. "Capa" wildcard
/// Document tipinden AYRI bir kind'dır — Onay Akışları tasarımcısında ayrı bir entity olarak
/// listelenir ("DÖF Kapanış"). BuildContextAsync Capa'yı DocumentId (dof shell Document.Id)
/// üzerinden <see cref="ICapaRepository.GetDetailAsync"/> ile çözer; kural motoru en az
/// Severity üzerinden eşleme yapabilsin diye header + SQL parametrelerine dahil edilir.
/// </summary>
public sealed class CapaApprovalEntityType : IApprovalEntityType
{
    private readonly ICapaRepository _repo;

    public CapaApprovalEntityType(ICapaRepository repo)
    {
        _repo = repo;
    }

    /// <summary>
    /// "Capa" — CapaService.OnClosureApprovalCompletedAsync tetikleme akışıyla (StartApprovalRequest.EntityKind)
    /// ve ApprovalController/SqlApprovalInstanceRepository tarafındaki "Capa" string literalleriyle TUTARLI olmalı.
    /// </summary>
    public string Code          => "Capa";
    public string Label         => "DÖF Kapanış";
    public string Icon          => "ShieldCheck";
    public string GroupCategory => "Kalite";

    public IReadOnlyList<ApprovalEntityField> GetFields() => _fields;

    public IReadOnlyList<ApprovalEntityParameter> GetSqlParameters() => _sqlParams;

    public async Task<ApprovalEntityContext> BuildContextAsync(string entityId, CancellationToken ct)
    {
        if (!int.TryParse(entityId, out var documentId) || documentId <= 0)
            return new ApprovalEntityContext { EntityTypeCode = Code, EntityId = entityId ?? "" };

        var capa = await _repo.GetDetailAsync(documentId, ct);
        if (capa is null)
            return new ApprovalEntityContext { EntityTypeCode = Code, EntityId = entityId };

        var header = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["documentNumber"] = capa.DocumentNumber,
            ["title"]          = capa.Title,
            ["severity"]       = (decimal)capa.Severity,
            ["capaType"]       = (decimal)capa.CapaType,
            ["status"]         = (decimal)capa.Status,
        };

        var sqlParams = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["documentId"] = documentId,
            ["severity"]   = (int)capa.Severity,
            ["capaType"]   = (int)capa.CapaType,
            ["status"]     = (int)capa.Status,
        };

        return new ApprovalEntityContext
        {
            EntityTypeCode = Code,
            EntityId       = entityId,
            HeaderValues   = header,
            SqlParameters  = sqlParams,
        };
    }

    private static readonly IReadOnlyList<ApprovalEntityField> _fields = new[]
    {
        new ApprovalEntityField { Code="documentNumber", Label="DÖF No",          Type="text",    Scope="header", GroupLabel="DÖF Bilgileri" },
        new ApprovalEntityField { Code="title",          Label="Konu",            Type="text",    Scope="header", GroupLabel="DÖF Bilgileri" },
        new ApprovalEntityField { Code="severity",       Label="Önem Derecesi",   Type="numeric", Scope="header", GroupLabel="DÖF Bilgileri" /* CapaSeverity byte: 0=Dusuk,1=Orta,2=Yuksek,3=Kritik */ },
        new ApprovalEntityField { Code="capaType",       Label="DÖF Türü",        Type="numeric", Scope="header", GroupLabel="DÖF Bilgileri" /* CapaType byte: 0=Duzeltici,1=Onleyici */ },
        new ApprovalEntityField { Code="status",         Label="Durum",           Type="numeric", Scope="header", GroupLabel="DÖF Bilgileri" /* CapaStatus byte */ },
        new ApprovalEntityField { Code="sql.queryResult", Label="SQL Sorgu Sonucu", Type="sql",   Scope="sql",    GroupLabel="SQL Tabanlı Koşul" },
    };

    private static readonly IReadOnlyList<ApprovalEntityParameter> _sqlParams = new[]
    {
        new ApprovalEntityParameter { Name="documentId", Type="int", Description="DÖF belgesi ID (Document.Id)" },
        new ApprovalEntityParameter { Name="severity",   Type="int", Description="Önem derecesi (CapaSeverity byte)" },
        new ApprovalEntityParameter { Name="capaType",   Type="int", Description="DÖF türü (CapaType byte)" },
        new ApprovalEntityParameter { Name="status",     Type="int", Description="Durum (CapaStatus byte)" },
    };
}
