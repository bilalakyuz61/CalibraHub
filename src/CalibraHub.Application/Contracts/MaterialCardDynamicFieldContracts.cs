using CalibraHub.Domain.Enums;

namespace CalibraHub.Application.Contracts;

public sealed record MaterialCardDynamicSchemaDto(
    IReadOnlyCollection<MaterialCardFieldGroupDto> Groups,
    IReadOnlyCollection<MaterialCardDynamicFieldDefinitionDto> Fields);

public sealed record MaterialCardFieldGroupDto(
    Guid Id,
    string GroupKey,
    string GroupLabel,
    int DisplayOrder,
    bool IsActive,
    string ScreenCode = "material_cards",
    string? LayerKey = null);

public sealed record MaterialCardFieldOptionDto(
    Guid Id,
    Guid FieldDefinitionId,
    string OptionKey,
    string OptionLabel,
    int SortOrder,
    bool IsActive);

public sealed record MaterialCardDynamicFieldDefinitionDto(
    Guid Id,
    Guid? GroupId,
    string FieldKey,
    string FieldLabel,
    MaterialCardDynamicFieldDataType DataType,
    bool IsVisible,
    bool IsRequired,
    string? DefaultValue,
    int DisplayOrder,
    int ColumnSpan,
    bool IsSystem,
    bool IsActive,
    IReadOnlyCollection<MaterialCardFieldOptionDto> Options,
    string ScreenCode = "material_cards",
    string? LayerKey = null);

public sealed record SaveMaterialCardFieldGroupRequest(
    Guid? GroupId,
    string GroupKey,
    string GroupLabel,
    int DisplayOrder,
    bool IsActive,
    string? ScreenCode = null,
    string? LayerKey = null);

public sealed record SaveMaterialCardFieldOptionRequest(
    Guid? OptionId,
    string OptionKey,
    string OptionLabel,
    int SortOrder,
    bool IsActive);

public sealed record SaveMaterialCardDynamicFieldRequest(
    Guid? FieldId,
    Guid? GroupId,
    string FieldKey,
    string FieldLabel,
    MaterialCardDynamicFieldDataType DataType,
    bool IsVisible,
    bool IsRequired,
    string? DefaultValue,
    int DisplayOrder,
    int ColumnSpan,
    bool IsActive,
    IReadOnlyCollection<SaveMaterialCardFieldOptionRequest> Options,
    string? ScreenCode = null,
    string? LayerKey = null);
