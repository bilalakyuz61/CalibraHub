namespace CalibraHub.Application.Approval.EntityTypes;

/// <summary>
/// Üretim Sonu Kaydı (ProductionRecord) onay akışı entity tipi — STUB.
/// Gerçek BuildContextAsync gelecek sprint'te <c>IWorkOrderOperationActivityRepository</c>
/// (veya ProductionRecord repo) ile besleyecek.
/// </summary>
public sealed class ProductionRecordApprovalEntityType : IApprovalEntityType
{
    public string Code          => "ProductionRecord";
    public string Label         => "Üretim Sonu Kaydı";
    public string Icon          => "ClipboardCheck";
    public string GroupCategory => "Üretim";

    public IReadOnlyList<ApprovalEntityField> GetFields() => _fields;

    public IReadOnlyList<ApprovalEntityParameter> GetSqlParameters() => _sqlParams;

    public Task<ApprovalEntityContext> BuildContextAsync(string entityId, CancellationToken ct)
    {
        // TODO (Faz 5): IWorkOrderOperationActivityRepository.GetByIdAsync ile gerçek context.
        //   - HeaderValues: workOrderNumber, completedQuantity, scrapQuantity,
        //                   completedBy, shiftId
        //   - SqlParameters: productionRecordId
        int.TryParse(entityId, out var prodId);
        var sqlParams = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["productionRecordId"] = prodId == 0 ? (object?)DBNull.Value : prodId,
        };
        return Task.FromResult(new ApprovalEntityContext
        {
            EntityTypeCode = Code,
            EntityId       = entityId ?? "",
            SqlParameters  = sqlParams,
        });
    }

    private static readonly IReadOnlyList<ApprovalEntityField> _fields = new[]
    {
        new ApprovalEntityField { Code="workOrderNumber",   Label="İş Emri No",     Type="text",    Scope="header", GroupLabel="Üretim Bilgileri" },
        new ApprovalEntityField { Code="completedQuantity", Label="Üretilen Miktar",Type="numeric", Scope="header", GroupLabel="Üretim Bilgileri" },
        new ApprovalEntityField { Code="scrapQuantity",     Label="Fire Miktar",    Type="numeric", Scope="header", GroupLabel="Üretim Bilgileri" },
        new ApprovalEntityField { Code="completedBy",       Label="Üreten",         Type="lookup",  Scope="header", GroupLabel="Üretim Bilgileri", LookupSource="users" },
        new ApprovalEntityField { Code="shiftId",           Label="Vardiya",        Type="lookup",  Scope="header", GroupLabel="Üretim Bilgileri" /* TODO: lookupSource="shifts" */ },
        new ApprovalEntityField { Code="sql.queryResult",   Label="SQL Sorgu Sonucu", Type="sql",   Scope="sql",    GroupLabel="SQL Tabanlı Koşul" },
    };

    private static readonly IReadOnlyList<ApprovalEntityParameter> _sqlParams = new[]
    {
        new ApprovalEntityParameter { Name="productionRecordId", Type="int", Description="Üretim kaydı ID" },
    };
}
