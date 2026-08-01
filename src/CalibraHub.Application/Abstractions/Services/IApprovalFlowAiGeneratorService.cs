using CalibraHub.Application.Contracts;

namespace CalibraHub.Application.Abstractions.Services;

/// <summary>
/// 2026-08-01 — Doğal dil komuttan onay akışı TASLAĞI üretir (node/edge/rule).
/// Direkt kaydetme YOK; sonuç ApprovalFlowDesigner'ın React Flow state'ine
/// yüklenmek üzere önizleme olarak döner. Sağlayıcı Company Settings'teki aktif
/// AI provider'dır (providerCode=null → IAiClientFactory default'a düşer, Groq'a
/// hardcode yok).
/// </summary>
public interface IApprovalFlowAiGeneratorService
{
    /// <summary>Doğal dil komuttan taslak node/edge/rule listesi üretir.</summary>
    /// <param name="command">Kullanıcının doğal dil komutu (örn. "3000 TL üzeri satın alma siparişleri önce depo müdürüne, sonra genel müdüre gitsin").</param>
    /// <param name="documentKind">Akışın bağlı olduğu belge türü kodu (opsiyonel, sadece prompt bağlamı için — DocumentKind seçimi frontend'de kalır).</param>
    /// <param name="users">Mevcut şirkete ait aktif kullanıcı listesi — SpecificUser eşleme kaynağı.</param>
    /// <param name="departments">Mevcut şirkete ait aktif departman listesi — Department eşleme kaynağı.</param>
    /// <param name="userId">İstek sahibi kullanıcı — AI key çözümlemesinde user-override key denenir.</param>
    Task<ApprovalFlowAiGenerationResult> GenerateAsync(
        string command,
        string? documentKind,
        IReadOnlyList<ApprovalFlowAiUserRef> users,
        IReadOnlyList<ApprovalFlowAiDepartmentRef> departments,
        int? userId,
        CancellationToken ct);
}
