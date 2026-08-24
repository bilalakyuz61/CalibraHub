namespace CalibraHub.Infrastructure.Security;

/// <summary>
/// Data Protection anahtar halkasının KALICI konumunu çözer.
///
/// NEDEN (2026-08-24): anahtarlar daha önce <c>{ContentRoot}\.app-data-protection</c>
/// altında tutuluyordu — yani uygulamanın yayın (publish) klasörünün İÇİNDE. Güncelleme
/// bu klasörün üzerine yazdığı (ya da temizlediği) için anahtarlar kaybolabiliyordu.
///
/// Kaybın sonucu GERİ DÖNÜŞSÜZ ve SESSİZ: şifreli not içerikleri bu anahtarla açılıyor ve
/// <see cref="DataProtectionNoteEncryptionService"/> çözemediğinde istisna fırlatmıyor,
/// şifreli metni olduğu gibi döndürüyor. Kullanıcı notların yerinde anlamsız karakterler
/// görür, hiçbir hata düşmez — fark edildiğinde yedekten dönmek için genelde çok geçtir.
///
/// Çözüm: anahtarlar uygulama klasörünün DIŞINDA, ProgramData altında tutulur. Eski
/// kurulumlar için tek seferlik taşıma yapılır — eski klasördeki anahtarlar yeni konuma
/// KOPYALANIR (silinmez; taşıma yarıda kalırsa eski konum yedek olarak durur).
/// </summary>
public static class DataProtectionKeyStore
{
    /// <summary>Eski (kırılgan) konum — yayın klasörünün içinde.</summary>
    public const string LegacyFolderName = ".app-data-protection";

    /// <summary>Kalıcı konum: %ProgramData%\CalibraHub\DataProtectionKeys</summary>
    public static string GetStablePath()
        => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "CalibraHub", "DataProtectionKeys");

    public static string GetLegacyPath(string contentRootPath)
        => Path.Combine(contentRootPath, LegacyFolderName);

    /// <summary>
    /// Kullanılacak anahtar klasörünü döner ve gerekiyorsa eski konumdan taşır.
    /// </summary>
    /// <param name="contentRootPath">Uygulamanın ContentRoot'u (eski konumu bulmak için).</param>
    /// <param name="log">Bilgi/uyarı mesajı geri bildirimi (logger'a bağlanır).</param>
    /// <returns>
    /// Anahtarların yazılacağı klasör. ProgramData yazılamıyorsa (izin yok) eski konuma
    /// düşülür — uygulamanın hiç açılmaması, kırılgan konumda çalışmasından kötüdür.
    /// </returns>
    public static string ResolveAndMigrate(string contentRootPath, Action<string>? log = null)
    {
        var stable = GetStablePath();
        var legacy = GetLegacyPath(contentRootPath);

        try
        {
            Directory.CreateDirectory(stable);
        }
        catch (Exception ex)
        {
            // ProgramData yazılamıyor (kısıtlı servis hesabı vb.) → eski davranışa dön.
            log?.Invoke($"[DataProtection] Kalıcı anahtar klasörü oluşturulamadı ({stable}): {ex.Message}. " +
                        $"Eski konum kullanılacak: {legacy}. DİKKAT: bu klasör güncellemede silinebilir.");
            Directory.CreateDirectory(legacy);
            return legacy;
        }

        // Taşıma yalnız kalıcı konum BOŞken yapılır — dolusu her zaman gerçek kaynaktır.
        if (!HasKeys(stable) && HasKeys(legacy))
        {
            var copied = 0;
            foreach (var file in Directory.GetFiles(legacy, "*.xml"))
            {
                try
                {
                    File.Copy(file, Path.Combine(stable, Path.GetFileName(file)), overwrite: false);
                    copied++;
                }
                catch (Exception ex)
                {
                    // Tek dosya kopyalanamadıysa taşımayı YARIM bırakma — eski konumda kal.
                    // Yarım anahtar halkası, eksik anahtarla açılamayan notlar demektir.
                    log?.Invoke($"[DataProtection] Anahtar taşıma başarısız ({Path.GetFileName(file)}): {ex.Message}. " +
                                $"Eski konum kullanılmaya devam edecek: {legacy}");
                    return legacy;
                }
            }
            log?.Invoke($"[DataProtection] {copied} anahtar dosyası kalıcı konuma taşındı: {stable} " +
                        $"(eski konum yedek olarak korundu: {legacy})");
        }

        return stable;
    }

    /// <summary>Klasörde en az bir anahtar dosyası var mı — sağlık kontrolü de bunu kullanır.</summary>
    public static bool HasKeys(string path)
    {
        try
        {
            return Directory.Exists(path) && Directory.GetFiles(path, "*.xml").Length > 0;
        }
        catch
        {
            // Erişilemeyen klasör "anahtar yok" sayılır — çağıran buna göre uyarır.
            return false;
        }
    }

    /// <summary>Klasördeki anahtar dosyası sayısı (sağlık kontrolü raporu için).</summary>
    public static int CountKeys(string path)
    {
        try
        {
            return Directory.Exists(path) ? Directory.GetFiles(path, "*.xml").Length : 0;
        }
        catch
        {
            return 0;
        }
    }
}
