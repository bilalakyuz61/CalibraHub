using System.ComponentModel;
using CalibraHub.Domain.Enums;

namespace CalibraHub.Domain.Entities;

[Description("Sirket bazli form/modul parametreleri. (CompanyId, FormCode, ParamKey) UNIQUE — her satir tek bir parametre. FormCode='GENERAL' sirket geneli icin.")]
public sealed class CompanyParameter
{
    public int Id { get; init; }
    public int CompanyId { get; init; }
    public required string FormCode { get; init; }
    public required string ParamKey { get; init; }
    public string? ParamValue { get; init; }
    public CompanyParameterDataType DataType { get; init; } = CompanyParameterDataType.String;
    public DateTime? UpdatedAt { get; init; }
    public int? UpdatedBy { get; init; }
}
