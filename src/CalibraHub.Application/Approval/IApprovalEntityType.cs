namespace CalibraHub.Application.Approval;

/// <summary>
/// Onay akışı (ApprovalFlow) plugin sözleşmesi — her entity tipi (Document,
/// WorkOrder, Item, Contact, ProductionRecord, ...) bu interface'i implement
/// ederek karar koşullarında kullanılacak alan listesini, SQL parametrelerini
/// ve runtime'da entity ID'sinden context build mantığını sağlar.
///
/// Eski document-spesifik <see cref="Services.Approval.ApprovalDocumentContext"/>
/// yapısı yerine generic <see cref="ApprovalEntityContext"/> kullanılır.
/// </summary>
public interface IApprovalEntityType
{
    /// <summary>Code: "Document" / "WorkOrder" / "Item" / "Contact" / "ProductionRecord"</summary>
    string Code { get; }

    /// <summary>UI'da gösterilen kısa etiket: "Belge" / "İş Emri" / "Stok Kartı" vb.</summary>
    string Label { get; }

    /// <summary>lucide-react ikon adı: "FileText" / "Wrench" / "Package" / "Users".</summary>
    string Icon { get; }

    /// <summary>
    /// Designer dropdown'ında optgroup başlığı: "Belgeler" / "Üretim" / "Tanımlar" / "Diğer".
    /// Aynı kategori altındaki entity tipleri tek optgroup içinde gruplanır.
    /// </summary>
    string GroupCategory => "Diğer";

    /// <summary>Karar koşullarında (Decision node) kullanılabilecek alanlar.</summary>
    IReadOnlyList<ApprovalEntityField> GetFields();

    /// <summary>SQL sorgularına otomatik bind edilecek parametreler (Decision node SQL koşulu için).</summary>
    IReadOnlyList<ApprovalEntityParameter> GetSqlParameters();

    /// <summary>
    /// Verilen entity ID'sine göre evaluation context'i kur. Header + line +
    /// SQL parametreleri dictionary'leri ile geri döner. Veri bulunamazsa
    /// boş context dön (decision evaluator eksik veri için "false" sayar).
    /// </summary>
    Task<ApprovalEntityContext> BuildContextAsync(string entityId, CancellationToken ct);
}

/// <summary>Karar koşullarında kullanılan alan tanımı.</summary>
public sealed record ApprovalEntityField
{
    /// <summary>Field code (örn. "amount", "line.itemCode", "workOrderNumber").</summary>
    public string Code { get; init; } = "";

    /// <summary>UI etiketi (örn. "Toplam Tutar").</summary>
    public string Label { get; init; } = "";

    /// <summary>text | numeric | date | lookup | options | sql</summary>
    public string Type { get; init; } = "text";

    /// <summary>header | lineAny | lineAll | lineAgg | sql</summary>
    public string Scope { get; init; } = "header";

    /// <summary>Designer dropdown'ında optgroup başlığı (örn. "Belge Üst Bilgileri").</summary>
    public string GroupLabel { get; init; } = "";

    /// <summary>
    /// lookup tipi için kaynak: "departments" | "users" | "cariGroups[1]" |
    /// "materialGroups[3]" gibi. null → değer seçilebilir liste yok (free text).
    /// </summary>
    public string? LookupSource { get; init; }
}

/// <summary>SQL koşullarına otomatik bind edilecek parametre tanımı.</summary>
public sealed record ApprovalEntityParameter
{
    /// <summary>Parametre adı, SQL'de @{Name} olarak referans edilir (örn. "documentId").</summary>
    public string Name { get; init; } = "";

    /// <summary>int | decimal | string | date | guid</summary>
    public string Type { get; init; } = "int";

    /// <summary>UI ipucu / kısa açıklama.</summary>
    public string Description { get; init; } = "";
}

/// <summary>
/// Decision evaluator + Notification dispatcher tarafından kullanılan generic
/// evaluation context. Eski <see cref="Services.Approval.ApprovalDocumentContext"/>
/// muadili — entity-agnostic, dictionary tabanlı.
/// </summary>
public sealed class ApprovalEntityContext
{
    /// <summary>Entity tipinin code'u (örn. "Document", "WorkOrder").</summary>
    public string EntityTypeCode { get; init; } = "";

    /// <summary>Entity ID — bu code'a göre Guid string'i ya da int string'i olabilir.</summary>
    public string EntityId { get; init; } = "";

    /// <summary>
    /// Header (üst bilgi) alanları — field code → değer. DecisionEvaluator
    /// rule.Field kullanarak buradan değer çeker.
    /// </summary>
    public IReadOnlyDictionary<string, object?> HeaderValues { get; init; }
        = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Line (satır) alanları — her satır için field code → değer. line.itemCode
    /// gibi alanlar burada per-satır okunur. Tek satır yoksa boş liste.
    /// </summary>
    public IReadOnlyList<IReadOnlyDictionary<string, object?>> LineValues { get; init; }
        = Array.Empty<IReadOnlyDictionary<string, object?>>();

    /// <summary>
    /// SQL koşullarına bind edilecek parametreler — name → value. Entity type'ın
    /// <see cref="IApprovalEntityType.GetSqlParameters"/> listesinde tanımlanan
    /// her parametre buraya doldurulur.
    /// </summary>
    public IReadOnlyDictionary<string, object?> SqlParameters { get; init; }
        = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// SetVariable node'ların yazdığı, Decision node'ların okuduğu runtime değişkenleri.
    /// Aynı traversal zinciri içinde geçerlidir; akışlar arası paylaşılmaz.
    /// </summary>
    public Dictionary<string, object?> FlowVariables { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Notification dispatcher'ın "approver" recipient modunu çözümleyebilmesi için
    /// executor tarafından set edilen approval instance ID.
    /// </summary>
    public int? ApprovalInstanceId { get; set; }

    /// <summary>Akışı başlatan kullanıcının adı — {requesterName} token'ı için.</summary>
    public string? RequesterName { get; set; }

    /// <summary>Çalışan akışın adı — {flowName} token'ı için.</summary>
    public string? FlowName { get; set; }

    /// <summary>
    /// Hızlı onay/red link'lerinde kullanılacak dış erişim base URL'i.
    /// Task.Run fire-and-forget'te HTTP context kaybolduğundan, executor bu değeri
    /// HTTP context canlıyken (ApprovalFlowService) bir kez yakalar ve burada saklar.
    /// </summary>
    public string? BaseUrl { get; set; }
}

/// <summary>
/// DI ile inject edilen tüm <see cref="IApprovalEntityType"/> implementasyonlarını
/// code → instance map'iyle expose eder.
/// </summary>
public interface IApprovalEntityTypeRegistry
{
    /// <summary>Tüm kayıtlı entity tipleri.</summary>
    IReadOnlyList<IApprovalEntityType> All { get; }

    /// <summary>Code ile entity type lookup. Bulunamazsa null.</summary>
    IApprovalEntityType? Get(string code);
}
