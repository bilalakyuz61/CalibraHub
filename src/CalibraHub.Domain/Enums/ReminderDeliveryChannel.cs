namespace CalibraHub.Domain.Enums;

/// <summary>
/// Hatirlatici tetiklendiginde nereye gonderilecek — uygulama ici bildirim,
/// e-posta veya her ikisi. Default: InApp.
/// </summary>
public enum ReminderDeliveryChannel
{
    InApp = 0,
    Email = 1,
    Both  = 2,
}
