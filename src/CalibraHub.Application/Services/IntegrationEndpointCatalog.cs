namespace CalibraHub.Application.Services;

public sealed record IntegrationEndpointEntry(
    string Path,
    string Method,
    string Title,
    string Description,
    string? BodyTemplate);

public static class IntegrationEndpointCatalog
{
    public static readonly IReadOnlyList<IntegrationEndpointEntry> Netsis =
    [
        new("/api/v2/ARPs",            "POST",   "Cari Hesap – Yeni",       "Netsis'te yeni cari hesap (ARP) kaydı oluşturur. CalibraHub'da Cari Hesap eklendiğinde tetiklenir.",            "{\n  \"CariKod\": \"{ContactCode}\",\n  \"CariUnvani1\": \"{ContactName}\",\n  \"VergiNumarasi\": \"{TaxNumber}\"\n}"),
        new("/api/v2/ARPs",            "GET",    "Cari Hesap – Liste/Sorgu", "Netsis'teki cari hesap listesini getirir veya filtreyle sorgular.",                                                              null),
        new("/api/v2/ARPs",            "PUT",    "Cari Hesap – Güncelle",  "Mevcut bir cari hesabın bilgilerini günceller.",                                                                                   "{\n  \"CariKod\": \"{ContactCode}\",\n  \"CariUnvani1\": \"{ContactName}\"\n}"),
        new("/api/v2/Items",           "POST",   "Stok – Yeni",             "Netsis'te yeni stok kartı oluşturur. Malzeme Kartı eklendiğinde tetiklenir.",                                                          "{\n  \"StokKodu\": \"{MaterialCode}\",\n  \"StokAdi\": \"{MaterialName}\",\n  \"GrupKodu\": \"{MaterialTypeName}\"\n}"),
        new("/api/v2/Items",           "GET",    "Stok – Liste/Sorgu",      "Netsis'teki stok listesini getirir veya filtreyle sorgular.",                                                                       null),
        new("/api/v2/Items",           "PUT",    "Stok – Güncelle",        "Mevcut bir stok kartını günceller.",                                                                                                       "{\n  \"StokKodu\": \"{MaterialCode}\",\n  \"StokAdi\": \"{MaterialName}\"\n}"),
        new("/api/v2/SalesOrders",     "POST",   "Satış Siparişi – Yeni",   "Netsis'te yeni satış siparişi oluşturur. Satış Teklifi onaylandığında kullanılabilir.",                                                "{\n  \"BelgeNo\": \"{DocumentNumber}\",\n  \"CariKod\": \"{ContactCode}\",\n  \"BelgeTarihi\": \"{DocumentDate}\"\n}"),
        new("/api/v2/SalesInvoices",   "POST",   "Satış Faturası – Yeni",   "Netsis'te yeni satış faturası oluşturur.",                                                                                          "{\n  \"FaturaNo\": \"{DocumentNumber}\",\n  \"CariKod\": \"{ContactCode}\",\n  \"FaturaTarihi\": \"{DocumentDate}\",\n  \"ToplamTutar\": \"{TotalAmount}\"\n}"),
        new("/api/v2/PurchaseOrders",  "POST",   "Alış Siparişi – Yeni",    "Netsis'te yeni alış (satınalma) siparişi oluşturur.",                                                                                "{\n  \"BelgeNo\": \"{DocumentNumber}\",\n  \"CariKod\": \"{ContactCode}\"\n}"),
        new("/api/v2/PurchaseInvoices","POST",   "Alış Faturası – Yeni",    "Netsis'te yeni alış (satınalma) faturası oluşturur.",                                                                                "{\n  \"FaturaNo\": \"{DocumentNumber}\",\n  \"CariKod\": \"{ContactCode}\",\n  \"ToplamTutar\": \"{TotalAmount}\"\n}"),
        new("/api/v2/StockMovements",  "POST",   "Stok Hareketi – Yeni",    "Netsis'te yeni stok hareketi (giriş/çıkış/transfer) kaydı oluşturur.",                                                       "{\n  \"StokKodu\": \"{MaterialCode}\",\n  \"HareketTipi\": \"G\",\n  \"Miktar\": \"1\"\n}"),
        new("/api/v2/Warehouses",      "GET",    "Depo – Liste",            "Netsis'teki depo listesini getirir.",                                                                                                null),
        new("/api/v2/Banks",           "GET",    "Banka – Liste",           "Netsis'teki banka kartı listesini getirir.",                                                                                       null),
        new("/api/v2/Receipts",        "POST",   "Tahsilat – Yeni",         "Netsis'te yeni tahsilat kaydı oluşturur.",                                                                                       "{\n  \"CariKod\": \"{ContactCode}\",\n  \"Tutar\": \"{TotalAmount}\"\n}"),
    ];
}
