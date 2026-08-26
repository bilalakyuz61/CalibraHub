namespace CalibraHub.Application.Contracts;

public sealed record AuthenticatedUserDto(
    int Id,
    string FullName,
    string Email,
    string Role,
    int CompanyId,
    string CompanyName,
    int? DepartmentId = null,
    // Gecici parolayla acilmis hesap: ilk giriste parola degistirme zorunlu (2026-08-26).
    bool MustChangePassword = false);
