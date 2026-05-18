using CalibraHub.Application.Contracts;

namespace CalibraHub.Application.Abstractions.Services;

public interface IAdminManagementService
{
    Task<int> SaveCompanyAsync(SaveCompanyRequest request, CancellationToken cancellationToken);
    Task<int> SaveIntegratorSettingsAsync(SaveIntegratorSettingsRequest request, CancellationToken cancellationToken);
    Task<IntegratorConnectionTestResult> TestIntegratorConnectionAsync(
        TestIntegratorConnectionRequest request,
        CancellationToken cancellationToken);
    Task SaveSmtpProfileAsync(SaveSmtpProfileRequest request, CancellationToken cancellationToken);
    Task SaveErpConnectionSettingsAsync(SaveErpConnectionSettingsRequest request, CancellationToken cancellationToken);
    Task DeleteIntegratorSettingsAsync(int id, CancellationToken cancellationToken);
    Task DeleteErpConnectionAsync(Guid id, CancellationToken cancellationToken);
    Task<ErpConnectionTestResult> TestErpConnectionAsync(
        TestErpConnectionRequest request,
        CancellationToken cancellationToken);
    Task<SmtpConnectionTestResult> TestSmtpConnectionAsync(
        TestSmtpConnectionRequest request,
        CancellationToken cancellationToken);
    Task CreateDepartmentAsync(CreateDepartmentRequest request, CancellationToken cancellationToken);
    Task UpdateDepartmentAsync(UpdateDepartmentRequest request, CancellationToken cancellationToken);
    Task DeleteDepartmentAsync(int id, CancellationToken cancellationToken);
    Task CreateUserAsync(CreateUserRequest request, CancellationToken cancellationToken);
    Task UpdateUserAsync(UpdateUserRequest request, CancellationToken cancellationToken);
}
