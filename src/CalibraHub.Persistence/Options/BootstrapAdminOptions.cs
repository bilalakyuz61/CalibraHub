namespace CalibraHub.Persistence.Options;

public sealed class BootstrapAdminOptions
{
    public const string SectionName = "BootstrapAdmin";

    public bool SeedOnStartup { get; init; } = true;
    public string FullName { get; init; } = "Sistem Admin";
    public string Email { get; init; } = "admin@calibra.local";
    public string EmployeeCode { get; init; } = "ADM-001";
    public string DefaultPassword { get; init; } = "12345678";
}
