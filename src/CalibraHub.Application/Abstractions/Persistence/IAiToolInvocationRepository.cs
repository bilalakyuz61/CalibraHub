namespace CalibraHub.Application.Abstractions.Persistence;

/// <summary>
/// 2026-05-24 — Calibo write tool denetim kaydi repository'si.
/// Yonetici "Calibo bu hafta neler yapti?" sorusuna cevap veren tablo (AiToolInvocation).
/// </summary>
public interface IAiToolInvocationRepository
{
    Task LogExecutedAsync(AiToolInvocationLogEntry entry, CancellationToken ct);
}

public sealed record AiToolInvocationLogEntry(
    int UserId,
    string ToolName,
    string? ActionLabel,
    string? ArgumentsJson,
    string Status,                  // "executed" | "error"
    string? ResultSummary,
    string? AffectedEntity,         // "Contact:42" | "Item:108" | "Document:7"
    string? ErrorMessage);
