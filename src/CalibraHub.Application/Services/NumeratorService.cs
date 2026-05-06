using System.Text.RegularExpressions;
using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Application.Abstractions.Services;

namespace CalibraHub.Application.Services;

/// <summary>
/// Belge numarasi uretici — CompanyParameter'dan format mask'i okur, Numerator'dan sonraki degeri alir,
/// placeholder'lari ({yyyy}, {MM}, {seq:N}) replace eder.
/// </summary>
public sealed class NumeratorService : INumeratorService
{
    private static readonly Regex TokenRegex = new(@"\{([^{}]+)\}", RegexOptions.Compiled);

    private readonly INumeratorRepository _numerators;
    private readonly ICompanyParameterService _parameters;

    public NumeratorService(INumeratorRepository numerators, ICompanyParameterService parameters)
    {
        _numerators = numerators;
        _parameters = parameters;
    }

    public async Task<string> GetNextNumberAsync(string entityType, string formCode, string defaultMask, CancellationToken ct)
    {
        var mask = await _parameters.GetStringAsync(formCode, "NumeratorMask", ct);
        if (string.IsNullOrWhiteSpace(mask)) mask = defaultMask;

        var resetPolicy = await _parameters.GetStringAsync(formCode, "NumeratorResetPolicy", ct);
        if (string.IsNullOrWhiteSpace(resetPolicy)) resetPolicy = "Yearly";

        var nextValue = await _numerators.GetNextValueAsync(entityType, resetPolicy!, ct);
        return ApplyMask(mask!, nextValue, DateTime.Now);
    }

    internal static string ApplyMask(string mask, int seqValue, DateTime stamp)
    {
        return TokenRegex.Replace(mask, m =>
        {
            var token = m.Groups[1].Value.Trim();
            if (string.Equals(token, "yyyy", StringComparison.OrdinalIgnoreCase)) return stamp.Year.ToString("0000");
            if (string.Equals(token, "yy", StringComparison.OrdinalIgnoreCase)) return (stamp.Year % 100).ToString("00");
            if (string.Equals(token, "MM", StringComparison.Ordinal)) return stamp.Month.ToString("00");
            if (string.Equals(token, "dd", StringComparison.Ordinal)) return stamp.Day.ToString("00");
            if (string.Equals(token, "seq", StringComparison.OrdinalIgnoreCase)) return seqValue.ToString();
            if (token.StartsWith("seq:", StringComparison.OrdinalIgnoreCase) &&
                int.TryParse(token[4..], out var width) && width > 0)
            {
                return seqValue.ToString(new string('0', width));
            }
            return m.Value;
        });
    }
}
