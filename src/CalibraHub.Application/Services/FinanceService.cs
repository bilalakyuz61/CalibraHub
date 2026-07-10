using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Application.Abstractions.Services;
using CalibraHub.Application.Auditing;
using CalibraHub.Application.Contracts;
using CalibraHub.Domain.Entities;

namespace CalibraHub.Application.Services;

public sealed class FinanceService : IFinanceService
{
    private readonly IFinanceRepository _repo;
    private readonly IAuditTrailService? _audit;

    public FinanceService(IFinanceRepository repo, IAuditTrailService? audit = null)
    {
        _repo = repo;
        _audit = audit;
    }

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

    public async Task<ContactDto?> GetContactByCodeAsync(string code, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(code)) return null;
        var entity = await _repo.GetContactByCodeAsync(code.Trim(), cancellationToken);
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
                AccountType = (Domain.Enums.ContactType)request.AccountType,
                AccountCode = code,
                AccountTitle = request.AccountTitle?.Trim() ?? string.Empty,
                TaxNumber = NullIfEmpty(request.TaxNumber),
                IdentityNumber = NullIfEmpty(request.IdentityNumber),
                TaxOffice = NullIfEmpty(request.TaxOffice),
                Phone = NullIfEmpty(request.Phone),
                Mobile = NullIfEmpty(request.Mobile),
                Email = NullIfEmpty(request.Email),
                Website = NullIfEmpty(request.Website),
                Address = NullIfEmpty(request.Address),
                PostalCode = NullIfEmpty(request.PostalCode),
                City = NullIfEmpty(request.City),
                District = NullIfEmpty(request.District),
                Neighborhood = NullIfEmpty(request.Neighborhood),
                CountryCode = NullIfEmpty(request.CountryCode)?.ToUpperInvariant(),
                ContactPerson = NullIfEmpty(request.ContactPerson),
                IsActive = request.IsActive,
                PriceGroupId = request.PriceGroupId > 0 ? request.PriceGroupId : null,
                SalesRepresentativeId = request.SalesRepresentativeId > 0 ? request.SalesRepresentativeId : null,
                ContactGroupId = request.ContactGroupId > 0 ? request.ContactGroupId : null,
                WaPhone = NullIfEmpty(request.WaPhone),
                WaName = NullIfEmpty(request.WaName),
                CreatedAt = existing.CreatedAt
            };
            await _repo.UpdateContactAsync(updated, cancellationToken);

            // İşlem logu — yalnızca değişen alanlar (CompanyId yeni nesnede set edilmediği için hariç)
            if (_audit is not null)
            {
                try
                {
                    var changes = AuditDiff.Compute(existing, updated, "Contact", ignore: new[] { "CompanyId" });
                    _audit.LogChanges("Contact", existing.Id, updated.AccountTitle, changes);
                }
                catch { /* audit yazımı kaydı asla bozmaz */ }
            }
            return (true, null, ToDto(updated));
        }
        else
        {
            var entity = new Contact
            {
                AccountType = (Domain.Enums.ContactType)request.AccountType,
                AccountCode = code,
                AccountTitle = request.AccountTitle?.Trim() ?? string.Empty,
                TaxNumber = NullIfEmpty(request.TaxNumber),
                IdentityNumber = NullIfEmpty(request.IdentityNumber),
                TaxOffice = NullIfEmpty(request.TaxOffice),
                Phone = NullIfEmpty(request.Phone),
                Mobile = NullIfEmpty(request.Mobile),
                Email = NullIfEmpty(request.Email),
                Website = NullIfEmpty(request.Website),
                Address = NullIfEmpty(request.Address),
                PostalCode = NullIfEmpty(request.PostalCode),
                City = NullIfEmpty(request.City),
                District = NullIfEmpty(request.District),
                Neighborhood = NullIfEmpty(request.Neighborhood),
                CountryCode = NullIfEmpty(request.CountryCode)?.ToUpperInvariant(),
                ContactPerson = NullIfEmpty(request.ContactPerson),
                IsActive = request.IsActive,
                PriceGroupId = request.PriceGroupId > 0 ? request.PriceGroupId : null,
                SalesRepresentativeId = request.SalesRepresentativeId > 0 ? request.SalesRepresentativeId : null,
                ContactGroupId = request.ContactGroupId > 0 ? request.ContactGroupId : null,
                WaPhone = NullIfEmpty(request.WaPhone),
                WaName = NullIfEmpty(request.WaName),
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
                Neighborhood = entity.Neighborhood,
                CountryCode = entity.CountryCode,
                Mobile = entity.Mobile,
                Website = entity.Website,
                PostalCode = entity.PostalCode,
                ContactPerson = entity.ContactPerson,
                IsActive = entity.IsActive,
                PriceGroupId = entity.PriceGroupId,
                SalesRepresentativeId = entity.SalesRepresentativeId,
                ContactGroupId = entity.ContactGroupId,
                WaPhone = entity.WaPhone,
                WaName = entity.WaName,
                CreatedAt = entity.CreatedAt
            };

            // İşlem logu — yeni cari (ilk değer dökümüyle)
            _audit?.LogInsert("Contact", newId, entity.AccountTitle, detail: code,
                snapshot: created, snapshotIgnore: ["CompanyId"]);
            return (true, null, ToDto(created));
        }
    }

    public async Task<(bool Success, string? Error)> DeleteContactAsync(int id, CancellationToken cancellationToken)
    {
        var existing = await _repo.GetContactByIdAsync(id, cancellationToken);
        if (existing is null)
            return (false, "Kayıt bulunamadı.");

        await _repo.DeleteContactAsync(id, cancellationToken);

        // İşlem logu — cari silme
        _audit?.LogDelete("Contact", id, existing.AccountTitle, detail: existing.AccountCode);
        return (true, null);
    }

    public async Task<(bool Success, string? Error)> SetContactPriceGroupAsync(int contactId, int? priceGroupId, CancellationToken cancellationToken)
    {
        if (contactId <= 0) return (false, "Geçersiz cari.");
        var existing = await _repo.GetContactByIdAsync(contactId, cancellationToken);
        if (existing is null) return (false, "Cari bulunamadı.");
        await _repo.UpdateContactPriceGroupAsync(contactId, priceGroupId, cancellationToken);

        // İşlem logu — yalnızca fiyat grubu değiştiyse
        if (_audit is not null && existing.PriceGroupId != priceGroupId)
        {
            _audit.LogChanges("Contact", contactId, existing.AccountTitle,
                [new AuditFieldChange("PriceGroupId", "Fiyat Grubu",
                    AuditDiff.Normalize(existing.PriceGroupId), AuditDiff.Normalize(priceGroupId))]);
        }
        return (true, null);
    }

    public async Task<IReadOnlyCollection<ContactDto>> GetContactsByPriceGroupAsync(int priceGroupId, CancellationToken cancellationToken)
    {
        if (priceGroupId <= 0) return Array.Empty<ContactDto>();
        var contacts = await _repo.GetContactsByPriceGroupAsync(priceGroupId, cancellationToken);
        return contacts.Select(ToDto).ToArray();
    }

    private static ContactDto ToDto(Contact a) => new(
        a.Id, (byte)a.AccountType, a.AccountCode, a.AccountTitle,
        a.TaxNumber, a.IdentityNumber, a.TaxOffice, a.Phone, a.Email, a.Address, a.City, a.District,
        a.IsActive, a.PriceGroupId, a.CreatedAt, a.CountryCode,
        a.Mobile, a.Website, a.PostalCode, a.ContactPerson, a.Neighborhood, a.SalesRepresentativeId,
        a.WaPhone, a.WaName, a.ContactGroupId);

    private static string? NullIfEmpty(string? s) =>
        string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
