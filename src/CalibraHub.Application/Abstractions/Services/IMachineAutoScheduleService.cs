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

    /// <summary>Önizleme — hesaplar, PERSIST ETMEZ.</summary>
    Task<AutoSchedulePreviewResultDto> PreviewAsync(
        IReadOnlyList<int> includedWorkOrderIds, DateTime fromUtc, CancellationToken ct);

    /// <summary>Uygula — aynı girdiden yeniden hesaplar ve Status=Planned olarak persist eder.</summary>
    Task<AutoScheduleApplyResultDto> ApplyAsync(
        IReadOnlyList<int> includedWorkOrderIds, DateTime fromUtc, int? userId, CancellationToken ct);
}
