namespace CalibraHub.Web.Models.Finance;

/// <summary>
/// Contacts SmartBoard view model. Controller server-side olarak
/// BoardConfig objesini hazırlar (gercek cari listesi + admin dynamic
/// widget schema). View inline JSON olarak mountSmartBoard'a gecer.
/// </summary>
public sealed class ContactsViewModel
{
    public object? BoardConfig { get; init; }
}
