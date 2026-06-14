using CalibraHub.Application.Services.Approval;
using Microsoft.Extensions.DependencyInjection;

namespace CalibraHub.Application.Approval.EntityTypes;

/// <summary>
/// 9 belge entity tipinin (1 wildcard "Tüm Belgeler" + 8 spesifik belge türü) tek noktadan
/// tanımı + DI registration helper. Her tip <see cref="GenericDocumentApprovalEntityType"/>
/// instance'ı olarak kayıt edilir; ortak field/parameter/context mantığı
/// <see cref="DocumentApprovalEntityType"/> içinde kalır (kod tekrarı yok).
/// </summary>
public static class DocumentEntityTypes
{
    /// <summary>
    /// (Code, Label, Icon, DocumentTypeFilter) tuple listesi.
    /// DocumentTypeFilter = DocumentService.GetByTypeAsync için belge tipi kodu (ileride match'te kullanılacak).
    /// </summary>
    public static readonly IReadOnlyList<(string Code, string Label, string Icon, string? Filter)> Definitions =
        new (string, string, string, string?)[]
        {
            // (Document = wildcard tutmaya devam, "Tüm Belgeler" etiketi)
            ("EInvoice",         "e-Fatura",              "Receipt",       null),
            ("EArchive",         "e-Arşiv",               "Archive",       null),
            ("EDispatch",        "e-İrsaliye",            "Truck",         null),
            ("SalesQuote",       "Satış Teklifi",         "FileText",      "satis_teklifi"),
            ("SalesOrder",       "Satış Siparişi",        "ShoppingBag",   "satis_siparisi"),
            ("PurchaseRequest",  "İhtiyaç Kaydı",         "FileSearch",    "alis_talebi"),
            ("PurchaseQuote",    "Satın Alma Teklifi",    "FileText",      "alis_teklifi"),
            ("PurchaseOrder",    "Satın Alma Siparişi",   "ShoppingCart",  "alis_siparisi"),
        };

    /// <summary>
    /// DI'a wildcard "Document" (DocumentApprovalEntityType) ve 8 spesifik belge tipi
    /// (GenericDocumentApprovalEntityType) kayıt eder. Hepsi Scoped — IApprovalDocumentContextProvider
    /// Scoped olduğu için Singleton register etmek mümkün değil.
    /// </summary>
    public static IServiceCollection AddDocumentEntityTypes(this IServiceCollection services)
    {
        // Wildcard ("Tüm Belgeler") — backward-compat: eski DB değerleri de buraya düşer.
        services.AddScoped<IApprovalEntityType, DocumentApprovalEntityType>();

        // Spesifik belge türleri — paylaşılan field/parameter setiyle.
        foreach (var (code, label, icon, filter) in Definitions)
        {
            services.AddScoped<IApprovalEntityType>(sp =>
                new GenericDocumentApprovalEntityType(
                    sp.GetRequiredService<IApprovalDocumentContextProvider>(),
                    code, label, icon, filter));
        }
        return services;
    }
}
