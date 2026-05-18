namespace CalibraHub.Application.Abstractions.Services;

/// <summary>
/// Provider dokumanlarindan derlenmis "kullanilabilir" endpoint katalogu.
/// DB'ye kaydedilmis IntegrationEndpoint instance'larindan farkli — bu liste
/// statik referans, sistem genelinde paylasilir, sürüm bagimsiz.
///
/// Su an Netsis NetOpenX REST katalogu (NetsisRestEndpoints.csv — 320 endpoint)
/// destekleniyor. V2: Logo Tiger, Custom REST, kullanici tarafindan import.
///
/// UI tarafinda "URL Sablonu" combobox'i bu service'i kullanir, kullanici
/// secince yeni bir IntegrationEndpoint instance'i yaratilir.
/// </summary>
public interface IEndpointCatalogService
{
    /// <summary>Tum kataloglari (suanda yalnizca Netsis) dondurur.</summary>
    IReadOnlyList<EndpointCatalogItem> GetAll();

    /// <summary>
    /// Provider'a gore filtrele (Netsis / Logo / Custom).
    /// "all" veya bos: hepsi.
    /// </summary>
    IReadOnlyList<EndpointCatalogItem> GetByProvider(string? provider);
}

/// <summary>
/// Bir katalog kaydi — bir provider'in dokumantasyonundan derlenmis tek endpoint.
///
/// Kategori (Sales/Purchase/Customer/Stock/Bank/EDocument/Other) Resource adindan
/// heuristic olarak cikarilir — Sablon Galerisi'yle ortak chip filtreleri icin.
/// </summary>
public sealed record EndpointCatalogItem(
    string Provider,        // "Netsis" | "Logo" | "Custom"
    string Resource,        // "ItemSlips", "ARPs", "Items", "BankAccountTransaction"
    string MethodName,      // "PostInternal", "Describe", "GetInternal", "CustomerRisk"
    string HttpMethod,      // "POST" | "GET" | "PUT" | "DELETE"
    string UrlTemplate,     // "/api/v2/ItemSlips"
    string? InputType,      // "ARPs" (request body type adi)
    string? ReturnType,     // "TResult`1"
    string Category,        // "Sales" | "Purchase" | "Customer" | "Stock" | "Bank" | "EDocument" | "Other"
    string Summary);        // Insan okunabilir kisa aciklama — combobox'ta gosterilir
