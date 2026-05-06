using System.Globalization;
using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Application.Abstractions.Services;
using CalibraHub.Application.Contracts;
using CalibraHub.Domain.Entities;
using CalibraHub.Domain.Enums;

namespace CalibraHub.Application.Services;

/// <summary>
/// Sirket parametre servisi — okuma/yazma facade.
/// Tipli getter'lar (GetIntAsync, GetBoolAsync, ...) ParamValue string'ini parse eder.
/// </summary>
public sealed class CompanyParameterService : ICompanyParameterService
{
    private readonly ICompanyParameterRepository _repo;

    public CompanyParameterService(ICompanyParameterRepository repo) => _repo = repo;

    public async Task<IReadOnlyCollection<CompanyParameterDto>> ListAsync(string? formCode, CancellationToken ct)
    {
        var entities = string.IsNullOrWhiteSpace(formCode)
            ? await _repo.GetAllAsync(ct)
            : await _repo.GetByFormAsync(formCode!.Trim(), ct);
        return entities.Select(Map).ToArray();
    }

    public async Task<string?> GetStringAsync(string formCode, string paramKey, CancellationToken ct)
    {
        var p = await _repo.GetAsync(formCode, paramKey, ct);
        return p?.ParamValue;
    }

    public async Task<int?> GetIntAsync(string formCode, string paramKey, CancellationToken ct)
    {
        var raw = await GetStringAsync(formCode, paramKey, ct);
        return int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : null;
    }

    public async Task<bool?> GetBoolAsync(string formCode, string paramKey, CancellationToken ct)
    {
        var raw = await GetStringAsync(formCode, paramKey, ct);
        if (string.IsNullOrWhiteSpace(raw)) return null;
        if (bool.TryParse(raw, out var b)) return b;
        if (raw == "1" || raw.Equals("true", StringComparison.OrdinalIgnoreCase)) return true;
        if (raw == "0" || raw.Equals("false", StringComparison.OrdinalIgnoreCase)) return false;
        return null;
    }

    public async Task<DateTime?> GetDateAsync(string formCode, string paramKey, CancellationToken ct)
    {
        var raw = await GetStringAsync(formCode, paramKey, ct);
        return DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var d) ? d : null;
    }

    public Task SetAsync(SetCompanyParameterRequest request, CancellationToken ct)
    {
        ValidateKey(request.FormCode, nameof(request.FormCode));
        ValidateKey(request.ParamKey, nameof(request.ParamKey));
        return _repo.UpsertAsync(request.FormCode.Trim(), request.ParamKey.Trim(), request.ParamValue, request.DataType, null, ct);
    }

    public Task DeleteAsync(string formCode, string paramKey, CancellationToken ct)
    {
        ValidateKey(formCode, nameof(formCode));
        ValidateKey(paramKey, nameof(paramKey));
        return _repo.DeleteAsync(formCode.Trim(), paramKey.Trim(), ct);
    }

    private static void ValidateKey(string value, string name)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException($"{name} bos olamaz.", name);
    }

    private static CompanyParameterDto Map(CompanyParameter e) =>
        new(e.Id, e.CompanyId, e.FormCode, e.ParamKey, e.ParamValue, e.DataType, e.UpdatedAt, e.UpdatedBy);
}
