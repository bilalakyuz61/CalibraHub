namespace CalibraHub.Application.Auditing;

/// <summary>
/// Merkezi işlem log servisi — tüm insert/update/delete ve güvenlik olayları buradan geçer.
///
/// Çağrılar SENKRON ve non-blocking'dir: girdi bounded olmayan bir Channel'a bırakılır,
/// <c>AuditFileWriter</c> background servisi günlük JSONL dosyasına yazar. Log yazımı
/// hiçbir iş akışını bekletmez ve hata durumunda iş akışını asla bozmaz.
///
/// Kullanım (servis/controller içinden):
///   _audit.LogInsert("Item", id, item.Name);
///   _audit.LogUpdate("Item", id, item.Name, oldItem, newItem);   // yalnızca değişen alanlar loglanır
///   _audit.LogDelete("Item", id, item.Name);
///   _audit.LogEvent(AuditActions.Login, actor: new AuditActor(companyId, userId, email, ip));
/// </summary>
public interface IAuditTrailService
{
    /// <summary>
    /// Yeni kayıt oluşturuldu. <paramref name="snapshot"/> verilirse kaydın dolu skaler
    /// alanları "boş → değer" formatında log satırına eklenir (ilk değer dökümü).
    /// <paramref name="snapshotIgnore"/> ile hassas alanlar (PIN, hash) dışarıda bırakılır.
    /// <paramref name="extraChanges"/> snapshot'a ek satırlar içindir (ör. belge kalem dökümü).
    /// </summary>
    void LogInsert(string entity, object? entityId, string? title, string? detail = null, AuditActor? actor = null,
        object? snapshot = null, IEnumerable<string>? snapshotIgnore = null,
        IReadOnlyList<AuditFieldChange>? extraChanges = null);

    /// <summary>
    /// Kayıt güncellendi — <paramref name="oldSnapshot"/> ile <paramref name="newSnapshot"/>
    /// reflection ile karşılaştırılır, yalnızca değişen alanlar loglanır.
    /// Hiçbir alan değişmediyse log satırı YAZILMAZ (gürültü önleme).
    /// </summary>
    void LogUpdate(string entity, object? entityId, string? title, object? oldSnapshot, object? newSnapshot,
        string? detail = null, AuditActor? actor = null);

    /// <summary>
    /// Kayıt güncellendi — değişiklik listesi çağıran tarafından hazırlanmıştır
    /// (ör. kalem satırı diff'leri). Boş liste verilirse log yazılmaz.
    /// </summary>
    void LogChanges(string entity, object? entityId, string? title, IReadOnlyList<AuditFieldChange> changes,
        string? detail = null, AuditActor? actor = null);

    /// <summary>
    /// Kayıt silindi (soft/hard farketmez — kullanıcı gözünden silme).
    /// <paramref name="snapshot"/> ile silinen içeriğin dökümü (ör. belge kalemleri)
    /// log satırına eklenebilir — Old dolu, New null olacak şekilde verilir.
    /// </summary>
    void LogDelete(string entity, object? entityId, string? title, string? detail = null, AuditActor? actor = null,
        IReadOnlyList<AuditFieldChange>? snapshot = null);

    /// <summary>Güvenlik/oturum olayı (Login, LoginFailed, Logout) veya serbest olay.</summary>
    void LogEvent(string action, string? detail = null, AuditActor? actor = null,
        string? entity = null, object? entityId = null, string? title = null);
}
