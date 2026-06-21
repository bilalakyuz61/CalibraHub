using CalibraHub.Application.Abstractions.Persistence;
using Microsoft.Extensions.Logging;

namespace CalibraHub.Application.Services.Approval;

/// <summary>
/// Faz 4 Runtime — DecisionEvaluator + Notification dispatch için tek noktadan
/// belge bağlamı. IncomingDocument tabanlı (e-fatura vb.) header bilgileri taşır;
/// satır/cari grubu/material grubu verisi opsiyoneldir — bağlam üretirken kaynak
/// veri eksikse boş bırakılır (decision rule eksik veri için "false" döner).
/// </summary>
public sealed class ApprovalDocumentContext
{
    public int? DocumentId { get; init; }
    public string? DocumentNumber { get; init; }
    public string DocumentKind { get; init; } = "";
    public decimal Amount { get; init; }
    public DateTime DocumentDate { get; init; }
    public int? ContactId { get; init; }
    public string? ContactCode { get; init; }  // VKN / Tax No
    public string? ContactName { get; init; }
    /// <summary>level 1-5 → cardGroupId (entityType=2 contact mapping).</summary>
    public IReadOnlyDictionary<int, int> ContactGroupByLevel { get; init; } = new Dictionary<int, int>();
    public int? CreatedByUserId { get; init; }
    public int? CreatedByDepartmentId { get; init; }
    public IReadOnlyList<DocumentLineCtx> Lines { get; init; } = Array.Empty<DocumentLineCtx>();
}

public sealed class DocumentLineCtx
{
    public int? ItemId { get; init; }
    public string? ItemCode { get; init; }
    public string? ItemName { get; init; }
    public decimal Quantity { get; init; }
    public decimal UnitPrice { get; init; }
    public decimal LineTotal { get; init; }
    /// <summary>level 1-5 → groupCode (MaterialGroupMapping).</summary>
    public IReadOnlyDictionary<int, string> MaterialGroupByLevel { get; init; } = new Dictionary<int, string>();
}

public interface IApprovalDocumentContextProvider
{
    Task<ApprovalDocumentContext> BuildAsync(int documentId, CancellationToken ct);
}

/// <summary>
/// IncomingDocument tabanlı context builder. Onay sistemi şu an gelen e-faturalar
/// üzerinde çalıştığı için belge body'sinden tutar/iletişim bilgisi çekme TODO —
/// MVP'de sadece header (DocumentNumber, IssueDate, SenderTaxNumber) doldurulur.
/// </summary>
public sealed class ApprovalDocumentContextProvider : IApprovalDocumentContextProvider
{
    private readonly IDocumentRepository _documentRepo;
    private readonly ICardGroupRepository _cardGroupRepo;
    private readonly ILogger<ApprovalDocumentContextProvider> _logger;

    public ApprovalDocumentContextProvider(
        IDocumentRepository documentRepo,
        ICardGroupRepository cardGroupRepo,
        ILogger<ApprovalDocumentContextProvider> logger)
    {
        _documentRepo = documentRepo;
        _cardGroupRepo = cardGroupRepo;
        _logger = logger;
    }

    public async Task<ApprovalDocumentContext> BuildAsync(int documentId, CancellationToken ct)
    {
        var doc = await _documentRepo.GetByIdAsync(documentId, ct);
        if (doc is null)
        {
            _logger.LogWarning("ApprovalDocumentContext: Document bulunamadı (Id={IntId}).", documentId);
            return new ApprovalDocumentContext { DocumentId = documentId };
        }

        // Contact card group mappings (entityType=2 cari grup)
        IReadOnlyDictionary<int, int> contactGroups = new Dictionary<int, int>();
        if (doc.ContactId.HasValue)
        {
            try
            {
                var mappings = await _cardGroupRepo.GetEntityMappingsAsync(2, doc.ContactId.Value.ToString(), ct);
                contactGroups = mappings.ToDictionary(m => m.Level, m => m.CardGroupId);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "CardGroup mappings lookup başarısız (contactId={Cid}).", doc.ContactId);
            }
        }

        return new ApprovalDocumentContext
        {
            DocumentId            = documentId,
            DocumentNumber        = doc.DocumentNumber,
            DocumentKind          = "Document",
            Amount                = doc.GrandTotal,
            DocumentDate          = doc.DocumentDate,
            ContactId             = doc.ContactId,
            ContactCode           = null,
            ContactName           = null,
            ContactGroupByLevel   = contactGroups,
            CreatedByUserId       = doc.CreatedById,
            CreatedByDepartmentId = null,
            Lines                 = Array.Empty<DocumentLineCtx>(),
        };
    }
}
