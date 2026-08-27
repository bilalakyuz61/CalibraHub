namespace CalibraHub.Web.Infrastructure.Collaboration;

/// <summary>
/// Eşzamanlı düzenleme kilidinin SUNUCU tarafında zorlanması (2026-08-27).
///
/// Kilit şimdiye kadar yalnız arayüzde uygulanıyordu: <c>collaboration.js</c> ikinci
/// kullanıcının alanlarını <c>disabled</c> yapıyor, ama hiçbir Save endpoint'i kilidi
/// sorgulamıyordu — doğrudan POST atan (veya JS'i bypass eden) istemci kilitli kayda
/// yazabiliyordu. Bu sınıf o kontrolü tek bir yerde toplar; çağıran endpoint kaydetmeden
/// önce <see cref="CheckWrite"/> çağırır ve dönen mesaj doluysa isteği reddeder.
///
/// Sahiplik <b>UserId</b> üzerinden kontrol edilir, SessionId üzerinden DEĞİL: uygulama
/// yeniden başladığında kilitler DB'den geri yüklenirken SessionId/ConnectionId boş yazılır
/// (<c>CollaborationRuntimeStore.RestoreLock</c>), session tabanlı bir kontrol o noktadan
/// sonra yanlış çalışırdı. Aynı kullanıcının ikinci sekmesi kendi kilidini geçebilir —
/// bu kasıtlıdır (kullanıcı kendi kaydını kilitlemez).
/// </summary>
public sealed class CollaborationLockGuard
{
    private readonly CollaborationRuntimeStore _store;

    public CollaborationLockGuard(CollaborationRuntimeStore store) => _store = store;

    /// <summary>
    /// İstemcinin <c>collaboration.js → normalizeToken</c> fonksiyonunun birebir C# karşılığı:
    /// camelCase sınırına tire koyar, alfanümerik olmayanları tireye çevirir, tekrarlı ve
    /// baştaki/sondaki tireleri atar, küçük harfe indirir. ("SALES_QUOTE_EDIT" → "sales-quote-edit")
    ///
    /// KRİTİK: kilit anahtarı istemcinin ürettiği bu biçimle saklanır. Sunucu farklı bir
    /// normalizasyon uygularsa (ör. yalnız ToLower) anahtar TUTMAZ ve guard sessizce
    /// "kilit yok" der — yani hiç çalışmaz. Bu yüzden iki tarafın aynı kalması zorunludur.
    /// </summary>
    public static string NormalizeToken(string? value)
        => CalibraHub.Application.Collaboration.RecordKeyNormalizer.NormalizeToken(value);

    /// <summary>
    /// Kayıt başkasının kilidindeyse kullanıcıya gösterilecek mesajı, yazabiliyorsa
    /// <c>null</c> döner. Yeni kayıtta (<paramref name="recordId"/> ≤ 0) veya kayıt tipi
    /// bilinmiyorsa kilit aranmaz.
    /// </summary>
    public string? CheckWrite(string? recordType, int recordId, int currentUserId)
    {
        if (recordId <= 0) return null;
        var type = NormalizeToken(recordType);
        if (type.Length == 0) return null;

        var state = _store.GetRecordState(type, recordId.ToString(), DateTime.Now);
        var lockState = state.Lock;
        if (lockState is null) return null;
        if (lockState.OwnerUserId == currentUserId) return null;

        var who = string.IsNullOrWhiteSpace(lockState.OwnerDisplayName)
            ? "başka bir kullanıcı"
            : lockState.OwnerDisplayName;
        return $"Bu kayıt şu anda {who} tarafından düzenleniyor. Kilit kalkınca tekrar deneyin.";
    }
}
