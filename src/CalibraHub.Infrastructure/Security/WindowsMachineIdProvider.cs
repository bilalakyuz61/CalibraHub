using CalibraHub.Application.Abstractions.Services;
using Microsoft.Extensions.Configuration;

namespace CalibraHub.Infrastructure.Security;

/// <summary>
/// Makine kimligini belirler. Oncelik sirasi:
///   1) LicenseSettings:MachineIdOverride (config'te manuel belirtilen)
///   2) Windows: HKLM\SOFTWARE\Microsoft\Cryptography\MachineGuid (reg key)
///   3) Environment.MachineName + OSVersion (fallback — kalici degil)
///
/// Reg key SADECE Windows'ta okunur; diger OS'larda 3. fallback devreye girer.
/// Lisans uretimi sirasinda vendor'un kullandigi kaynakla ayni olmali;
/// production'da genellikle MachineIdOverride ile musteriye ozel sabit
/// bir GUID set edilir.
/// </summary>
public sealed class WindowsMachineIdProvider : IMachineIdProvider
{
    private readonly string? _override;

    public WindowsMachineIdProvider(IConfiguration configuration)
    {
        _override = configuration["LicenseSettings:MachineIdOverride"];
    }

    public string GetMachineId()
    {
        if (!string.IsNullOrWhiteSpace(_override)) return _override.Trim();

        if (OperatingSystem.IsWindows())
        {
            try
            {
                using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                    @"SOFTWARE\Microsoft\Cryptography", writable: false);
                var guid = key?.GetValue("MachineGuid") as string;
                if (!string.IsNullOrWhiteSpace(guid)) return guid.Trim();
            }
            catch { /* swallow — fallback'a duseriz */ }
        }

        // Fallback — kalici degil ama son care
        return $"{Environment.MachineName}-{Environment.OSVersion.Platform}";
    }
}
