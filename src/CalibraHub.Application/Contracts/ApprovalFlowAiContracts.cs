namespace CalibraHub.Application.Contracts;

// ── Onay akışı AI üretimi (doğal dil → taslak node/edge/rule) ──────────────────
// 2026-08-01: /ApprovalFlow/GenerateStepsAi. Kullanıcı komutundan TASLAK üretilir,
// direkt kaydetme YOK — sonuç ApprovalFlowDesigner'ın (React Flow) beklediği
// {nodes, edges, rules} şekliyle birebir uyumludur (bkz. ApprovalFlowController.
// BuildDesignerInitialPayload). Frontend Onayla/Uygula dediğinde mevcut designer
// state'ine (setNodes/setEdges/setRules) yükler; kaydetme her zaman kullanıcının
// mevcut "Kaydet" butonuyla olur.

/// <summary>Context — mevcut şirkete ait kullanıcı listesi (SpecificUser eşleme için).</summary>
public sealed record ApprovalFlowAiUserRef(int Id, string Name);

/// <summary>Context — mevcut şirkete ait departman listesi (Department eşleme için).</summary>
public sealed record ApprovalFlowAiDepartmentRef(int Id, string Name);

/// <summary>
/// AI üretim sonucu. Ok=false ise Nodes/Edges/Rules boş liste, Error dolu.
/// Warnings: isim→ID eşleşmedi (approverId boş bırakıldı, tasarımcıda seçilmeli) gibi
/// kullanıcıya gösterilecek uyarılar — üretim başarısız DEĞİLDİR, sadece dikkat gerektirir.
/// </summary>
public sealed record ApprovalFlowAiGenerationResult(
    bool Ok,
    IReadOnlyList<object> Nodes,
    IReadOnlyList<object> Edges,
    IReadOnlyList<object> Rules,
    IReadOnlyList<string> Warnings,
    string? Error);
