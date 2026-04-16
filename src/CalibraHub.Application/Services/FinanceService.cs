using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Application.Abstractions.Services;
using CalibraHub.Application.Contracts;
using CalibraHub.Domain.Entities;

namespace CalibraHub.Application.Services;

public sealed class FinanceService : IFinanceService
{
    private readonly IFinanceRepository _repo;

    public FinanceService(IFinanceRepository repo) => _repo = repo;

    public async Task<IReadOnlyCollection<ContactAccountDto>> GetContactAccountsAsync(
        byte? accountType, string? search, CancellationToken cancellationToken)
    {
        var accounts = await _repo.GetContactAccountsAsync(accountType, search, cancellationToken);
        return accounts.Select(ToDto).ToList();
    }

    public async Task<(IReadOnlyCollection<ContactAccountDto> Items, int TotalCount)> GetContactAccountsPagedAsync(
        byte? accountType, string? search, int offset, int pageSize, CancellationToken cancellationToken)
    {
        var (accounts, totalCount) = await _repo.GetContactAccountsPagedAsync(accountType, search, offset, pageSize, cancellationToken);
        return (accounts.Select(ToDto).ToList(), totalCount);
    }

    public async Task<ContactAccountDto?> GetContactAccountByIdAsync(int id, CancellationToken cancellationToken)
    {
        var entity = await _repo.GetContactAccountByIdAsync(id, cancellationToken);
        return entity is null ? null : ToDto(entity);
    }

    public async Task<(bool Success, string? Error, ContactAccountDto? Account)> UpsertContactAccountAsync(
        SaveContactAccountRequest request, CancellationToken cancellationToken)
    {
        var code = (request.AccountCode ?? string.Empty).Trim().ToUpperInvariant();
        if (string.IsNullOrEmpty(code))
            return (false, "Hesap kodu zorunludur.", null);

        if (request.AccountType is < 1 or > 3)
            return (false, "Geçersiz hesap tipi.", null);

        if (await _repo.CodeExistsAsync(code, request.Id, cancellationToken))
            return (false, $"'{code}' kodu zaten kullanılıyor.", null);

        if (request.Id is > 0)
        {
            var existing = await _repo.GetContactAccountByIdAsync(request.Id.Value, cancellationToken);
            if (existing is null)
                return (false, "Kayıt bulunamadı.", null);

            var updated = new ContactAccount
            {
                Id = existing.Id,
                AccountType = request.AccountType,
                AccountCode = code,
                AccountTitle = request.AccountTitle?.Trim() ?? string.Empty,
                TaxNumber = NullIfEmpty(request.TaxNumber),
                IdentityNumber = NullIfEmpty(request.IdentityNumber),
                TaxOffice = NullIfEmpty(request.TaxOffice),
                Phone = NullIfEmpty(request.Phone),
                Email = NullIfEmpty(request.Email),
                Address = NullIfEmpty(request.Address),
                City = NullIfEmpty(request.City),
                IsActive = request.IsActive,
                PriceGroupId = request.PriceGroupId > 0 ? request.PriceGroupId : null,
                CreatedAt = existing.CreatedAt
            };
            await _repo.UpdateContactAccountAsync(updated, cancellationToken);
            return (true, null, ToDto(updated));
        }
        else
        {
            var entity = new ContactAccount
            {
                AccountType = request.AccountType,
                AccountCode = code,
                AccountTitle = request.AccountTitle?.Trim() ?? string.Empty,
                TaxNumber = NullIfEmpty(request.TaxNumber),
                IdentityNumber = NullIfEmpty(request.IdentityNumber),
                TaxOffice = NullIfEmpty(request.TaxOffice),
                Phone = NullIfEmpty(request.Phone),
                Email = NullIfEmpty(request.Email),
                Address = NullIfEmpty(request.Address),
                City = NullIfEmpty(request.City),
                IsActive = request.IsActive,
                PriceGroupId = request.PriceGroupId > 0 ? request.PriceGroupId : null,
                CreatedAt = DateTime.Now
            };
            var newId = await _repo.AddContactAccountAsync(entity, cancellationToken);
            var created = new ContactAccount
            {
                Id = newId,
                AccountType = entity.AccountType,
                AccountCode = entity.AccountCode,
                AccountTitle = entity.AccountTitle,
                TaxNumber = entity.TaxNumber,
                IdentityNumber = entity.IdentityNumber,
                TaxOffice = entity.TaxOffice,
                Phone = entity.Phone,
                Email = entity.Email,
                Address = entity.Address,
                City = entity.City,
                IsActive = entity.IsActive,
                PriceGroupId = entity.PriceGroupId,
                CreatedAt = entity.CreatedAt
            };
            return (true, null, ToDto(created));
        }
    }

    public async Task<(bool Success, string? Error)> DeleteContactAccountAsync(int id, CancellationToken cancellationToken)
    {
        var existing = await _repo.GetContactAccountByIdAsync(id, cancellationToken);
        if (existing is null)
            return (false, "Kayıt bulunamadı.");

        await _repo.DeleteContactAccountAsync(id, cancellationToken);
        return (true, null);
    }

    private static ContactAccountDto ToDto(ContactAccount a) => new(
        a.Id, a.AccountType, a.AccountCode, a.AccountTitle,
        a.TaxNumber, a.IdentityNumber, a.TaxOffice, a.Phone, a.Email, a.Address, a.City,
        a.IsActive, a.PriceGroupId, a.CreatedAt);

    private static string? NullIfEmpty(string? s) =>
        string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
