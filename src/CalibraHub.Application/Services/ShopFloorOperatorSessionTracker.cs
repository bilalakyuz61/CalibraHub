using System.Collections.Concurrent;

namespace CalibraHub.Application.Services;

/// <summary>
/// Mobil üretim ekranında "bu oturumda hangi operatör PIN ile doğrulandı" kaydı — in-memory.
///
/// NEDEN GEREKLİ: <c>auth-operator</c> ucu PIN'i doğrulayıp operatorId döndürüyordu, ancak
/// <c>operations/start</c> ve <c>operations/complete</c> uçları gelen operatorId'nin PIN ile
/// doğrulanmış olup olmadığını KONTROL ETMİYORDU — yalnız personelin aktif ve üretim operatörü
/// olduğuna bakıyordu. Yani PIN pratikte istemci tarafında kalıyor, hazırlanmış bir istek
/// herhangi bir operatör adına iş başlatıp bitirebiliyordu. Bu sınıf o boşluğu kapatır:
/// start/complete, ya bu kayıtta doğrulanmış bir (kullanıcı, operatör) çifti ister ya da
/// PIN'siz yola izin veren koşulu (kullanıcının KENDİ personel kaydı + IsMobilePinRequired=false).
///
/// Tasarım: persistent tablo değil singleton in-memory (ShopFloorLockoutTracker ile aynı desen).
/// Uygulama yeniden başlarsa doğrulama düşer ve kullanıcı PIN'i tekrar girer — güvenli taraf.
/// </summary>
public sealed class ShopFloorOperatorSessionTracker
{
    /// <summary>
    /// Doğrulamanın geçerlilik süresi. Bir vardiyayı kapsayacak kadar uzun (operatör her
    /// operasyonda PIN girmek zorunda kalmasın), ertesi güne taşacak kadar değil.
    /// </summary>
    private static readonly TimeSpan Ttl = TimeSpan.FromHours(12);

    private readonly ConcurrentDictionary<string, DateTime> _verified = new(StringComparer.Ordinal);

    private static string Key(int companyId, int userId, int operatorId) => $"{companyId}|{userId}|{operatorId}";

    /// <summary>PIN/kart doğrulaması başarılı olduğunda çağrılır.</summary>
    public void MarkVerified(int companyId, int userId, int operatorId)
    {
        if (userId <= 0 || operatorId <= 0) return;
        _verified[Key(companyId, userId, operatorId)] = DateTime.UtcNow.Add(Ttl);
    }

    /// <summary>Bu kullanıcı, bu operatör adına işlem yapmak için PIN doğrulaması yapmış mı?</summary>
    public bool IsVerified(int companyId, int userId, int operatorId)
    {
        if (userId <= 0 || operatorId <= 0) return false;
        var key = Key(companyId, userId, operatorId);
        if (!_verified.TryGetValue(key, out var expiresAt)) return false;
        if (expiresAt > DateTime.UtcNow) return true;

        // Süresi dolmuş kaydı temizle — sözlük sınırsız büyümesin.
        _verified.TryRemove(key, out _);
        return false;
    }

    /// <summary>Oturum kapanışı / operatör değişimi için doğrulamayı düşürür.</summary>
    public void Clear(int companyId, int userId, int operatorId)
        => _verified.TryRemove(Key(companyId, userId, operatorId), out _);
}
