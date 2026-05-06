namespace CalibraHub.Application.Abstractions.Services;

/// <summary>
/// Scheduled task SQL prosedur parametrelerinde kullanilan {TOKEN} placeholder'lari
/// icin sirket bagli degerleri uretir. UI'dan tanimlanan task icin parametre degeri
/// "{COMPANY_ID}" gibi bir placeholder ise, executor calistirmadan once bu servisle
/// tokenleri gercek sirket degerleriyle replace eder.
/// </summary>
public interface IScheduledTaskTokenResolver
{
    /// <summary>
    /// Verilen sirket id'si icin token-deger sozlugunu doner. Anahtar token adi
    /// (susluler haric, ornegin "COMPANY_ID"), deger ise replace edilecek string.
    /// Sirket bulunamazsa veya bagli kayit yoksa, ilgili token bos string olarak doner.
    /// </summary>
    Task<IReadOnlyDictionary<string, string?>> ResolveAsync(int companyId, CancellationToken cancellationToken);
}
