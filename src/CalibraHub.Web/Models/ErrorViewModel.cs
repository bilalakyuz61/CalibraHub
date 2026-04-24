namespace CalibraHub.Web.Models;

public class ErrorViewModel
{
    public string? RequestId { get; set; }
    public bool ShowRequestId => !string.IsNullOrEmpty(RequestId);

    /// <summary>Istegin basladigi URL path'i — "Hangi sayfada hata oldu" icin.</summary>
    public string? Path { get; set; }

    /// <summary>Hatanin tipinin adi (ornek: InvalidOperationException).</summary>
    public string? ExceptionType { get; set; }

    /// <summary>Hatanin kisa mesaji — kullaniciya her zaman gosterilir.</summary>
    public string? Message { get; set; }

    /// <summary>Stack trace — yalnizca Development'ta gosterilir.</summary>
    public string? StackTrace { get; set; }

    /// <summary>Yardimci ipucu — bilinen hata tiplerinde turkce aciklama.</summary>
    public string? Hint { get; set; }

    public bool IsDevelopment { get; set; }
}
