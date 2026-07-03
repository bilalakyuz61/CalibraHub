namespace CalibraHub.Application.Contracts;

/// <summary>
/// İş emri bileşen DTO — Faz 2 BOM patlatma çıktısı + display alanları.
/// Items + ItemConfiguration JOIN ile zenginleştirilir (frontend kart için).
/// </summary>
public sealed record WorkOrderComponentDto(
    int Id,
    int WorkOrderId,
    int ItemId,
    string? ItemCode,
    string? ItemName,
    int? ConfigId,
    string? ConfigCode,
    decimal RequiredQuantity,
    decimal IssuedQuantity,
    decimal ScrapRate,
    int? UnitId,
    string? UnitCode,
    string? Notes,
    DateTime Created,
    DateTime? Updated);

/// <summary>
/// Patlatma sonucu özeti — Frontend toast/log için.
/// </summary>
public sealed record ExplodeBomResultDto(
    int WorkOrderId,
    int BomId,
    int ComponentCount,
    decimal Multiplier);

/// <summary>
/// Malzeme Sarf Et — ShopFloor'da manuel bileşen çıkışı (2026-07-02). Otomatik BOM oranından
/// TÜRETİLMEZ — operatör gerçek sarfı girer (fire/ıskarta/ikame sapması BOM oranını her zaman
/// bozar). WorkOrderComponent.IssuedQuantity += Quantity + DocumentLine'a Issue satırı aynı
/// transaction'da atomik yazılır.
/// </summary>
public sealed record IssueWorkOrderComponentRequest(
    int WorkOrderComponentId,
    decimal Quantity,
    int OperatorPersonnelId);
