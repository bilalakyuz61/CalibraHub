namespace CalibraHub.Persistence.Options;

public sealed class BootstrapAdminOptions
{
    public const string SectionName = "BootstrapAdmin";

    public bool SeedOnStartup { get; init; } = true;
    public string FullName { get; init; } = "Sistem Admin";
    public string Email { get; init; } = "admin@calibra.local";
    public string EmployeeCode { get; init; } = "ADM-001";
    /// <summary>2026-08-24 (K4): sabit varsayilan KALDIRILDI. Bos birakilirsa seed sirasinda
    /// rastgele bir parola uretilip BIR KEZ loglanir; onceden belirlemek icin appsettings
    /// BootstrapAdmin:DefaultPassword kullanilir.</summary>
    public string DefaultPassword { get; init; } = string.Empty;
}
