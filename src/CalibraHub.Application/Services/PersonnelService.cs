using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Application.Abstractions.Services;
using CalibraHub.Application.Contracts;
using CalibraHub.Domain.Entities;

namespace CalibraHub.Application.Services;

public sealed class PersonnelService : IPersonnelService
{
    private readonly IPersonnelRepository _repo;

    public PersonnelService(IPersonnelRepository repo) => _repo = repo;

    public Task<IReadOnlyCollection<PersonnelDto>> ListAsync(bool includeInactive, bool onlyOperators, CancellationToken ct)
        => _repo.ListAsync(includeInactive, onlyOperators, ct);

    public Task<PersonnelDto?> GetAsync(int id, CancellationToken ct) => _repo.GetAsync(id, ct);

    public Task<int> SaveAsync(SavePersonnelRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.Code))
            throw new ArgumentException("Personnel kodu zorunlu.", nameof(req.Code));
        if (string.IsNullOrWhiteSpace(req.FullName))
            throw new ArgumentException("Tam ad zorunlu.", nameof(req.FullName));
        if (!string.IsNullOrWhiteSpace(req.PinCode) && req.PinCode.Trim().Length is < 4 or > 10)
            throw new ArgumentException("PIN 4-10 hane olmalı.", nameof(req.PinCode));

        var entity = new Personnel
        {
            Id = req.Id,
            Code = req.Code.Trim(),
            FullName = req.FullName.Trim(),
            Title = string.IsNullOrWhiteSpace(req.Title) ? null : req.Title.Trim(),
            Department = string.IsNullOrWhiteSpace(req.Department) ? null : req.Department.Trim(),
            PinCode = string.IsNullOrWhiteSpace(req.PinCode) ? null : req.PinCode.Trim(),
            CardNo = string.IsNullOrWhiteSpace(req.CardNo) ? null : req.CardNo.Trim(),
            IsProductionOperator = req.IsProductionOperator,
            IsActive = req.IsActive,
            UserId = req.UserId,
            Phone = string.IsNullOrWhiteSpace(req.Phone) ? null : req.Phone.Trim(),
            Email = string.IsNullOrWhiteSpace(req.Email) ? null : req.Email.Trim(),
            Notes = string.IsNullOrWhiteSpace(req.Notes) ? null : req.Notes.Trim(),
        };
        return _repo.SaveAsync(entity, ct);
    }

    public Task DeleteAsync(int id, CancellationToken ct) => _repo.DeleteAsync(id, ct);

    public Task<PersonnelDto?> GetByPinOrCardAsync(string? pinCode, string? cardNo, CancellationToken ct)
        => _repo.GetByPinOrCardAsync(pinCode, cardNo, ct);
}
