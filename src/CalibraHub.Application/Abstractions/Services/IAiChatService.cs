using CalibraHub.Application.Contracts;

namespace CalibraHub.Application.Abstractions.Services;

/// <summary>
/// 2026-05-23 — Floating chat widget + AI Asistan tab kullanım katmanı.
///
/// AskStreamAsync: IAsyncEnumerable<string> ile cevap parça parça yield edilir
/// (SSE stream'e map edilir Controller'da).
/// </summary>
public interface IAiChatService
{
    IAsyncEnumerable<string> AskStreamAsync(
        ChatRequest request,
        int? userId,
        CancellationToken ct);

    /// <summary>Tek-shot (non-stream) — opsiyonel, basit kullanım için.</summary>
    Task<Contracts.ChatResponse> AskAsync(
        ChatRequest request,
        int? userId,
        CancellationToken ct);
}
