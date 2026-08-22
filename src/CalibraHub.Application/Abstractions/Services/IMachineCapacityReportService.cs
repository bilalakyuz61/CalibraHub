using CalibraHub.Application.Contracts;

namespace CalibraHub.Application.Abstractions.Services;

/// <summary>
/// Makine Planlama — Kapasite / Yük Raporu (backend, 2026-08-22). Bkz. <c>CapacityLoadContracts.cs</c>
/// XML doc'u (kilitli sözleşme + hesaplama özeti) ve <c>MachineCapacityReportService</c> implementasyon
/// doc'u (TZ/gün-hafta kova yaklaşımı, Faz 3 motoruyla aynı desen).
/// </summary>
public interface IMachineCapacityReportService
{
    /// <summary><paramref name="fromUtc"/>/<paramref name="toUtc"/> gerçek UTC an'lardır (controller
    /// tarafında <c>DateTime.ToUniversalTime()</c> ile normalize edilmiş olmalı — MVC query binder'ın
    /// "Z" ekli ISO'yu Kind=Local etiketiyle döndürme tuzağı için bkz. MachineScheduleData deseni).
    /// Servis bu an'ları yerel takvime çevirip gün/hafta kova sınırlarını kendisi türetir.
    /// <paramref name="bucket"/> yalnız "day" veya "week" kabul eder; başka bir değer
    /// <see cref="ArgumentException"/> fırlatır.</summary>
    Task<CapacityLoadResultDto> GetCapacityLoadAsync(DateTime fromUtc, DateTime toUtc, string bucket, CancellationToken ct);
}
