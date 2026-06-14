using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Application.Abstractions.Services;
using CalibraHub.Application.Contracts;
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
    public Guid DocumentId { get; init; }
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
    Task<ApprovalDocumentContext> BuildAsync(Guid documentId, CancellationToken ct);
}

/// <summary>
/// IncomingDocument tabanlı context builder. Onay sistemi şu an gelen e-faturalar
/// üzerinde çalıştığı için belge body'sinden tutar/iletişim bilgisi çekme TODO —
/// MVP'de sadece header (DocumentNumber, IssueDate, SenderTaxNumber) doldurulur.
/// </summary>
public sealed class ApprovalDocumentContextProvider : IApprovalDocumentContextProvider
{
    private readonly IIncomingDocumentRepository _incomingRepo;
    private readonly IUserProfileRepository _userRepo;
    private readonly ICardGroupRepository _cardGroupRepo;
    private readonly IFinanceService? _financeService;
    private readonly ILogisticsConfigurationService? _logistics;
    private readonly ILogger<ApprovalDocumentContextProvider> _logger;

    public ApprovalDocumentContextProvider(
        IIncomingDocumentRepository incomingRepo,
        IUserProfileRepository userRepo,
        ICardGroupRepository cardGroupRepo,
        ILogger<ApprovalDocumentContextProvider> logger,
        IFinanceService? financeService = null,
        ILogisticsConfigurationService? logistics = null)
    {
        _incomingRepo = incomingRepo;
        _userRepo = userRepo;
        _cardGroupRepo = cardGroupRepo;
        _financeService = financeService;
        _logistics = logistics;
        _logger = logger;
    }

    public async Task<ApprovalDocumentContext> BuildAsync(Guid documentId, CancellationToken ct)
    {
        var incoming = await _incomingRepo.GetByIdAsync(documentId, ct);
        if (incoming is null)
        {
            // Belge bulunamadıysa: boş context dön — decision evaluator
            // ham veri olmadan "false" döner, akış güvenli tarafta kalır.
            _logger.LogWarning("ApprovalDocumentContext: IncomingDocument bulunamadı ({DocId}).", documentId);
            return new ApprovalDocumentContext { DocumentId = documentId };
        }

        // Contact (cari) kartı sender VKN üzerinden eşle — varsa.
        int? contactId = null;
        string? contactName = incoming.SenderName;
        if (_financeService is not null && !string.IsNullOrWhiteSpace(incoming.SenderTaxNumber))
        {
            try
            {
                var candidates = await _financeService.GetContactsAsync(null, incoming.SenderTaxNumber, ct);
                var contact = candidates.FirstOrDefault(c =>
                    string.Equals(c.TaxNumber?.Trim(), incoming.SenderTaxNumber.Trim(), StringComparison.OrdinalIgnoreCase));
                if (contact is not null)
                {
                    contactId = contact.Id;
                    contactName = contact.AccountTitle ?? contactName;
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Contact lookup (VKN={Tax}) başarısız.", incoming.SenderTaxNumber);
            }
        }

        // Contact card group mappings (entityType=2 cari grup)
        IReadOnlyDictionary<int, int> contactGroups = new Dictionary<int, int>();
        if (contactId.HasValue)
        {
            try
            {
                var mappings = await _cardGroupRepo.GetEntityMappingsAsync(2, contactId.Value.ToString(), ct);
                contactGroups = mappings.ToDictionary(m => m.Level, m => m.CardGroupId);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "CardGroup mappings lookup başarısız (contactId={Cid}).", contactId);
            }
        }

        // IncomingDocument'ta CreatedByUserId yok — null geçilir.
        // İleride uygulama tarafında document.CreatedByUserId eklendiğinde bağlanır.
        int? createdByUserId = null;
        int? createdByDeptId = null;

        // Lines: IncomingDocument body'si XML olduğundan parse maliyeti yüksek.
        // MVP: line tarafı boş — line-scope decision rule'ları "false" döner.
        // TODO: XML/UBL parse → Lines doldur (e-fatura entegrasyonu Faz 5).
        var lines = Array.Empty<DocumentLineCtx>();

        return new ApprovalDocumentContext
        {
            DocumentId = documentId,
            DocumentNumber = incoming.DocumentNumber,
            DocumentKind = incoming.Kind.ToString(),
            Amount = 0m,  // TODO: UBL XML body parse
            DocumentDate = incoming.IssueDate.ToDateTime(TimeOnly.MinValue),
            ContactId = contactId,
            ContactCode = incoming.SenderTaxNumber,
            ContactName = contactName,
            ContactGroupByLevel = contactGroups,
            CreatedByUserId = createdByUserId,
            CreatedByDepartmentId = createdByDeptId,
            Lines = lines,
        };
    }
}
