using CalibraHub.Domain.Enums;

namespace CalibraHub.Application.Contracts;

public sealed record CompanyParameterDto(
    int Id,
    int CompanyId,
    string FormCode,
    string ParamKey,
    string? ParamValue,
    CompanyParameterDataType DataType,
    DateTime? UpdatedAt,
    int? UpdatedBy);

public sealed record SetCompanyParameterRequest(
    string FormCode,
    string ParamKey,
    string? ParamValue,
    CompanyParameterDataType DataType);
