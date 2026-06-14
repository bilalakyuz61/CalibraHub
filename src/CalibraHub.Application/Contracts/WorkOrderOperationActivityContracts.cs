using CalibraHub.Domain.Enums;

namespace CalibraHub.Application.Contracts;

/// <summary>
/// Üretim sahası aktivite log satırı — Faz 1 MVP (2026-05-20).
/// ShopFloor terminalinde "Hareket Geçmişi" panelinde gösterilir, OEE raporlamasının
/// (Faz 4) hammaddesi.
/// </summary>
public sealed record WorkOrderOperationActivityDto(
    int Id,
    int WorkOrderOperationId,
    int PersonnelId,
    string? PersonnelName,            // display — repository JOIN ile doldurur
    WorkOrderActivityType ActivityType,
    string ActivityTypeLabel,         // enum Description (örn. "Hazırlık")
    int? ActivityReasonId,
    string? ActivityReasonName,
    DateTime StartedAt,
    DateTime? EndedAt,                // NULL = an aktif
    int? DurationSeconds,             // EndedAt - StartedAt (saniye), aktif ise NULL
    decimal? Quantity,
    decimal? ScrapQuantity,
    string? Notes);

/// <summary>
/// Yeni aktivite başlatma isteği. Aktif aktivite varsa otomatik kapatılır (transition).
/// Quantity/ScrapQuantity yalnız Production tipinde anlamlı; diğer tiplerde service yok sayar.
/// Other tipinde Notes zorunlu (Domain.EnsureValid kontrol eder).
/// </summary>
public sealed record StartActivityRequest(
    int WorkOrderOperationId,
    int PersonnelId,
    WorkOrderActivityType ActivityType,
    int? ActivityReasonId,
    string? Notes);

/// <summary>
/// Aktif aktiviteyi kapatma isteği — başlat/dur akışı dışında manuel kapatma için.
/// Yeni aktivite başlatmadan sadece "an aktif aktiviteyi bitir" senaryosu.
/// </summary>
public sealed record EndActivityRequest(
    int WorkOrderOperationId,
    int PersonnelId,
    string? Notes);
