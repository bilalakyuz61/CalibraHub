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

        // Alan doğrulaması (2026-08-24 güvenlik denetimi, ORTA).
        // Proje FluentValidation kuralları (SaveContactRequestValidator) yazılmış ama
        // hiçbir yerden ÇAĞRILMIYORDU — yani cari e-postası, VKN ve TCKN hiçbir katmanda
        // doğrulanmıyor, "abc" gibi bir e-posta veya 3 haneli VKN sessizce kaydediliyordu.
        // Doğrulama burada (servis girişinde) yapılır: hem web hem içe aktarım hem AI
        // aracı aynı kapıdan geçer.
        var fieldError = ValidateContactFields(request);
        if (fieldError is not null)
            return (false, fieldError, null);

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

    /// <summary>
    /// Kullanıcının ELLE girdiği alanların biçim doğrulaması (e-posta, VKN, TCKN, telefon).
    /// Boş bırakılan alan doğrulanmaz — hepsi opsiyoneldir; dolduysa doğru olmalıdır.
    /// Hata mesajı kullanıcıya gösterilir, bu yüzden hangi alanın neden reddedildiğini söyler.
    /// </summary>
    private static string? ValidateContactFields(SaveContactRequest request)
    {
        var title = (request.AccountTitle ?? string.Empty).Trim();
        if (title.Length == 0) return "Cari unvanı zorunludur.";
        if (title.Length > 200) return "Cari unvanı en fazla 200 karakter olabilir.";

        var email = (request.Email ?? string.Empty).Trim();
        if (email.Length > 0)
        {
            // Basit ama kesin kural: tek '@', öncesi ve sonrası dolu, alan adında nokta var.
            var at = email.IndexOf('@');
            var lastAt = email.LastIndexOf('@');
            var domain = at >= 0 ? email[(at + 1)..] : string.Empty;
            if (at <= 0 || at != lastAt || domain.Length < 3 || !domain.Contains('.')
                || domain.StartsWith('.') || domain.EndsWith('.') || email.Contains(' '))
                return "Geçerli bir e-posta adresi giriniz.";
        }

        var tax = (request.TaxNumber ?? string.Empty).Trim();
        if (tax.Length > 0 && !(tax.Length == 10 && tax.All(char.IsAsciiDigit)))
            return "Vergi numarası 10 haneli rakam olmalıdır.";

        var tckn = (request.IdentityNumber ?? string.Empty).Trim();
        if (tckn.Length > 0 && !(tckn.Length == 11 && tckn.All(char.IsAsciiDigit)))
            return "TC kimlik numarası 11 haneli rakam olmalıdır.";

        var wa = (request.WaPhone ?? string.Empty).Trim();
        if (wa.Length > 0 && wa.Count(char.IsAsciiDigit) < 10)
            return "WhatsApp numarası en az 10 hane olmalıdır (ülke kodu dahil).";

        return null;
    }

    private static string? NullIfEmpty(string? s) =>
        string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
