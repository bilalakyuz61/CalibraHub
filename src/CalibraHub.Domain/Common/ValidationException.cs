namespace CalibraHub.Domain.Common;

/// <summary>
/// Kullanici girdi/request validation hatasi — alan bazli detay tasir.
/// HTTP 400 mapping'i (ApiExceptionMiddleware). DomainException'dan farki:
/// DomainException = entity invariant ihlali (sebep tek mesaj),
/// ValidationException = form/request validation (sebep field -> message dict).
///
/// FluentValidation entegrasyonu sonrasi otomatik dogan exception tipi olabilir.
///
/// Ornek:
///   throw new ValidationException(new Dictionary&lt;string, string[]&gt;
///   {
///       ["Email"]    = new[] { "Gecerli email girilmedi." },
///       ["Password"] = new[] { "En az 8 karakter olmali.", "En az 1 rakam olmali." }
///   });
/// </summary>
public sealed class ValidationException : Exception
{
    /// <summary>
    /// Field bazli hata mesajlari: { fieldName: [messages...] }.
    /// Tek-mesaj senaryosunda key "" (bos) kullanilabilir.
    /// </summary>
    public IReadOnlyDictionary<string, string[]> Errors { get; }

    public ValidationException(string message)
        : base(message)
    {
        Errors = new Dictionary<string, string[]> { [""] = new[] { message } };
    }

    public ValidationException(IReadOnlyDictionary<string, string[]> errors)
        : base("Validation hatasi.")
    {
        Errors = errors ?? new Dictionary<string, string[]>();
    }
}
