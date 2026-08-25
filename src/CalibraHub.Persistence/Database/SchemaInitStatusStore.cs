using System.Collections.Concurrent;

namespace CalibraHub.Persistence.Database;

/// <summary>
/// Açılıştaki şema (migration) sonucunun şirket bazında hafızası.
///
/// NEDEN VAR (2026-08-25): per-company şema init zinciri bir şirkette patladığında startup
/// döngüsü onu ATLAYIP devam ediyor — bilinçli bir karar, tek bozuk şirket uygulamayı
/// engellemesin diye. Ama sonucu şuydu: o şirketin DB'si YARIM GÖÇMÜŞ halde kalıyor,
/// uygulama normal açılıyor ve kullanıcı içeri girip çalışmaya başlıyor. Eksik kolon/tablo
/// ilk kullanıldığında "Invalid column name" 500'ü olarak patlıyor — hem de saatler sonra,
/// hiçbir bağlantı kurulamadan.
///
/// Bu sınıf o boşluğu kapatır: init sonucu burada işaretlenir, giriş / şirket değiştirme
/// akışı buna bakıp kullanıcıyı yarım göçmüş şirkete SOKMAZ.
///
/// Durum yalnızca bellektedir — kasıtlı: bir sonraki açılışta zincir yeniden çalışır
/// (idempotent) ve sorun düzelmişse damga kendiliğinden temizlenir. Kalıcı bir "bozuk"
/// bayrağı, düzelmiş bir şirketi elle temizlenene kadar kilitli bırakırdı.
/// </summary>
public sealed class SchemaInitStatusStore
{
    private readonly ConcurrentDictionary<int, string> _failures = new();

    /// <summary>Şema init başarısız — şirket erişime kapatılır.</summary>
    public void MarkFailed(int companyId, string reason)
    {
        if (companyId <= 0) return;
        _failures[companyId] = string.IsNullOrWhiteSpace(reason) ? "Bilinmeyen hata" : reason.Trim();
    }

    /// <summary>Şema init başarılı — varsa eski işaret kaldırılır.</summary>
    public void MarkSucceeded(int companyId)
    {
        if (companyId <= 0) return;
        _failures.TryRemove(companyId, out _);
    }

    /// <summary>Şirketin şeması yarım kalmış mı; kalmışsa sebebi.</summary>
    public bool IsBroken(int companyId, out string? reason)
    {
        reason = null;
        if (companyId <= 0) return false;
        if (!_failures.TryGetValue(companyId, out var r)) return false;
        reason = r;
        return true;
    }

    /// <summary>Sağlık ekranı / tanı için: şeması yarım kalmış şirketler.</summary>
    public IReadOnlyDictionary<int, string> Snapshot() => _failures.ToDictionary(kv => kv.Key, kv => kv.Value);
}
