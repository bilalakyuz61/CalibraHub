namespace CalibraHub.Application.Abstractions.Services;

/// <summary>
/// Sunucunun benzersiz makine kimligini doner — lisans sistemi bu degeri
/// binding icin kullanir. Windows'ta registry'den Machine GUID, diger
/// platformlarda fallback yollar. LicenseSettings.MachineIdOverride doluysa
/// o deger return edilir (test/dev senaryolari icin).
/// </summary>
public interface IMachineIdProvider
{
    string GetMachineId();
}
