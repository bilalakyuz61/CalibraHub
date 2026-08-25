using System.Security.Cryptography;

namespace CalibraHub.Application.Services.Security;

/// <summary>
/// Tek kullanımlık ilk giriş / sıfırlama parolası üreteci.
///
/// <para><b>Neden (2026-08-24 güvenlik denetimi, K4):</b> Kullanıcı oluşturma, parola sıfırlama
/// ve bootstrap admin seed'i sabit <c>"12345678"</c> kullanıyordu. Bu değer kaynak kodda ve
/// <c>appsettings.json</c>'da yayımlıydı; üstelik zorunlu değiştirme mekanizması yok. Sonuç:
/// PBKDF2 altyapısı ne kadar güçlü olursa olsun, hesaplar tahmin edilebilir tek bir parolayla
/// açılabiliyordu.</para>
///
/// <para>Üretilen parola <c>PasswordHasher.ValidateStrength</c> kurallarını (10+ karakter,
/// büyük/küçük harf, rakam, özel karakter) karşılar; karıştırma da dahil tüm rastgelelik
/// <see cref="RandomNumberGenerator"/> iledir.</para>
///
/// <para><b>Kullanım notu:</b> üretilen parola çağıran tarafından kullanıcıya BİR KEZ
/// gösterilmelidir (mevcut ekranlar "Varsayılan şifre: …" mesajını zaten gösteriyor).</para>
/// </summary>
public static class TemporaryPassword
{
    // Karıştırılabilecek karakterler (0/O, 1/l/I) bilinçli olarak dışarıda — parola
    // çoğu zaman telefonla/sözlü iletiliyor.
    private const string Upper = "ABCDEFGHJKLMNPQRSTUVWXYZ";
    private const string Lower = "abcdefghijkmnpqrstuvwxyz";
    private const string Digit = "23456789";
    private const string Special = "!@#$%*?-_";

    /// <summary>Güçlü, rastgele bir geçici parola üretir (varsayılan 14 karakter).</summary>
    public static string Generate(int length = 14)
    {
        if (length < 10) length = 10; // ValidateStrength alt sınırı

        var all = Upper + Lower + Digit + Special;
        var chars = new List<char>(length)
        {
            Upper[RandomNumberGenerator.GetInt32(Upper.Length)],
            Lower[RandomNumberGenerator.GetInt32(Lower.Length)],
            Digit[RandomNumberGenerator.GetInt32(Digit.Length)],
            Special[RandomNumberGenerator.GetInt32(Special.Length)],
        };
        while (chars.Count < length)
            chars.Add(all[RandomNumberGenerator.GetInt32(all.Length)]);

        // Fisher-Yates — zorunlu karakterler hep baştaki 4 pozisyonda kalmasın.
        for (var i = chars.Count - 1; i > 0; i--)
        {
            var j = RandomNumberGenerator.GetInt32(i + 1);
            (chars[i], chars[j]) = (chars[j], chars[i]);
        }
        return new string(chars.ToArray());
    }
}
