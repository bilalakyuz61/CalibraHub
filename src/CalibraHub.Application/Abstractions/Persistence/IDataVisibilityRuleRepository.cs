using CalibraHub.Domain.Entities;

namespace CalibraHub.Application.Abstractions.Persistence;

/// <summary>
/// 2026-06-12 — Satır görünürlük kuralları persistence. Impl: SqlDataVisibilityRuleRepository.
///
/// Kurallar PER-COMPANY DB'de durur (Contact/CariGroup/WidgetMas ile aynı bağlantı) — bu yüzden
/// repo <c>OpenConnectionAsync</c> kullanır (PermissionGrant'ın aksine, o sistem-DB'dedir).
/// Yükleme metodları kuralı <see cref="DataVisibilityRule.Values"/> + <see cref="DataVisibilityRule.Grants"/>
/// ile birlikte hydrate eder.
/// </summary>
public interface IDataVisibilityRuleRepository
{
    /// <summary>
    /// Bir FormCode için AKTİF kuralları values + grants ile birlikte döner.
    /// Filtre servisinin yasaklı-küme hesabı için ana giriş.
    /// </summary>
    Task<IReadOnlyList<DataVisibilityRule>> ListActiveByFormAsync(string formCode, CancellationToken ct);

    /// <summary>Tüm kurallar (admin UI). includeInactive=false ise yalnızca aktif.</summary>
    Task<IReadOnlyList<DataVisibilityRule>> ListAllAsync(bool includeInactive, CancellationToken ct);

    /// <summary>Tek kural (values + grants hydrate).</summary>
    Task<DataVisibilityRule?> GetByIdAsync(int id, CancellationToken ct);

    /// <summary>
    /// Upsert (Id=0 → INSERT, Id&gt;0 → UPDATE). Values ve Grants tek transaction'da
    /// REPLACE edilir (eski child satırlar silinip rule.Values/rule.Grants yazılır). Yeni Id döner.
    /// </summary>
    Task<int> SaveAsync(DataVisibilityRule rule, CancellationToken ct);

    /// <summary>Kuralı sil — FK CASCADE values + grants'i temizler.</summary>
    Task DeleteAsync(int id, CancellationToken ct);

    /// <summary>Aktif/pasif toggle (in-place refresh için).</summary>
    Task SetActiveAsync(int id, bool isActive, int? updatedById, CancellationToken ct);
}
