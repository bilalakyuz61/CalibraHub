using CalibraHub.Application.Abstractions.Services;
using CalibraHub.Application.Security;
using CalibraHub.Domain.Entities;
using CalibraHub.Domain.Enums;

namespace CalibraHub.Persistence.Repositories;

public sealed class InMemoryDevelopmentBootstrapper
{
    private const int DefaultCompanyId = 1;
    private const int DefaultDepartmentId = 1;
    private const int DefaultAdminUserId = 100;
    private const string DefaultPassword = "12345678";

    private readonly InMemoryDataStore _dataStore;
    private readonly IPasswordHashService _passwordHashService;

    public InMemoryDevelopmentBootstrapper(
        InMemoryDataStore dataStore,
        IPasswordHashService passwordHashService)
    {
        _dataStore = dataStore;
        _passwordHashService = passwordHashService;
    }

    public Task SeedAsync(CancellationToken cancellationToken)
    {
        var company = EnsureDefaultCompany();
        var department = EnsureDefaultDepartment(company.Id);

        foreach (var user in _dataStore.Users.Values.Where(x => string.IsNullOrWhiteSpace(x.PasswordHash)))
        {
            user.SetPasswordHash(_passwordHashService.HashPassword(DefaultPassword));
        }

        if (_dataStore.Users.Values.All(x =>
                !string.Equals(x.Email, "admin@calibra.local", StringComparison.OrdinalIgnoreCase)))
        {
            var adminUser = new UserProfile
            {
                Id = DefaultAdminUserId,
                CompanyId = company.Id,
                FullName = "Sistem Admin",
                Email = "admin@calibra.local",
                EmployeeCode = "ADM-001",
                DepartmentId = department.Id,
                Role = UserRole.SystemAdmin,
                Permissions = UserAuthorizationCatalog.GetAllowedPermissions(UserRole.SystemAdmin).ToArray()
            };
            adminUser.SetPasswordHash(_passwordHashService.HashPassword(DefaultPassword));
            _dataStore.Users[adminUser.Id] = adminUser;
        }

        return Task.CompletedTask;
    }

    private Company EnsureDefaultCompany()
    {
        var existingCompany = _dataStore.Companies.Values.FirstOrDefault(x => x.IsActive);
        if (existingCompany is not null)
        {
            return existingCompany;
        }

        var company = new Company
        {
            Id = DefaultCompanyId,
            Name = "Calibra Merkez",
            Title = "Calibra Teknoloji A.S.",
            Address = "Istanbul",
            TaxOffice = "Beyoglu",
            TaxNumber = "1234567890",
            IsEDocumentApprovalEnabled = false
        };

        _dataStore.Companies[company.Id] = company;
        return company;
    }

    private Department EnsureDefaultDepartment(int companyId)
    {
        var existingDepartment = _dataStore.Departments.Values.FirstOrDefault(x => x.IsActive && x.CompanyId == companyId);
        if (existingDepartment is not null)
        {
            return existingDepartment;
        }

        var department = new Department
        {
            Id = DefaultDepartmentId,
            CompanyId = companyId,
            Name = "Finans"
        };

        _dataStore.Departments[department.Id] = department;
        return department;
    }
}
