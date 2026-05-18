namespace CalibraHub.Domain.Common;

/// <summary>
/// Domain invariant ihlali — entity'nin kendi davranis kurali kirildiginda firlatilir.
///
/// Ornek senaryolar:
///   - Document.Approve() — Status Sent degilse
///   - Document.DiscountRate = -5 — negatif iskonto
///   - Document.AddLine(qty=0) — sifir miktarli kalem
///
/// Bu exception "kullanici hatasi" sinifindandir (HTTP 400 mapping'i icin).
/// Application/Web katmaninda yakalanip uygun cevaba donusturulur (rapor §2.7 ileride
/// global exception middleware ile JSON 400 + traceId formatina cevirir).
/// </summary>
public sealed class DomainException : Exception
{
    public DomainException(string message) : base(message) { }
    public DomainException(string message, Exception innerException) : base(message, innerException) { }

    /// <summary>
    /// Invariant kontrol shortcut: condition false ise exception firlatir.
    /// Kullanim: DomainException.ThrowIf(amount &lt; 0, "Tutar negatif olamaz");
    /// </summary>
    public static void ThrowIf(bool condition, string message)
    {
        if (condition) throw new DomainException(message);
    }
}
