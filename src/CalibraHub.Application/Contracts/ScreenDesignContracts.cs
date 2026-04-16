namespace CalibraHub.Application.Contracts;

public sealed record ScreenDesignScreenDto(
    string ScreenCode,
    string ScreenLabel,
    string GroupLabel,
    bool UsesMaterialCardSchema);

public sealed record ScreenDesignFieldDefinitionDto(
    string ItemKey,
    string ItemLabel,
    int DefaultColumnSpan,
    bool DefaultRequired);

public sealed record ScreenDesignTabDto(
    string TabKey,
    string TabLabel,
    int DisplayOrder,
    bool IsActive);

public sealed record ScreenDesignItemDto(
    string ItemKey,
    string ItemLabel,
    string TabKey,
    int DisplayOrder,
    int ColumnSpan,
    bool IsVisible,
    bool IsRequired);

public sealed record ScreenDesignLayoutDto(
    string ScreenCode,
    string ScreenLabel,
    IReadOnlyCollection<ScreenDesignTabDto> Tabs,
    IReadOnlyCollection<ScreenDesignItemDto> Items,
    IReadOnlyCollection<ScreenDesignFieldDefinitionDto> AvailableItems);

public sealed record SaveScreenDesignTabRequest(
    string TabKey,
    string TabLabel,
    int DisplayOrder,
    bool IsActive);

public sealed record SaveScreenDesignItemRequest(
    string ItemKey,
    string TabKey,
    int DisplayOrder,
    int ColumnSpan,
    bool IsVisible,
    bool IsRequired);

public sealed record SaveScreenDesignLayoutRequest(
    string ScreenCode,
    IReadOnlyCollection<SaveScreenDesignTabRequest> Tabs,
    IReadOnlyCollection<SaveScreenDesignItemRequest> Items);
