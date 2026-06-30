using System.Collections.Concurrent;

namespace CalibraHub.Application.Services;

/// <summary>
/// Login brute-force koruması — in-memory başarısız deneme sayacı.
///
/// Email bazında art arda hatalı giriş denemelerini sayar. Pencere içinde limit dolunca
/// hesap geçici olarak kilitlenir; süre dolunca otomatik açılır (ShopFloor gibi kalıcı değil).
///
/// Tasarım: singleton in-memory. Restart sonrası sayaç sıfırlanır — kabul edilebilir,
/// tek-instance on-premises kurulum. Kilitli durum hafızada tutulur; restart klitlemeyi kaldırır.
/// </summary>
public sealed class LoginLockoutTracker
{
    private readonly record struct Entry(int Count, DateTime FirstFailure, DateTime? LockedUntil);

    private readonly ConcurrentDictionary<string, Entry> _state = new(StringComparer.OrdinalIgnoreCase);

    public const int MaxAttempts   = 5;
    public const int WindowMinutes = 15;
    public const int LockoutMinutes = 15;

    private static string Key(string email) => email.Trim().ToLowerInvariant();

    /// <summary>Hesap kilitliyse kilit bitiş zamanını döner; değilse null.</summary>
    public DateTime? CheckLocked(string email)
    {
        if (string.IsNullOrWhiteSpace(email)) return null;
        var key = Key(email);
        if (!_state.TryGetValue(key, out var entry)) return null;
        if (!entry.LockedUntil.HasValue) return null;

        if (DateTime.UtcNow < entry.LockedUntil.Value)
            return entry.LockedUntil.Value;

        // Kilit süresi doldu — kaydı temizle
        _state.TryRemove(key, out _);
        return null;
    }

    /// <summary>
    /// Başarısız deneme kaydeder. Limit dolunca hesabı kilitler ve true döner.
    /// </summary>
    public bool RegisterFailure(string email)
    {
        if (string.IsNullOrWhiteSpace(email)) return false;
        var key = Key(email);
        var now = DateTime.UtcNow;

        _state.AddOrUpdate(
            key,
            _ => new Entry(1, now, null),
            (_, existing) =>
            {
                // Pencere dışındaysa sayacı sıfırla
                if ((now - existing.FirstFailure).TotalMinutes > WindowMinutes)
                    return new Entry(1, now, null);

                var newCount = existing.Count + 1;
                var lockedUntil = newCount >= MaxAttempts
                    ? now.AddMinutes(LockoutMinutes)
                    : (DateTime?)null;
                return new Entry(newCount, existing.FirstFailure, lockedUntil);
            });

        return _state.TryGetValue(key, out var e) && e.LockedUntil.HasValue;
    }

    /// <summary>Başarılı giriş sonrası sayacı sıfırlar.</summary>
    public void Reset(string email)
    {
        if (!string.IsNullOrWhiteSpace(email))
            _state.TryRemove(Key(email), out _);
    }

    /// <summary>Geçerli deneme sayısı (test/diagnostic).</summary>
    public int GetCount(string email)
    {
        if (string.IsNullOrWhiteSpace(email)) return 0;
        return _state.TryGetValue(Key(email), out var e) ? e.Count : 0;
    }
}
