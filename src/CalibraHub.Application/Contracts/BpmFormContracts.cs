using CalibraHub.Domain.Enums;

namespace CalibraHub.Application.Contracts;

// ── Form Definition ───────────────────────────────────────────────────────────

public sealed record BpmFormFieldDto(
    int          Id,
    int          FormDefinitionId,
    string       Key,
    string       Label,
    BpmFieldType FieldType,
    bool         IsRequired,
    int          SortOrder,
    string?      OptionsJson,
    string?      Placeholder,
    string?      DefaultValue,
    int          LayoutRow     = 0,
    int          LayoutCol     = 0,
    int          LayoutColSpan = 12);

public sealed record BpmFormDefinitionDto(
    int     Id,
    string  Name,
    string  Code,
    string? Description,
    int?    WorkflowDefinitionId,
    string? WorkflowDefinitionName,
    bool    IsActive);

public sealed record BpmFormDefinitionDetailDto(
    BpmFormDefinitionDto          Definition,
    IReadOnlyList<BpmFormFieldDto> Fields);

public sealed record SaveBpmFormDefinitionRequest(
    int?    Id,
    string  Name,
    string? Description,
    int?    WorkflowDefinitionId,
    bool    IsActive);

public sealed record SaveBpmFormFieldRequest(
    int?         Id,
    int          FormDefinitionId,
    string       Key,
    string       Label,
    BpmFieldType FieldType,
    bool         IsRequired,
    int          SortOrder,
    string?      OptionsJson,
    string?      Placeholder,
    string?      DefaultValue,
    int          LayoutRow     = 0,
    int          LayoutCol     = 0,
    int          LayoutColSpan = 12);

// ── Form Submission ───────────────────────────────────────────────────────────

public sealed record BpmSubmissionValueDto(string FieldKey, string? Value);

public sealed record BpmFormSubmissionDto(
    int     Id,
    int     FormDefinitionId,
    string  FormName,
    string? SubmittedBy,
    DateTime SubmittedAt,
    string  Status,
    int?    WorkflowInstanceId);

public sealed record BpmFormSubmissionDetailDto(
    BpmFormSubmissionDto               Submission,
    BpmFormDefinitionDetailDto         Form,
    IReadOnlyList<BpmSubmissionValueDto> Values);

public sealed record SubmitBpmFormRequest(
    int                                    FormDefinitionId,
    IReadOnlyList<BpmSubmissionValueDto>   Values);
