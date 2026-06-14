using System.Collections.Concurrent;

namespace CalibraHub.Application.Services;

/// <summary>
/// Üretim paneli (ShopFloor) PIN brute-force koruması — in-memory hatalı deneme sayacı.
///
/// (CompanyId, PersonnelCode) bazında art arda hatalı PIN denemelerini sayar.
/// Doğru giriş veya manuel reset sayacı sıfırlar. Limit doluşunca <see cref="RegisterFailureAsync"/>
/// true döner; bunun üzerine çağrılan AuthOperator endpoint'i Personnel kaydını pasife alır.
///
/// Tasarım kararı: persistent kolon değil singleton in-memory. Restart sonrası sayaç sıfırlanır,
/// ama "bloklu" durum Personnel.IsActive=0 üzerinden persist olur — kullanıcı yöneticiden açtırır.
/// </summary>
public sealed class ShopFloorLockoutTracker
{
    private readonly ConcurrentDictionary<string, int> _attempts = new(StringComparer.OrdinalIgnoreCase);

    private static string Key(int companyId, string personnelCode) =>
        $"{companyId}|{personnelCode.Trim()}";

    /// <summary>Hatalı deneme sayar. Limit > 0 ve sayaç limite ulaşırsa true döner (lock tetiklenmeli).</summary>
    public bool RegisterFailure(int companyId, string personnelCode, int limit)
    {
        if (string.IsNullOrWhiteSpace(personnelCode)) return false;
        if (limit <= 0) return false;

        var key = Key(companyId, personnelCode);
        var newCount = _attempts.AddOrUpdate(key, 1, (_, v) => v + 1);
        if (newCount >= limit)
        {
            _attempts.TryRemove(key, out _);
            return true;
        }
        return false;
    }

    /// <summary>Başarılı giriş veya manuel reset — sayacı temizler.</summary>
    public void Reset(int companyId, string personnelCode)
    {
        if (string.IsNullOrWhiteSpace(personnelCode)) return;
        _attempts.TryRemove(Key(companyId, personnelCode), out _);
    }

    /// <summary>Geçerli sayac (debug/diagnostic).</summary>
    public int GetCount(int companyId, string personnelCode)
    {
        if (string.IsNullOrWhiteSpace(personnelCode)) return 0;
        return _attempts.TryGetValue(Key(companyId, personnelCode), out var v) ? v : 0;
    }
}
