namespace CalibraHub.Application.Contracts;

// ════════════════════════════════════════════════════════════════════════
// 2026-05-23 Yapay Zeka Entegrasyonu DTO'ları
//
// Admin tarafı: AiProvider CRUD (Şirket Ayarları)
// Kullanıcı tarafı: AiUserKey CRUD (Profil → AI Anahtarlarım)
// Runtime: ChatRequest/Response, NlSql, Summarize
// ════════════════════════════════════════════════════════════════════════

// ── Admin tarafı (Şirket Ayarları "Yapay Zeka" sekmesi) ────────────────

/// <summary>
/// Şirket Ayarları AI sekmesinde listelenen provider.
/// ApiKey ASLA dönülmez — sadece "var/yok" bilgisi (HasApiKey).
/// </summary>
public sealed record AiProviderDto(
    int Id,
    string Code,
    string Label,
    bool HasApiKey,             // true ise key girilmiş (gerçek değer dönmez)
    string? EndpointUrl,
    string? DefaultModel,
    string? ExtraJson,
    bool IsActive,
    bool IsDefault,
    int SortOrder,
    DateTime Created,
    DateTime? Updated);

/// <summary>
/// Wizard/Modal kaydet — Id=0 yeni, >0 update.
/// ApiKey boş ise mevcut key korunur (admin sadece label/endpoint vs. güncelliyorsa).
/// </summary>
public sealed record SaveAiProviderRequest(
    int Id,
    string Code,
    string Label,
    string? ApiKey,             // PLAIN — backend encrypt eder. NULL/boş ise mevcut korunur
    string? EndpointUrl,
    string? DefaultModel,
    string? ExtraJson,
    bool IsActive,
    bool IsDefault,
    int SortOrder);

/// <summary>
/// Kullanıcıya açık (sohbet penceresi dropdown'unda) görünen minimal liste.
/// IsUserOverride = true ise kullanıcının kendi key'i tanımlı, false ise şirket default.
/// </summary>
public sealed record AiProviderListItemDto(
    int Id,
    string Code,
    string Label,
    string? DefaultModel,
    bool IsDefault,
    bool IsUserOverride);

// ── Kullanıcı tarafı (Profil → AI Anahtarlarım) ──────────────────────────

/// <summary>
/// Kullanıcının bir provider için override key'i. ApiKey'in gerçek değeri DÖNMEZ —
/// sadece HasApiKey flag'i. Override silmek için POST /Account/AiKeys/delete/{providerId}.
/// </summary>
public sealed record AiUserKeyDto(
    int ProviderId,
    string ProviderCode,
    string ProviderLabel,
    bool HasOverride,
    DateTime? OverrideCreated);

public sealed record SaveAiUserKeyRequest(
    int ProviderId,
    string ApiKey);             // PLAIN — backend encrypt eder

// ── Runtime: Chat (floating widget) ──────────────────────────────────────

public sealed record ChatMessageDto(
    string Role,                // "user" | "assistant" | "system"
    string Content,
    // 2026-05-24: Mesaja ekli dosyalar/resimler. Sadece "user" rolunde dolu olur.
    IReadOnlyList<ChatAttachmentDto>? Attachments = null);

/// <summary>
/// Bir chat mesajina eklenen dosya. Iki ana tip destekler:
///   - Resim (image/*): base64 data + mime type. Multimodal model'e iletilir (Ollama vision/Anthropic/OpenAI vision/Gemini).
///   - Text (text/*, .txt/.md/.csv/.json/.log/.sql/.cs/.js vs.): content alani sunucuda dolar — mesaj basina system context olarak prepend edilir.
/// PDF ve DOCX ileride eklenebilir (su an extractor yok, "binyari dosya destelenmiyor" hatasi doner).
/// </summary>
public sealed record ChatAttachmentDto(
    string Name,                // orijinal dosya adi (display + log)
    string MimeType,            // image/png, image/jpeg, text/plain, text/markdown, application/pdf vb.
    string? Base64Data,         // resimler icin doludur (data:; prefix YOK — saf base64)
    string? TextContent);       // text dosyalar icin doludur (UTF-8 metin)

public sealed record ChatRequest(
    string? ProviderCode,       // null ise default provider
    string? Model,              // null ise provider.DefaultModel
    IReadOnlyList<ChatMessageDto> Messages,
    string? Context = null);    // sayfa context'i (formCode + recordId vb. JSON)

public sealed record ChatResponse(
    bool Success,
    string? Text,
    string? Error);

// ── Runtime: Summarize (form özetleme — Faz 1.B) ────────────────────────

public sealed record SummarizeRequest(
    string FormCode,
    string RecordId,
    string? ProviderCode);

public sealed record SummarizeResponse(
    bool Success,
    string? Summary,
    string? Error);

// ── Runtime: NL → SQL Analiz (Faz 1.C) ──────────────────────────────────

public sealed record NlSqlRequest(
    string Query,               // doğal dil: "geçen ay en çok satan 10 ürün"
    string? ProviderCode);

public sealed record NlSqlResponse(
    bool Success,
    string? GeneratedSql,
    IReadOnlyList<string>? Columns,
    IReadOnlyList<IReadOnlyDictionary<string, object?>>? Rows,
    int? RowCount,
    string? Error);
