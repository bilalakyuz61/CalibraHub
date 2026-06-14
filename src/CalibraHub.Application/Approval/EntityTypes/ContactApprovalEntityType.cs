namespace CalibraHub.Application.Approval.EntityTypes;

/// <summary>
/// Cari Hesap (Contact) onay akışı entity tipi — STUB.
/// Gerçek BuildContextAsync gelecek sprint'te <c>IFinanceService.GetContactByIdAsync</c>
/// + cari grup mapping ile besleyecek.
/// </summary>
public sealed class ContactApprovalEntityType : IApprovalEntityType
{
    public string Code          => "Contact";
    public string Label         => "Cari Hesap";
    public string Icon          => "Users";
    public string GroupCategory => "Tanımlar";

    public IReadOnlyList<ApprovalEntityField> GetFields() => _fields;

    public IReadOnlyList<ApprovalEntityParameter> GetSqlParameters() => _sqlParams;

    public Task<ApprovalEntityContext> BuildContextAsync(string entityId, CancellationToken ct)
    {
        // TODO (Faz 5): IFinanceService + ICardGroupRepository ile gerçek context.
        //   - HeaderValues: contactCode, contactName, contactType, contactGroup1..5
        //   - SqlParameters: contactId
        int.TryParse(entityId, out var contactId);
        var sqlParams = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["contactId"] = contactId == 0 ? (object?)DBNull.Value : contactId,
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
        new ApprovalEntityField { Code="contactCode",   Label="Cari Kodu",      Type="text",   Scope="header", GroupLabel="Cari Bilgileri" },
        new ApprovalEntityField { Code="contactName",   Label="Cari Adı",       Type="text",   Scope="header", GroupLabel="Cari Bilgileri" },
        new ApprovalEntityField { Code="contactType",   Label="Cari Tipi",      Type="lookup", Scope="header", GroupLabel="Cari Bilgileri" /* TODO: lookupSource="contactTypes" */ },
        new ApprovalEntityField { Code="contactGroup1", Label="Cari Grubu 1",   Type="lookup", Scope="header", GroupLabel="Cari Bilgileri", LookupSource="cariGroups[1]" },
        new ApprovalEntityField { Code="contactGroup2", Label="Cari Grubu 2",   Type="lookup", Scope="header", GroupLabel="Cari Bilgileri", LookupSource="cariGroups[2]" },
        new ApprovalEntityField { Code="contactGroup3", Label="Cari Grubu 3",   Type="lookup", Scope="header", GroupLabel="Cari Bilgileri", LookupSource="cariGroups[3]" },
        new ApprovalEntityField { Code="contactGroup4", Label="Cari Grubu 4",   Type="lookup", Scope="header", GroupLabel="Cari Bilgileri", LookupSource="cariGroups[4]" },
        new ApprovalEntityField { Code="contactGroup5", Label="Cari Grubu 5",   Type="lookup", Scope="header", GroupLabel="Cari Bilgileri", LookupSource="cariGroups[5]" },
        new ApprovalEntityField { Code="sql.queryResult", Label="SQL Sorgu Sonucu", Type="sql", Scope="sql",   GroupLabel="SQL Tabanlı Koşul" },
    };

    private static readonly IReadOnlyList<ApprovalEntityParameter> _sqlParams = new[]
    {
        new ApprovalEntityParameter { Name="contactId", Type="int", Description="Cari hesap ID" },
    };
}
