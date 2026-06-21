using CalibraHub.Application.Services.Approval;

namespace CalibraHub.Application.Approval.EntityTypes;

/// <summary>
/// Parametreli belge entity tipi — her belge türü (e-Fatura, e-Arşiv, Satış Teklifi, ...)
/// için ayrı plugin instance oluşturulur ama field/parameter/context map mantığı
/// <see cref="DocumentApprovalEntityType"/> ile paylaşılır (kod tekrarı yok).
///
/// <para>
/// docTypeFilter parametresi şu an match akışında kullanılmıyor (Document'tan türeyen
/// tüm belgeler aynı ApprovalDocumentContext'i kullanır); ileride belge türüne özel
/// filtreleme gerektiğinde service tarafına forward edilir.
/// </para>
/// </summary>
public sealed class GenericDocumentApprovalEntityType : IApprovalEntityType
{
    private readonly IApprovalDocumentContextProvider _ctxProvider;
    private readonly string _code;
    private readonly string _label;
    private readonly string _icon;
    private readonly string? _docTypeFilter;

    public GenericDocumentApprovalEntityType(
        IApprovalDocumentContextProvider ctxProvider,
        string code,
        string label,
        string icon,
        string? docTypeFilter)
    {
        _ctxProvider   = ctxProvider ?? throw new ArgumentNullException(nameof(ctxProvider));
        _code          = code  ?? throw new ArgumentNullException(nameof(code));
        _label         = label ?? code;
        _icon          = icon  ?? "FileText";
        _docTypeFilter = docTypeFilter;
    }

    public string Code          => _code;
    public string Label         => _label;
    public string Icon          => _icon;
    public string GroupCategory => "Belgeler";

    /// <summary>Document tipinin filtrelendiği belge type code (örn. "satis_teklifi"); null → wildcard.</summary>
    public string? DocumentTypeFilter => _docTypeFilter;

    public IReadOnlyList<ApprovalEntityField> GetFields()
        => DocumentApprovalEntityType.CommonFields;

    public IReadOnlyList<ApprovalEntityParameter> GetSqlParameters()
        => DocumentApprovalEntityType.CommonParameters;

    public async Task<ApprovalEntityContext> BuildContextAsync(string entityId, CancellationToken ct)
    {
        if (!int.TryParse(entityId, out var documentIntId) || documentIntId <= 0)
        {
            return new ApprovalEntityContext { EntityTypeCode = Code, EntityId = entityId ?? "" };
        }

        var doc = await _ctxProvider.BuildAsync(documentIntId, ct);
        return DocumentApprovalEntityType.MapToEntityContext(doc, Code);
    }
}
