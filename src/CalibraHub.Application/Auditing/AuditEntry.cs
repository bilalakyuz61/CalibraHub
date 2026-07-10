namespace CalibraHub.Application.Auditing;

/// <summary>
/// İşlem log modülü (audit trail) — standart aksiyon kodları.
/// Log dosyasında string olarak saklanır; UI etiketleri <see cref="AuditFieldLabels.ActionLabel"/>.
/// </summary>
public static class AuditActions
{
    public const string Insert      = "Insert";
    public const string Update      = "Update";
    public const string Delete      = "Delete";
    public const string Login       = "Login";
    public const string LoginFailed = "LoginFailed";
    public const string Logout      = "Logout";
    public const string Event       = "Event";

    public static readonly IReadOnlyList<string> All =
        [Insert, Update, Delete, Login, LoginFailed, Logout, Event];
}

/// <summary>Tek bir alan değişikliği — update loglarında yalnızca değişen alanlar tutulur.</summary>
/// <param name="Field">Property/kolon adı (ör. "Quantity").</param>
/// <param name="Label">Kullanıcıya gösterilecek Türkçe etiket (ör. "Miktar").</param>
/// <param name="Old">Eski değer (normalize edilmiş string, null = boştu).</param>
/// <param name="New">Yeni değer (normalize edilmiş string, null = boşaltıldı).</param>
public sealed record AuditFieldChange(string Field, string? Label, string? Old, string? New);

/// <summary>
/// Günlük JSONL dosyasına yazılan tek log satırı. Alan adları camelCase serialize edilir
/// (ts, companyId, userId, user, action, entity, entityId, title, changes, detail, ip, src).
/// </summary>
public sealed class AuditEntry
{
    /// <summary>UTC zaman damgası.</summary>
    public DateTime Ts { get; set; }

    public int CompanyId { get; set; }

    public int? UserId { get; set; }

    /// <summary>Kullanıcı adı / e-posta; arka plan işlemlerinde "SYSTEM".</summary>
    public string? User { get; set; }

    /// <summary><see cref="AuditActions"/> değerlerinden biri.</summary>
    public string Action { get; set; } = AuditActions.Event;

    /// <summary>Entity/belge tipi kodu (ör. "satis_siparisi", "Item", "Contact").</summary>
    public string? Entity { get; set; }

    public string? EntityId { get; set; }

    /// <summary>Kaydın kullanıcıya gösterilen kimliği (belge no, ad vb.).</summary>
    public string? Title { get; set; }

    /// <summary>Update işlemlerinde yalnızca değişen alanlar; diğer aksiyonlarda null.</summary>
    public List<AuditFieldChange>? Changes { get; set; }

    public string? Detail { get; set; }

    public string? Ip { get; set; }

    /// <summary>Kaynak: "Web" | "System" | "Import" | "Integration".</summary>
    public string? Src { get; set; }
}

/// <summary>
/// Log satırına damgalanacak kimlik bilgisi. HttpContext dışındaki akışlarda
/// (login öncesi, background job) açıkça verilir; null alanlar ortamdan çözümlenir.
/// </summary>
public sealed record AuditActor(
    int? CompanyId = null,
    int? UserId = null,
    string? UserName = null,
    string? Ip = null,
    string? Source = null);
