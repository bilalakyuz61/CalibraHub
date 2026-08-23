namespace CalibraHub.Application.Diagnostics;

/// <summary>
/// Merkezi hata log ekranı arama isteği. <paramref name="FromUtc"/> dahil,
/// <paramref name="ToUtc"/> HARİÇ (exclusive) UTC anlarıdır — yerel gün aralığı
/// controller'da UTC'ye çevrilir.
/// </summary>
/// <param name="Level">"Error" | "Critical" | null/boş = tümü.</param>
/// <param name="Text">Serbest metin — category, source, message, exceptionMessage alanlarında arar.</param>
public sealed record SystemErrorSearchRequest(
    DateTime FromUtc,
    DateTime ToUtc,
    string? Level = null,
    string? Text = null,
    int Page = 1,
    int PageSize = 50);

public sealed record SystemErrorSearchResult(
    IReadOnlyList<SystemErrorEntry> Items,
    int Total);

/// <summary>
/// Günlük JSONL hata log dosyalarından okuma/sorgulama servisi (sunucu geneli — per-company
/// bölümleme YOK). Dosyalar yeniden-eskiye taranır; satırlar önce ucuz substring
/// ön-filtresinden geçer, yalnızca aday satırlar JSON parse edilir.
/// </summary>
public interface ISystemErrorLogQueryService
{
    Task<SystemErrorSearchResult> SearchAsync(SystemErrorSearchRequest request, CancellationToken ct);
}
