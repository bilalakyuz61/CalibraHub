namespace CalibraHub.Application.Approval.EntityTypes;

/// <summary>
/// Stok Kartı (Item) onay akışı entity tipi — STUB.
/// Gerçek BuildContextAsync gelecek sprint'te <c>ILogisticsConfigurationService.GetItemByIdAsync</c>
/// (veya muadili) ile item kaydını çekecek.
/// </summary>
public sealed class ItemApprovalEntityType : IApprovalEntityType
{
    public string Code          => "Item";
    public string Label         => "Stok Kartı";
    public string Icon          => "Package";
    public string GroupCategory => "Tanımlar";

    public IReadOnlyList<ApprovalEntityField> GetFields() => _fields;

    public IReadOnlyList<ApprovalEntityParameter> GetSqlParameters() => _sqlParams;

    public Task<ApprovalEntityContext> BuildContextAsync(string entityId, CancellationToken ct)
    {
        // TODO (Faz 5): ILogisticsConfigurationService.GetItemByIdAsync(itemId) ile gerçek context.
        //   - HeaderValues: itemCode, itemName, unitPrice, taxRate, isActive,
        //                   materialGroup1..5
        //   - SqlParameters: itemId, itemCode
        int.TryParse(entityId, out var itemId);
        var sqlParams = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["itemId"]   = itemId == 0 ? (object?)DBNull.Value : itemId,
            ["itemCode"] = DBNull.Value,
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
        new ApprovalEntityField { Code="itemCode",       Label="Stok Kodu",     Type="text",    Scope="header", GroupLabel="Stok Kartı Bilgileri" },
        new ApprovalEntityField { Code="itemName",       Label="Stok Adı",      Type="text",    Scope="header", GroupLabel="Stok Kartı Bilgileri" },
        new ApprovalEntityField { Code="unitPrice",      Label="Birim Fiyat",   Type="numeric", Scope="header", GroupLabel="Stok Kartı Bilgileri" },
        new ApprovalEntityField { Code="taxRate",        Label="KDV Oranı",     Type="numeric", Scope="header", GroupLabel="Stok Kartı Bilgileri" },
        new ApprovalEntityField { Code="isActive",       Label="Aktif",         Type="text",    Scope="header", GroupLabel="Stok Kartı Bilgileri" },
        new ApprovalEntityField { Code="materialGroup1", Label="Stok Grubu 1",  Type="lookup",  Scope="header", GroupLabel="Stok Kartı Bilgileri", LookupSource="materialGroups[1]" },
        new ApprovalEntityField { Code="materialGroup2", Label="Stok Grubu 2",  Type="lookup",  Scope="header", GroupLabel="Stok Kartı Bilgileri", LookupSource="materialGroups[2]" },
        new ApprovalEntityField { Code="materialGroup3", Label="Stok Grubu 3",  Type="lookup",  Scope="header", GroupLabel="Stok Kartı Bilgileri", LookupSource="materialGroups[3]" },
        new ApprovalEntityField { Code="materialGroup4", Label="Stok Grubu 4",  Type="lookup",  Scope="header", GroupLabel="Stok Kartı Bilgileri", LookupSource="materialGroups[4]" },
        new ApprovalEntityField { Code="materialGroup5", Label="Stok Grubu 5",  Type="lookup",  Scope="header", GroupLabel="Stok Kartı Bilgileri", LookupSource="materialGroups[5]" },
        new ApprovalEntityField { Code="sql.queryResult",Label="SQL Sorgu Sonucu", Type="sql",  Scope="sql",    GroupLabel="SQL Tabanlı Koşul" },
    };

    private static readonly IReadOnlyList<ApprovalEntityParameter> _sqlParams = new[]
    {
        new ApprovalEntityParameter { Name="itemId",   Type="int",    Description="Stok kartı ID" },
        new ApprovalEntityParameter { Name="itemCode", Type="string", Description="Stok kodu" },
    };
}
