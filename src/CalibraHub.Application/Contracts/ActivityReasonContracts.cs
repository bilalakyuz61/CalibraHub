using CalibraHub.Domain.Enums;

namespace CalibraHub.Application.Contracts;

/// <summary>
/// Saha aktivite alt sebep DTO'su (2026-05-21 — Faz 2).
/// Admin tanım ekranı + ShopFloor "Durum Değiştir" → sebep dropdown'unda kullanılır.
/// </summary>
public sealed record ActivityReasonDto(
    int Id,
    WorkOrderActivityType ActivityType,
    string ActivityTypeLabel,    // display — enum Description
    string Code,
    string Name,
    string? Description,
    string? ColorHex,
    int SortOrder,
    bool IsActive,
    DateTime Created,
    DateTime? Updated);

public sealed record SaveActivityReasonRequest(
    int? Id,                     // null veya 0 → INSERT, dolu → UPDATE
    WorkOrderActivityType ActivityType,
    string Code,
    string Name,
    string? Description,
    string? ColorHex,
    int SortOrder = 0,
    bool IsActive = true);
