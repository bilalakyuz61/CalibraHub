using CalibraHub.Application.Contracts;

namespace CalibraHub.Application.Abstractions.Services;

/// <summary>
/// Makine Planlama Faz 3 (2026-08-05) — forward, sonlu-kapasite otomatik çizelgeleme motoru.
/// Preview ve Apply AYNI hesaplamayı yapar (determinizm: <c>fromUtc</c> anchor, DateTime.Now/Random
/// kullanılmaz) — Apply, client'ın gönderdiği blok koordinatlarına GÜVENMEZ, kendi yeniden hesaplar.
/// Bkz. <c>MachineAutoScheduleContracts.cs</c> XML doc'ları.
/// </summary>
public interface IMachineAutoScheduleService
{
    /// <summary>Aday iş emri listesi (kullanıcı bu listeden hariç tutacaklarını çıkarır).</summary>
    Task<IReadOnlyList<AutoScheduleCandidateWorkOrderDto>> GetCandidatesAsync(CancellationToken ct);

    /// <summary>Önizleme — hesaplar, PERSIST ETMEZ. <paramref name="scenarioId"/> null ise
    /// varsayılan Vardiya Senaryosu kullanılır (bkz. <c>IMachineCalendarRepository.ListWorkWindowsAsync</c>).</summary>
    Task<AutoSchedulePreviewResultDto> PreviewAsync(
        IReadOnlyList<int> includedWorkOrderIds, DateTime fromUtc, int? scenarioId, CancellationToken ct);

    /// <summary>Uygula — aynı girdiden yeniden hesaplar ve Status=Planned olarak persist eder.</summary>
    Task<AutoScheduleApplyResultDto> ApplyAsync(
        IReadOnlyList<int> includedWorkOrderIds, DateTime fromUtc, int? scenarioId, int? userId, CancellationToken ct);

    /// <summary>Blok Kilitle + Yeniden Çizelgele (2026-08-22) — Önizleme, PERSIST ETMEZ. Kapsam TÜM
    /// açık iş emirleri (opt-out listesi yok); Kilitli(2)/Onaylı(3) bloğu OLMAYAN operasyonlar
    /// (planlanmamış + yalnız-Planlı) yeniden yerleştirilir, Kilitli/Onaylı bloklar SABİT kapasite
    /// kısıtıdır. Request'te scenarioId yok — motor varsayılan Vardiya Senaryosu'nu kullanır.</summary>
    Task<ReschedulePreviewResultDto> ReschedulePreviewAsync(DateTime fromUtc, CancellationToken ct);

    /// <summary>Uygula — Preview ile AYNI girdiden (fromUtc) yeniden hesaplar (client koordinatına
    /// güvenmez); TEK transaction'da serbest set'in aktif Planlı bloklarını soft-delete edip yeni
    /// önerileri yazar (bkz. <c>IMachineAutoScheduleRepository.ApplyRescheduleAsync</c>).</summary>
    Task<RescheduleApplyResultDto> RescheduleApplyAsync(DateTime fromUtc, int? userId, CancellationToken ct);
}
