namespace CalibraHub.Application.Abstractions.Security;

/// <summary>
/// Not içeriğinin DB seviyesinde at-rest şifrelenmesi için kullanılır.
/// Uygulama katmanında şeffaf çalışır: repository yazmadan önce Protect,
/// okuduktan sonra Unprotect çağırır. Kullanıcı değişikliği fark etmez.
///
/// Mevcut (şifrelenmemiş) düz metin kayıtlar da desteklenir: Unprotect
/// başarısız olursa girdiyi olduğu gibi döner (geriye dönük uyum).
/// </summary>
public interface INoteEncryptionService
{
    /// <summary>Düz metni şifrele. Null/empty ise dokunma.</summary>
    string? Protect(string? plaintext);

    /// <summary>Şifreli metni çöz. Hatalı/eski plaintext ise aynen döner.</summary>
    string? Unprotect(string? ciphertext);
}
