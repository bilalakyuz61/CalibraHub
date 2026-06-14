using CalibraHub.Application.Services.Approval;

namespace CalibraHub.Application.Approval.EntityTypes;

/// <summary>
/// Klasik belge tabanlı (Sales/Purchase/eFatura/eArşiv/eİrsaliye/PurchaseRequest/PurchaseQuote/SalesQuote)
/// onay akışları için "Tüm Belgeler" wildcard entity tipi. Spesifik belge türleri için
/// <see cref="GenericDocumentApprovalEntityType"/> kullanılır — bu tip backward-compat
/// için kalır (eski "Document" enum değeri ile kayıtlı flow'lar yine eşleşir).
///
/// BuildContextAsync mevcut <see cref="IApprovalDocumentContextProvider"/> mantığını WRAP eder.
/// CommonFields/CommonParameters/MapToEntityContext public static — tüm spesifik belge
/// entity tipleri bu paylaşılan alan setini kullanır (kod tekrarı yok).
/// </summary>
public sealed class DocumentApprovalEntityType : IApprovalEntityType
{
    private readonly IApprovalDocumentContextProvider _docProvider;

    public DocumentApprovalEntityType(IApprovalDocumentContextProvider docProvider)
    {
        _docProvider = docProvider;
    }

    public string Code          => "Document";
    public string Label         => "Tüm Belgeler";
    public string Icon          => "FileText";
    public string GroupCategory => "Belgeler";

    public IReadOnlyList<ApprovalEntityField> GetFields() => CommonFields;

    public IReadOnlyList<ApprovalEntityParameter> GetSqlParameters() => CommonParameters;

    public async Task<ApprovalEntityContext> BuildContextAsync(string entityId, CancellationToken ct)
    {
        if (!Guid.TryParse(entityId, out var documentId))
        {
            return new ApprovalEntityContext { EntityTypeCode = Code, EntityId = entityId ?? "" };
        }

        var doc = await _docProvider.BuildAsync(documentId, ct);
        return MapToEntityContext(doc, Code);
    }

    /// <summary>
    /// Legacy <see cref="ApprovalDocumentContext"/> → generic <see cref="ApprovalEntityContext"/>.
    /// Eski field code'ları (amount, taxNo, contactName, line.itemCode, contactGroup1...)
    /// header/line dictionary'lerine yerleştirilir. Public static — spesifik belge entity
    /// tipleri (GenericDocumentApprovalEntityType) bu helper'ı paylaşır.
    /// </summary>
    public static ApprovalEntityContext MapToEntityContext(ApprovalDocumentContext doc, string entityTypeCode = "Document")
    {
        var header = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["amount"]            = doc.Amount,
            ["documentDate"]      = doc.DocumentDate,
            ["taxNo"]             = doc.ContactCode,
            ["contactName"]       = doc.ContactName,
            ["user.userId"]       = doc.CreatedByUserId?.ToString(),
            ["user.departmentId"] = doc.CreatedByDepartmentId,
            // Aggregate alanları
            ["lineCount"]         = (decimal)doc.Lines.Count,
            ["lineMaxTotal"]      = doc.Lines.Count > 0 ? doc.Lines.Max(l => l.LineTotal) : 0m,
            ["lineSumQty"]        = doc.Lines.Sum(l => l.Quantity),
        };
        // Cari grupları (1-5)
        foreach (var (level, id) in doc.ContactGroupByLevel)
        {
            header[$"contactGroup{level}"] = id;
        }

        var lines = doc.Lines.Select(l =>
        {
            var dict = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["line.itemCode"]  = l.ItemCode,
                ["line.itemName"]  = l.ItemName,
                ["line.quantity"]  = l.Quantity,
                ["line.unitPrice"] = l.UnitPrice,
                ["line.lineTotal"] = l.LineTotal,
            };
            foreach (var (level, code) in l.MaterialGroupByLevel)
            {
                dict[$"line.materialGroup{level}"] = code;
            }
            return (IReadOnlyDictionary<string, object?>)dict;
        }).ToList();

        var sqlParams = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["documentId"]   = doc.DocumentId,
            ["contactId"]    = (object?)doc.ContactId ?? DBNull.Value,
            ["amount"]       = doc.Amount,
            ["userId"]       = (object?)doc.CreatedByUserId ?? DBNull.Value,
            ["departmentId"] = (object?)doc.CreatedByDepartmentId ?? DBNull.Value,
        };

        return new ApprovalEntityContext
        {
            EntityTypeCode = entityTypeCode,
            EntityId       = doc.DocumentId.ToString(),
            HeaderValues   = header,
            LineValues     = lines,
            SqlParameters  = sqlParams,
        };
    }

    // ── Field listesi (mevcut hardcoded DECISION_FIELDS karşılığı) ────────────
    // Public — GenericDocumentApprovalEntityType bu paylaşılan listeyi kullanır.
    public static readonly IReadOnlyList<ApprovalEntityField> CommonFields = new[]
    {
        // Kullanıcı bilgileri
        new ApprovalEntityField { Code="user.departmentId", Label="Departman",           Type="lookup",  Scope="header", GroupLabel="Kullanıcı Bilgileri", LookupSource="departments" },
        new ApprovalEntityField { Code="user.userId",       Label="Belgeyi Oluşturan",   Type="lookup",  Scope="header", GroupLabel="Kullanıcı Bilgileri", LookupSource="users"       },
        // Belge üst bilgileri
        new ApprovalEntityField { Code="amount",          Label="Toplam Tutar",          Type="numeric", Scope="header", GroupLabel="Belge Üst Bilgileri" },
        new ApprovalEntityField { Code="documentDate",    Label="Belge Tarihi",          Type="date",    Scope="header", GroupLabel="Belge Üst Bilgileri" },
        new ApprovalEntityField { Code="taxNo",           Label="Tedarikçi/Müşteri VKN", Type="text",    Scope="header", GroupLabel="Belge Üst Bilgileri" },
        new ApprovalEntityField { Code="contactName",     Label="Tedarikçi/Müşteri Adı", Type="text",    Scope="header", GroupLabel="Belge Üst Bilgileri" },
        new ApprovalEntityField { Code="contactGroup1",   Label="Cari Grubu 1", Type="lookup", Scope="header", GroupLabel="Belge Üst Bilgileri", LookupSource="cariGroups[1]" },
        new ApprovalEntityField { Code="contactGroup2",   Label="Cari Grubu 2", Type="lookup", Scope="header", GroupLabel="Belge Üst Bilgileri", LookupSource="cariGroups[2]" },
        new ApprovalEntityField { Code="contactGroup3",   Label="Cari Grubu 3", Type="lookup", Scope="header", GroupLabel="Belge Üst Bilgileri", LookupSource="cariGroups[3]" },
        new ApprovalEntityField { Code="contactGroup4",   Label="Cari Grubu 4", Type="lookup", Scope="header", GroupLabel="Belge Üst Bilgileri", LookupSource="cariGroups[4]" },
        new ApprovalEntityField { Code="contactGroup5",   Label="Cari Grubu 5", Type="lookup", Scope="header", GroupLabel="Belge Üst Bilgileri", LookupSource="cariGroups[5]" },
        // Belge kalem bilgileri
        new ApprovalEntityField { Code="line.itemCode",      Label="Stok Kodu",    Type="text",    Scope="lineAny", GroupLabel="Belge Kalem Bilgileri" },
        new ApprovalEntityField { Code="line.itemName",      Label="Stok Adı",     Type="text",    Scope="lineAny", GroupLabel="Belge Kalem Bilgileri" },
        new ApprovalEntityField { Code="line.quantity",      Label="Miktar",       Type="numeric", Scope="lineAny", GroupLabel="Belge Kalem Bilgileri" },
        new ApprovalEntityField { Code="line.unitPrice",     Label="Birim Fiyat",  Type="numeric", Scope="lineAny", GroupLabel="Belge Kalem Bilgileri" },
        new ApprovalEntityField { Code="line.lineTotal",     Label="Satır Tutarı", Type="numeric", Scope="lineAny", GroupLabel="Belge Kalem Bilgileri" },
        new ApprovalEntityField { Code="line.materialGroup1",Label="Stok Grubu 1", Type="lookup",  Scope="lineAny", GroupLabel="Belge Kalem Bilgileri", LookupSource="materialGroups[1]" },
        new ApprovalEntityField { Code="line.materialGroup2",Label="Stok Grubu 2", Type="lookup",  Scope="lineAny", GroupLabel="Belge Kalem Bilgileri", LookupSource="materialGroups[2]" },
        new ApprovalEntityField { Code="line.materialGroup3",Label="Stok Grubu 3", Type="lookup",  Scope="lineAny", GroupLabel="Belge Kalem Bilgileri", LookupSource="materialGroups[3]" },
        new ApprovalEntityField { Code="line.materialGroup4",Label="Stok Grubu 4", Type="lookup",  Scope="lineAny", GroupLabel="Belge Kalem Bilgileri", LookupSource="materialGroups[4]" },
        new ApprovalEntityField { Code="line.materialGroup5",Label="Stok Grubu 5", Type="lookup",  Scope="lineAny", GroupLabel="Belge Kalem Bilgileri", LookupSource="materialGroups[5]" },
        new ApprovalEntityField { Code="lineCount",       Label="Kalem Sayısı (toplam)", Type="numeric", Scope="lineAgg", GroupLabel="Belge Kalem Bilgileri" },
        new ApprovalEntityField { Code="lineMaxTotal",    Label="En Büyük Satır Tutarı", Type="numeric", Scope="lineAgg", GroupLabel="Belge Kalem Bilgileri" },
        new ApprovalEntityField { Code="lineSumQty",      Label="Toplam Miktar",         Type="numeric", Scope="lineAgg", GroupLabel="Belge Kalem Bilgileri" },
        // SQL
        new ApprovalEntityField { Code="sql.queryResult", Label="SQL Sorgu Sonucu",      Type="sql",     Scope="sql",     GroupLabel="SQL Tabanlı Koşul" },
    };

    // Public — GenericDocumentApprovalEntityType bu paylaşılan listeyi kullanır.
    public static readonly IReadOnlyList<ApprovalEntityParameter> CommonParameters = new[]
    {
        new ApprovalEntityParameter { Name="documentId",   Type="guid",    Description="Belge GUID (IncomingDocument.Id)" },
        new ApprovalEntityParameter { Name="contactId",    Type="int",     Description="Eşleşen cari ID (varsa)"          },
        new ApprovalEntityParameter { Name="amount",       Type="decimal", Description="Belge toplam tutar"               },
        new ApprovalEntityParameter { Name="userId",       Type="guid",    Description="Belgeyi oluşturan kullanıcı ID"   },
        new ApprovalEntityParameter { Name="departmentId", Type="int",     Description="Oluşturucunun departman ID'si"    },
    };
}
