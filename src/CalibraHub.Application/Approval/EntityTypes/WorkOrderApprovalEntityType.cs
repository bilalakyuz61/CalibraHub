namespace CalibraHub.Application.Approval.EntityTypes;

/// <summary>
/// İş Emri (WorkOrder) onay akışı entity tipi — STUB.
/// Field listesi placeholder; gerçek BuildContextAsync gelecek sprint'te
/// <c>IWorkOrderRepository.GetByIdAsync</c> ile besleyecek.
/// </summary>
public sealed class WorkOrderApprovalEntityType : IApprovalEntityType
{
    public string Code          => "WorkOrder";
    public string Label         => "İş Emri";
    public string Icon          => "Wrench";
    public string GroupCategory => "Üretim";

    public IReadOnlyList<ApprovalEntityField> GetFields() => _fields;

    public IReadOnlyList<ApprovalEntityParameter> GetSqlParameters() => _sqlParams;

    public Task<ApprovalEntityContext> BuildContextAsync(string entityId, CancellationToken ct)
    {
        // TODO (Faz 5): IWorkOrderRepository.GetByIdAsync(workOrderId) ile gerçek context kur.
        //   - HeaderValues: workOrderNumber, productCode, quantity, routingId, priority,
        //                   plannedStartDate, responsibleUserId, machineId
        //   - SqlParameters: workOrderId, productCode, quantity, responsibleUserId
        //   - LineValues: rota operasyonları satır olarak çekilebilir
        int.TryParse(entityId, out var woId);
        var sqlParams = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["workOrderId"]        = woId == 0 ? (object?)DBNull.Value : woId,
            ["productCode"]        = DBNull.Value,
            ["quantity"]           = DBNull.Value,
            ["responsibleUserId"]  = DBNull.Value,
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
        new ApprovalEntityField { Code="workOrderNumber",   Label="İş Emri No",          Type="text",    Scope="header", GroupLabel="İş Emri Bilgileri" },
        new ApprovalEntityField { Code="productCode",       Label="Mamul Kodu",          Type="text",    Scope="header", GroupLabel="İş Emri Bilgileri" },
        new ApprovalEntityField { Code="quantity",          Label="Miktar",              Type="numeric", Scope="header", GroupLabel="İş Emri Bilgileri" },
        new ApprovalEntityField { Code="routingId",         Label="Rota",                Type="lookup",  Scope="header", GroupLabel="İş Emri Bilgileri" /* TODO: lookupSource="routings" */ },
        new ApprovalEntityField { Code="priority",          Label="Öncelik",             Type="numeric", Scope="header", GroupLabel="İş Emri Bilgileri" },
        new ApprovalEntityField { Code="plannedStartDate",  Label="Planlanan Başlangıç", Type="date",    Scope="header", GroupLabel="İş Emri Bilgileri" },
        new ApprovalEntityField { Code="responsibleUserId", Label="Sorumlu",             Type="lookup",  Scope="header", GroupLabel="İş Emri Bilgileri", LookupSource="users" },
        new ApprovalEntityField { Code="machineId",         Label="Makine",              Type="lookup",  Scope="header", GroupLabel="İş Emri Bilgileri" /* TODO: lookupSource="machines" */ },
        new ApprovalEntityField { Code="sql.queryResult",   Label="SQL Sorgu Sonucu",    Type="sql",     Scope="sql",    GroupLabel="SQL Tabanlı Koşul" },
    };

    private static readonly IReadOnlyList<ApprovalEntityParameter> _sqlParams = new[]
    {
        new ApprovalEntityParameter { Name="workOrderId",       Type="int",     Description="İş emri ID" },
        new ApprovalEntityParameter { Name="productCode",       Type="string",  Description="Üretilen mamul kodu" },
        new ApprovalEntityParameter { Name="quantity",          Type="decimal", Description="Üretim miktarı" },
        new ApprovalEntityParameter { Name="responsibleUserId", Type="guid",    Description="Sorumlu kullanıcı ID" },
    };
}
