using CalibraHub.Application.Services.Messaging;
using Xunit;

namespace CalibraHub.Tests.Security;

/// <summary>
/// 2026-08-24 güvenlik denetimi Y4 (wwwroot'a çalıştırılabilir içerik → depolanmış XSS)
/// regresyon testleri.
///
/// WhatsApp medyası <c>wwwroot/uploads/whatsapp/...</c> altına kaydedilip aynı origin'de
/// servis ediliyor. Uzantı eskiden ya gönderenin dosya adından ya da bilinmeyen MIME'ın alt
/// tipinden türetiliyordu — <c>text/html</c> gelince dosya <c>.html</c> olarak yazılıyor ve
/// tarayıcıda script çalışabiliyordu. Yükleyen taraf çoğu zaman kimliği doğrulanmamış bir
/// dış WhatsApp kullanıcısı olduğu için etki yüksekti.
/// </summary>
public class WaMediaFilesTests
{
    [Theory]
    [InlineData("text/html")]
    [InlineData("image/svg+xml")]
    [InlineData("application/xhtml+xml")]
    [InlineData("application/javascript")]
    [InlineData("text/xml")]
    [InlineData("application/x-msdownload")]
    public void Calistirilabilir_tipler_bin_olur(string tehlikeliMime)
    {
        Assert.Equal("bin", WaMediaFiles.MimeToExtension(tehlikeliMime));
    }

    [Theory]
    [InlineData("image/jpeg", "jpg")]
    [InlineData("image/png", "png")]
    [InlineData("video/mp4", "mp4")]
    [InlineData("audio/ogg", "ogg")]
    [InlineData("application/pdf", "pdf")]
    [InlineData("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "xlsx")]
    [InlineData("text/plain", "txt")]
    public void Mesru_medya_tipleri_korunur(string mime, string beklenenUzanti)
    {
        Assert.Equal(beklenenUzanti, WaMediaFiles.MimeToExtension(mime));
    }

    [Fact]
    public void Charset_ekli_mime_dogru_cozulur()
    {
        Assert.Equal("txt", WaMediaFiles.MimeToExtension("text/plain; charset=utf-8"));
    }

    [Fact]
    public void Bos_mime_bin_olur()
    {
        Assert.Equal("bin", WaMediaFiles.MimeToExtension(null));
        Assert.Equal("bin", WaMediaFiles.MimeToExtension("   "));
    }

    [Theory]
    // Dizin dışına yazma (path traversal) ve ayırıcı denemeleri temizlenmeli.
    [InlineData("../../evil", "______evil")]
    [InlineData("a/b\\c", "a_b_c")]
    [InlineData("dosya adi.html", "dosya_adi_html")]
    public void SafeId_tehlikeli_karakterleri_temizler(string girdi, string beklenen)
    {
        Assert.Equal(beklenen, WaMediaFiles.SafeId(girdi));
    }

    [Fact]
    public void SafeId_normal_kimligi_bozmaz()
    {
        Assert.Equal("wamid-ABC_123", WaMediaFiles.SafeId("wamid-ABC_123"));
    }
}
