namespace CalibraHub.Web.Models.Sales;

/// <summary>
/// "Rezervasyon Deposu Seçimi" (Yükleme Planlama Merkezi parametresi) kaydı.
/// POST /Sales/SaveShipmentLocationConfig — bkz. SalesController.SaveShipmentLocationConfig XML doc'u.
/// Mode: "SPECIFIC" (belirli depolar) | "ALL" (tüm depolar rezervasyona açık).
/// LocationIds: SPECIFIC modda kullanılacak depo Id listesi — sunucu tarafında raf/hücre
/// tipine karşı doğrulanır.
/// </summary>
public sealed record SaveShipmentLocationConfigRequest(
    string?    Mode,
    List<int>? LocationIds);
