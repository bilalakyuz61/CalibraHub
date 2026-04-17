using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Application.Abstractions.Services;
using CalibraHub.Application.Contracts;
using CalibraHub.Domain.Entities;

namespace CalibraHub.Application.Services;

public sealed class FinanceService : IFinanceService
{
    private readonly IFinanceRepository _repo;

    public FinanceService(IFinanceRepository repo) => _repo = repo;

    public async Task<IReadOnlyCollection<ContactDto>> GetContactsAsync(
        byte? accountType, string? search, CancellationToken cancellationToken)
    {
        var accounts = await _repo.GetContactsAsync(accountType, search, cancellationToken);
        return accounts.Select(ToDto).ToList();
    }

    public async Task<(IReadOnlyCollection<ContactDto> Items, int TotalCount)> GetContactsPagedAsync(
        byte? accountType, string? search, int offset, int pageSize, CancellationToken cancellationToken)
    {
        var (accounts, totalCount) = await _repo.GetContactsPagedAsync(accountType, search, offset, pageSize, cancellationToken);
        return (accounts.Select(ToDto).ToList(), totalCount);
    }

    public async Task<ContactDto?> GetContactByIdAsync(int id, CancellationToken cancellationToken)
    {
        var entity = await _repo.GetContactByIdAsync(id, cancellationToken);
        return entity is null ? null : ToDto(entity);
    }

    public async Task<(bool Success, string? Error, ContactDto? Account)> UpsertContactAsync(
        SaveContactRequest request, CancellationToken cancellationToken)
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
            var existing = await _repo.GetContactByIdAsync(request.Id.Value, cancellationToken);
            if (existing is null)
                return (false, "Kayıt bulunamadı.", null);

            var updated = new Contact
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
                District = NullIfEmpty(request.District),
                IsActive = request.IsActive,
                PriceGroupId = request.PriceGroupId > 0 ? request.PriceGroupId : null,
                CreatedAt = existing.CreatedAt
            };
            await _repo.UpdateContactAsync(updated, cancellationToken);
            return (true, null, ToDto(updated));
        }
        else
        {
            var entity = new Contact
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
                District = NullIfEmpty(request.District),
                IsActive = request.IsActive,
                PriceGroupId = request.PriceGroupId > 0 ? request.PriceGroupId : null,
                CreatedAt = DateTime.Now
            };
            var newId = await _repo.AddContactAsync(entity, cancellationToken);
            var created = new Contact
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
                District = entity.District,
                IsActive = entity.IsActive,
                PriceGroupId = entity.PriceGroupId,
                CreatedAt = entity.CreatedAt
            };
            return (true, null, ToDto(created));
        }
    }

    public async Task<(bool Success, string? Error)> DeleteContactAsync(int id, CancellationToken cancellationToken)
    {
        var existing = await _repo.GetContactByIdAsync(id, cancellationToken);
        if (existing is null)
            return (false, "Kayıt bulunamadı.");

        await _repo.DeleteContactAsync(id, cancellationToken);
        return (true, null);
    }

    private static ContactDto ToDto(Contact a) => new(
        a.Id, a.AccountType, a.AccountCode, a.AccountTitle,
        a.TaxNumber, a.IdentityNumber, a.TaxOffice, a.Phone, a.Email, a.Address, a.City, a.District,
        a.IsActive, a.PriceGroupId, a.CreatedAt);

    private static string? NullIfEmpty(string? s) =>
        string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
