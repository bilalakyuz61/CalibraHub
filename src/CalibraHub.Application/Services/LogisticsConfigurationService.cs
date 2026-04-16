using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Application.Abstractions.Services;
using CalibraHub.Application.Contracts;
using CalibraHub.Domain.Entities;
using CalibraHub.Domain.Enums;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace CalibraHub.Application.Services;

public sealed class LogisticsConfigurationService : ILogisticsConfigurationService
{
    private const string ProductValueSeparator = " || ";
    private static readonly Regex PropertyCodeRegex = new("^[A-Za-z0-9]{8}$", RegexOptions.Compiled);
    private static readonly Regex MetadataKeyRegex = new("^[a-z][a-z0-9_]{1,59}$", RegexOptions.Compiled);
    private static readonly HashSet<string> AllowedMaterialTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "Stockable",
        "Consumable",
        "Service"
    };
    private static readonly HashSet<string> AllowedTrackingTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "None",
        "Lot",
        "Serial"
    };
    private static readonly HashSet<string> AllowedLocationTypeCodes = new(StringComparer.OrdinalIgnoreCase)
    {
        "FACTORY",
        "SECTION",
        "SHELF",
        "BIN"
    };

    private readonly ILogisticsConfigurationRepository _repository;

    public LogisticsConfigurationService(ILogisticsConfigurationRepository repository)
    {
        _repository = repository;
    }

    public async Task<MaterialCardDynamicSchemaDto> GetMaterialCardDynamicSchemaAsync(CancellationToken cancellationToken)
    {
        var (groups, fields, options) = await LoadMaterialCardDynamicSchemaAsync(cancellationToken);
        var optionsByField = options
            .GroupBy(x => x.FieldDefinitionId)
            .ToDictionary(
                x => x.Key,
                x => (IReadOnlyCollection<MaterialCardFieldOptionDto>)x
                    .OrderBy(y => y.SortOrder)
                    .ThenBy(y => y.OptionLabel, StringComparer.OrdinalIgnoreCase)
                    .Select(y => new MaterialCardFieldOptionDto(
                        y.Id,
                        y.FieldDefinitionId,
                        y.OptionKey,
                        y.OptionLabel,
                        y.SortOrder,
                        y.IsActive))
                    .ToArray());

        return new MaterialCardDynamicSchemaDto(
            groups
                .OrderBy(x => x.DisplayOrder)
                .ThenBy(x => x.GroupLabel, StringComparer.OrdinalIgnoreCase)
                .Select(x => new MaterialCardFieldGroupDto(
                    x.Id,
                    x.GroupKey,
                    x.GroupLabel,
                    x.DisplayOrder,
                    x.IsActive,
                    NormalizeScreenCode(x.ScreenCode),
                    NormalizeLayerKey(x.LayerKey)))
                .ToArray(),
            fields
                .OrderBy(x => x.DisplayOrder)
                .ThenBy(x => x.FieldLabel, StringComparer.OrdinalIgnoreCase)
                .Select(x => new MaterialCardDynamicFieldDefinitionDto(
                    x.Id,
                    x.GroupId,
                    x.FieldKey,
                    x.FieldLabel,
                    x.DataType,
                    x.IsVisible,
                    x.IsRequired,
                    x.DefaultValue,
                    x.DisplayOrder,
                    x.ColumnSpan,
                    x.IsSystem,
                    x.IsActive,
                    optionsByField.GetValueOrDefault(x.Id) ?? Array.Empty<MaterialCardFieldOptionDto>(),
                    NormalizeScreenCode(x.ScreenCode),
                    NormalizeLayerKey(x.LayerKey)))
                .ToArray());
    }

    public async Task SaveMaterialCardFieldGroupAsync(
        SaveMaterialCardFieldGroupRequest request,
        CancellationToken cancellationToken)
    {
        var groupKey = NormalizeMetadataKey(request.GroupKey, "Grup teknik adi");
        var groupLabel = NormalizeRequiredField(request.GroupLabel, 120, "Grup etiketi");
        var displayOrder = NormalizeSortOrder(request.DisplayOrder);

        var existingGroups = await _repository.GetMaterialCardFieldGroupsAsync(cancellationToken);
        var existing = request.GroupId.HasValue
            ? existingGroups.FirstOrDefault(x => x.Id == request.GroupId.Value)
            : null;

        if (existingGroups.Any(x =>
                x.Id != existing?.Id &&
                string.Equals(x.GroupKey, groupKey, StringComparison.OrdinalIgnoreCase)))
        {
            throw new ArgumentException("Ayni teknik ada sahip grup zaten mevcut.");
        }

        // Screen code normalize: yeni grup ise request.ScreenCode veya
        // default "material_cards"; mevcut grubun ScreenCode'u korunur.
        var normalizedScreenCode = NormalizeScreenCode(
            existing?.ScreenCode ?? request.ScreenCode ?? "material_cards");

        var group = new MaterialCardFieldGroup
        {
            Id = existing?.Id ?? request.GroupId ?? Guid.NewGuid(),
            ScreenCode = normalizedScreenCode,
            LayerKey = NormalizeLayerKey(existing?.LayerKey ?? request.LayerKey),
            GroupKey = groupKey,
            GroupLabel = groupLabel,
            DisplayOrder = displayOrder,
            CreatedAt = existing?.CreatedAt ?? DateTime.Now
        };

        group.SetActive(request.IsActive);
        await _repository.UpsertMaterialCardFieldGroupAsync(group, cancellationToken);
    }

    /// <summary>
    /// Eski "MaterialCards" (CamelCase) ScreenCode kayitlari ile yeni snake_case
    /// uretim kodlari arasindaki uyumu saglar. Her iki format da ayni anlama
    /// gelir; kanonik form snake_case (material_cards, contact_accounts, sales_quotes).
    /// </summary>
    private static string NormalizeScreenCode(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "material_cards";
        var lower = raw.Trim().ToLowerInvariant();
        return lower switch
        {
            "materialcards"   => "material_cards",
            "contactaccounts" => "contact_accounts",
            "salesquotes"     => "sales_quotes",
            _ => lower,
        };
    }

    /// <summary>
    /// Multi-layer ekranlar icin layer anahtari normalize eder.
    /// "default", null, "" → null (tek katman).
    /// </summary>
    private static string? NormalizeLayerKey(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var lower = raw.Trim().ToLowerInvariant();
        return lower == "default" ? null : lower;
    }

    public async Task SaveMaterialCardDynamicFieldAsync(
        SaveMaterialCardDynamicFieldRequest request,
        CancellationToken cancellationToken)
    {
        var (groups, fields, options) = await LoadMaterialCardDynamicSchemaAsync(cancellationToken);
        var existing = request.FieldId.HasValue
            ? fields.FirstOrDefault(x => x.Id == request.FieldId.Value)
            : null;

        if (request.GroupId.HasValue && groups.All(x => x.Id != request.GroupId.Value))
        {
            throw new ArgumentException("Secilen saha grubu bulunamadi.");
        }

        var fieldKey = existing?.IsSystem == true
            ? existing.FieldKey
            : NormalizeMetadataKey(request.FieldKey, "Veritabani saha adi");
        var fieldLabel = NormalizeRequiredField(request.FieldLabel, 120, "Gorunecek ad");
        var displayOrder = NormalizeSortOrder(request.DisplayOrder);
        var columnSpan = NormalizeMaterialCardColumnSpan(request.ColumnSpan);
        var defaultValue = NormalizeOptionalField(request.DefaultValue, 500);

        // Cakisma kontrolu sadece ayni (screen, layer) scope'unda yapilir;
        // farkli screen veya farkli layer'larda ayni fieldKey'e izin verilir.
        // Ornek: sales_quotes/header altinda "price", sales_quotes/line altinda
        // "price" ayri ayri tanimlanabilir.
        var conflictScreenCode = NormalizeScreenCode(
            existing?.ScreenCode ?? request.ScreenCode ?? "material_cards");
        var conflictLayerKey = NormalizeLayerKey(existing?.LayerKey ?? request.LayerKey);
        if (fields.Any(x =>
                x.Id != existing?.Id &&
                string.Equals(NormalizeScreenCode(x.ScreenCode), conflictScreenCode, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(NormalizeLayerKey(x.LayerKey), conflictLayerKey, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(x.FieldKey, fieldKey, StringComparison.OrdinalIgnoreCase)))
        {
            throw new ArgumentException("Ayni teknik ada sahip saha zaten mevcut.");
        }

        var effectiveDataType = existing?.IsSystem == true
            ? existing.DataType
            : request.DataType;

        if (existing is not null &&
            (!string.Equals(existing.FieldKey, fieldKey, StringComparison.OrdinalIgnoreCase) || existing.DataType != effectiveDataType))
        {
            throw new ArgumentException("Veri girilmis bir sahanin teknik adi veya veri tipi degistirilemez.");
        }

        var fieldId = existing?.Id ?? request.FieldId ?? Guid.NewGuid();
        var normalizedOptions = NormalizeMaterialCardFieldOptions(request.Options, effectiveDataType, fieldId);
        var existingOptionsById = options
            .Where(x => existing is not null && x.FieldDefinitionId == existing.Id)
            .ToDictionary(x => x.Id);

        foreach (var existingOption in existingOptionsById.Values.Where(x => x.IsActive))
        {
            if (!normalizedOptions.TryGetValue(existingOption.Id, out var requestedOption))
            {
                continue;
            }
        }

        // Screen code normalize: mevcut field'in ScreenCode'u korunur;
        // yeni field ise request.ScreenCode (React tarafindan gonderilen)
        // kullanilir, yoksa default "material_cards".
        var normalizedFieldScreenCode = NormalizeScreenCode(
            existing?.ScreenCode ?? request.ScreenCode ?? "material_cards");

        var field = new MaterialCardDynamicFieldDefinition
        {
            Id = fieldId,
            ScreenCode = normalizedFieldScreenCode,
            LayerKey = NormalizeLayerKey(existing?.LayerKey ?? request.LayerKey),
            GroupId = request.GroupId,
            FieldKey = fieldKey,
            FieldLabel = fieldLabel,
            DataType = effectiveDataType,
            IsVisible = request.IsVisible,
            IsRequired = request.IsVisible && request.IsRequired,
            DefaultValue = defaultValue,
            DisplayOrder = displayOrder,
            ColumnSpan = columnSpan,
            IsSystem = existing?.IsSystem ?? false,
            CreatedAt = existing?.CreatedAt ?? DateTime.Now
        };

        field.SetActive(request.IsActive);
        await _repository.UpsertMaterialCardDynamicFieldAsync(field, normalizedOptions.Values.ToArray(), cancellationToken);
    }

    public async Task<IReadOnlyCollection<MaterialCardFieldSettingDto>> GetMaterialCardFieldSettingsAsync(
        CancellationToken cancellationToken)
    {
        var persistedSettings = await _repository.GetMaterialCardFieldSettingsAsync(cancellationToken);
        var persistedByKey = persistedSettings
            .GroupBy(x => x.FieldKey, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.OrderByDescending(y => y.UpdatedAt).First(), StringComparer.OrdinalIgnoreCase);

        return MaterialCardFieldCatalog.Definitions
            .Select(definition =>
            {
                if (!persistedByKey.TryGetValue(definition.Key, out var persisted))
                {
                    return new MaterialCardFieldSettingDto(
                        definition.Key,
                        definition.Label,
                        definition.DefaultVisible,
                        definition.DefaultVisible && definition.DefaultRequired,
                        definition.DisplayOrder);
                }

                return new MaterialCardFieldSettingDto(
                    definition.Key,
                    string.IsNullOrWhiteSpace(persisted.FieldLabel) ? definition.Label : persisted.FieldLabel,
                    persisted.IsVisible,
                    persisted.IsVisible && persisted.IsRequired,
                    persisted.DisplayOrder);
            })
            .OrderBy(x => x.DisplayOrder)
            .ThenBy(x => x.FieldLabel, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public async Task SaveMaterialCardFieldSettingsAsync(
        IReadOnlyCollection<SaveMaterialCardFieldSettingRequest> requests,
        CancellationToken cancellationToken)
    {
        if (requests.Count == 0)
        {
            throw new ArgumentException("Gorunum ayarlari bos gonderilemez.");
        }

        var requestByKey = requests
            .Where(x => !string.IsNullOrWhiteSpace(x.FieldKey))
            .GroupBy(x => x.FieldKey.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.Last(), StringComparer.OrdinalIgnoreCase);

        foreach (var request in requestByKey.Keys)
        {
            if (!MaterialCardFieldCatalog.IsSupported(request))
            {
                throw new ArgumentException("Gorunum ayarlarinda gecersiz saha bulundu.");
            }
        }

        var existingSettings = await _repository.GetMaterialCardFieldSettingsAsync(cancellationToken);
        var existingByKey = existingSettings
            .GroupBy(x => x.FieldKey, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.OrderByDescending(y => y.UpdatedAt).First(), StringComparer.OrdinalIgnoreCase);

        var now = DateTime.Now;
        var settingsToPersist = MaterialCardFieldCatalog.Definitions
            .Select(definition =>
            {
                var hasRequest = requestByKey.TryGetValue(definition.Key, out var request);
                var visibleValue = hasRequest ? request!.IsVisible : definition.DefaultVisible;
                var requiredValue = visibleValue && (hasRequest ? request!.IsRequired : definition.DefaultRequired);

                var hasExisting = existingByKey.TryGetValue(definition.Key, out var existing);
                return new MaterialCardFieldSetting
                {
                    Id = hasExisting ? existing!.Id : Guid.NewGuid(),
                    FieldKey = definition.Key,
                    FieldLabel = definition.Label,
                    IsVisible = visibleValue,
                    IsRequired = requiredValue,
                    DisplayOrder = definition.DisplayOrder,
                    CreatedAt = hasExisting ? existing!.CreatedAt : now,
                    UpdatedAt = now
                };
            })
            .ToArray();

        await _repository.UpsertMaterialCardFieldSettingsAsync(settingsToPersist, cancellationToken);
    }

    public async Task<IReadOnlyCollection<WarehouseLocationDto>> GetWarehouseLocationsAsync(CancellationToken cancellationToken)
    {
        var locations = await _repository.GetWarehouseLocationsAsync(cancellationToken);

        return locations
            .OrderBy(x => x.SortOrder)
            .ThenBy(x => x.LocationTypeCode, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.LocationCode, StringComparer.OrdinalIgnoreCase)
            .Select(x => new WarehouseLocationDto(
                x.Id,
                x.ParentId,
                x.LocationTypeCode,
                x.LocationCode,
                x.LocationName,
                x.SortOrder,
                x.MaxWeightCapacity,
                x.VolumeCapacity,
                x.IsActive))
            .ToArray();
    }

    public async Task<IReadOnlyCollection<MeasureUnitDefinitionDto>> GetMeasureUnitDefinitionsAsync(
        CancellationToken cancellationToken)
    {
        var definitions = await _repository.GetMeasureUnitDefinitionsAsync(cancellationToken);
        return definitions
            .OrderBy(x => x.SortOrder)
            .ThenBy(x => x.UnitCode, StringComparer.OrdinalIgnoreCase)
            .Select(x => new MeasureUnitDefinitionDto(
                x.Id,
                x.UnitCode,
                x.UnitName,
                x.IntlCode,
                x.SortOrder,
                x.IsActive))
            .ToArray();
    }

    public async Task<LogisticsConfigurationSnapshotDto> GetSnapshotAsync(CancellationToken cancellationToken)
    {
        var stockCards = await _repository.GetStockCardsAsync(cancellationToken);
        var properties = await _repository.GetPropertiesAsync(cancellationToken);
        var propertyValues = await _repository.GetPropertyValuesAsync(cancellationToken);
        var mappings = await _repository.GetStockPropertyMappingsAsync(cancellationToken);

        var stockLookup = stockCards.ToDictionary(x => x.Id, x => x.MaterialCode);
        var propertyLookup = properties.ToDictionary(x => x.Id);
        var valueLookup = propertyValues.ToDictionary(x => x.Id, x => x.Value);

        return new LogisticsConfigurationSnapshotDto(
            StockCards: stockCards
                .Select(x => new StockCardDto(
                    x.Id,
                    x.MaterialCode,
                    x.MaterialName,
                    x.MaterialDescription,
                    x.MaterialTypeId,
                    x.IsActive,
                    x.CreatedDate,
                    x.CreatedByUserId,
                    x.ModifiedDate,
                    x.ModifiedByUserId,
                    x.TrackCombinations,
                    x.TaxRate,
                    x.ImageData,
                    x.ImageMimeType))
                .ToArray(),
            Properties: properties
                .Select(x => new ConfigurationPropertyDto(
                    x.Id,
                    x.Code,
                    x.Name,
                    x.DataType.ToString(),
                    x.IsActive))
                .ToArray(),
            PropertyValues: propertyValues
                .Select(x =>
                {
                    var property = propertyLookup.GetValueOrDefault(x.PropertyId);
                    return new ConfigurationPropertyValueDto(
                        x.Id,
                        x.PropertyId,
                        property?.Name ?? "-",
                        string.IsNullOrWhiteSpace(x.Code) ? "-" : x.Code,
                        string.IsNullOrWhiteSpace(x.Description) ? x.Value : x.Description,
                        x.Value,
                        x.SortOrder,
                        x.IsActive);
                })
                .ToArray(),
            StockPropertyMappings: mappings
                .Select(x =>
                {
                    var property = propertyLookup.GetValueOrDefault(x.PropertyId);
                    var stockCode = stockLookup.GetValueOrDefault(x.StockCardId, "-");
                    var propertyCode = property?.Code ?? "-";
                    var propertyDataType = property?.DataType ?? ConfigurationFieldDataType.Text;
                    var resolvedValue = x.PropertyValueId.HasValue ? valueLookup.GetValueOrDefault(x.PropertyValueId.Value) : null;
                    var fallbackValue = !string.IsNullOrWhiteSpace(resolvedValue)
                        ? resolvedValue
                        : !string.IsNullOrWhiteSpace(x.TextValue)
                            ? x.TextValue
                            : x.NumericValue.HasValue
                                ? x.NumericValue.Value.ToString("0.####", CultureInfo.InvariantCulture)
                                : x.DateValue.HasValue
                                    ? x.DateValue.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                                    : string.Empty;
                    var generatedConfigurationCode = !string.IsNullOrWhiteSpace(fallbackValue)
                        ? BuildConfigurationCode(stockCode, propertyCode, fallbackValue, propertyDataType)
                        : null;

                    return new StockCardPropertyMappingDto(
                        x.Id,
                        x.StockCardId,
                        stockCode,
                        x.PropertyId,
                        propertyCode,
                        property?.Name ?? "-",
                        propertyDataType.ToString(),
                        x.PropertyValueId,
                        resolvedValue,
                        !string.IsNullOrWhiteSpace(x.ConfigurationCode)
                            ? x.ConfigurationCode
                            : generatedConfigurationCode,
                        x.TextValue,
                        x.NumericValue,
                        x.DateValue,
                        x.IsActive);
                })
                .ToArray());
    }

    public async Task<ProductConfigurationSnapshotDto> GetProductConfigurationSnapshotAsync(CancellationToken cancellationToken)
    {
        var records = await _repository.GetProductConfigurationRecordsAsync(cancellationToken);

        var features = records
            .Where(x => string.Equals(x.RecordType, "FEATURE", StringComparison.OrdinalIgnoreCase))
            .OrderBy(x => x.RecordCode, StringComparer.OrdinalIgnoreCase)
            .Select(x => new ProductConfigurationFeatureDto(
                x.Id,
                x.RecordCode,
                x.RecordName,
                NormalizeProductDataTypeValue(x.DataType),
                x.IsActive,
                x.CreatedDate,
                string.IsNullOrWhiteSpace(x.RelatedMaterialCode) ? null : x.RelatedMaterialCode.Trim()))
            .ToArray();

        var featureLookup = features.ToDictionary(x => x.Id);

        var values = records
            .Where(x => string.Equals(x.RecordType, "VALUE", StringComparison.OrdinalIgnoreCase))
            .OrderBy(x => x.ParentId)
            .ThenBy(x => x.RecordCode, StringComparer.OrdinalIgnoreCase)
            .Select(x =>
            {
                var feature = x.ParentId.HasValue ? featureLookup.GetValueOrDefault(x.ParentId.Value) : null;
                var (description, valueData) = SplitProductValuePayload(x.RecordName);
                return new ProductConfigurationValueDto(
                    x.Id,
                    x.ParentId ?? 0,
                    feature?.Code ?? "-",
                    feature?.Name ?? "Tanimsiz Ozellik",
                    x.RecordCode,
                    description,
                    valueData,
                    x.IsActive,
                    x.CreatedDate,
                    string.IsNullOrWhiteSpace(x.RelatedMaterialCode) ? null : x.RelatedMaterialCode.Trim());
            })
            .ToArray();

        var valueLookup = values.ToDictionary(x => x.Id);

        var configurations = records
            .Where(x => string.Equals(x.RecordType, "CONFIG", StringComparison.OrdinalIgnoreCase) && x.ParentId == null)
            .OrderBy(x => x.RelatedMaterialCode, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.RecordCode, StringComparer.OrdinalIgnoreCase)
            .Select(x =>
            {
                var value = x.ParentId.HasValue ? valueLookup.GetValueOrDefault(x.ParentId.Value) : null;
                var feature = value is not null
                    ? featureLookup.GetValueOrDefault(value.FeatureId)
                    : null;

                var childValueIds = records
                    .Where(r => string.Equals(r.RecordType, "CONFIG", StringComparison.OrdinalIgnoreCase) && r.ParentId == x.Id)
                    .Select(r => int.TryParse(r.RecordName, out var vId) ? vId : 0)
                    .Where(vId => vId > 0)
                    .ToList();

                var allValueIds = new HashSet<int>();
                if (value?.Id != null) allValueIds.Add(value.Id);
                foreach (var cId in childValueIds) allValueIds.Add(cId);

                return new ProductConfigurationItemDto(
                    x.Id,
                    value?.Id,
                    feature?.Id,
                    x.RecordCode,
                    x.RecordName,
                    x.RelatedMaterialCode ?? "-",
                    feature?.Code ?? "-",
                    feature?.Name ?? "Tanimsiz Ozellik",
                    value?.Code ?? "-",
                    value?.Description ?? "Tanimsiz Deger",
                    value?.Value ?? "-",
                    x.IsActive,
                    x.CreatedDate,
                    allValueIds.ToArray());
            })
            .ToArray();

        var featureStockLinks = records
            .Where(x => string.Equals(x.RecordType, "FEATURE_STOCK", StringComparison.OrdinalIgnoreCase))
            .Where(x => x.ParentId.HasValue && !string.IsNullOrWhiteSpace(x.RelatedMaterialCode ?? x.RecordCode))
            .Select(x => new ProductConfigurationFeatureStockLinkDto(
                x.ParentId!.Value,
                (x.RelatedMaterialCode ?? x.RecordCode).Trim().ToUpperInvariant()))
            .Distinct()
            .ToArray();

        return new ProductConfigurationSnapshotDto(features, values, configurations, featureStockLinks);
    }

    public async Task<int> CreateProductConfigurationFeatureAsync(
        CreateProductConfigurationFeatureRequest request,
        CancellationToken cancellationToken)
    {
        var name = NormalizeRequiredField(request.Name, 255, "Ozellik adi");
        if (!Enum.IsDefined(request.DataType))
        {
            throw new ArgumentException("Ozellik veri tipi gecersiz.");
        }

        var normalizedDataType = ToProductDataTypeValue(request.DataType);
        var unitOfMeasure = request.DataType == ConfigurationFieldDataType.Numeric && !string.IsNullOrWhiteSpace(request.UnitOfMeasure)
            ? request.UnitOfMeasure.Trim()
            : null;
        var createdFeature = await _repository.AddProductFeatureAsync(name, normalizedDataType, request.IsActive, unitOfMeasure, cancellationToken);
        return createdFeature.Id;
    }

    public async Task<(int Id, string Code)> CreateProductConfigurationValueAsync(
        CreateProductConfigurationValueRequest request,
        CancellationToken cancellationToken)
    {
        if (request.FeatureId <= 0)
        {
            throw new ArgumentException("Ozellik secimi zorunludur.");
        }

        var features = await _repository.GetProductConfigurationRecordsAsync(cancellationToken);
        var selectedFeature = features.FirstOrDefault(x =>
            x.Id == request.FeatureId &&
            string.Equals(x.RecordType, "FEATURE", StringComparison.OrdinalIgnoreCase));

        if (selectedFeature is null)
        {
            throw new ArgumentException("Secilen ozellik bulunamadi.");
        }

        var dataType = ParseProductDataTypeValue(selectedFeature.DataType);
        var description = NormalizeOptionalField(request.Description, 120);
        var typedValue = NormalizeProductTypedValue(dataType, request.TextValue, request.NumericValue, request.DateValue);
        var payload = ComposeProductValuePayload(description, typedValue);

        if (payload.Length > 255)
        {
            throw new ArgumentException("Deger aciklamasi ve veri birlikte en fazla 255 karakter olabilir.");
        }

        var aciklama = string.IsNullOrWhiteSpace(request.Aciklama) ? null : request.Aciklama.Trim();
        var (id, code) = await _repository.AddProductValueAsync(request.FeatureId, payload, request.IsActive, aciklama, cancellationToken);
        return (id, code);
    }

    public async Task<(int Id, string Code)> CreateProductConfigurationCombinationAsync(
        CreateProductConfigurationCombinationRequest request,
        CancellationToken cancellationToken)
    {
        var relatedMaterialCode = NormalizeRequiredField(request.RelatedMaterialCode, 50, "Malzeme kodu").ToUpperInvariant();

        if (request.ValueIds == null || request.ValueIds.Count == 0)
        {
            throw new ArgumentException("En az bir deger secilmelidir.");
        }

        var snapshot = await GetProductConfigurationSnapshotAsync(cancellationToken);
        var configNameParts = new List<string>();

        foreach (var valueId in request.ValueIds)
        {
            var selectedValue = snapshot.Values.FirstOrDefault(x => x.Id == valueId);
            if (selectedValue is null)
            {
                throw new ArgumentException($"Secilen degerlerden biri (ID: {valueId}) bulunamadi.");
            }
            configNameParts.Add(selectedValue.Description);
        }

        var stockCards = await _repository.GetStockCardsAsync(cancellationToken);
        if (!stockCards.Any(x => string.Equals(x.MaterialCode, relatedMaterialCode, StringComparison.OrdinalIgnoreCase)))
        {
            throw new ArgumentException("Secilen malzeme kodu bulunamadi.");
        }

        var configName = string.Join(" - ", configNameParts);
        if (configName.Length > 200)
        {
            configName = configName.Substring(0, 197) + "...";
        }

        return await _repository.AddProductConfigurationCombinationAsync(
            relatedMaterialCode,
            configName,
            request.ValueIds,
            request.IsActive,
            cancellationToken);
    }

    public async Task CreateProductConfigurationItemAsync(
        CreateProductConfigurationItemRequest request,
        CancellationToken cancellationToken)
    {
        var relatedMaterialCode = NormalizeRequiredField(request.RelatedMaterialCode, 50, "Malzeme kodu").ToUpperInvariant();
        if (request.FeatureId <= 0)
        {
            throw new ArgumentException("Ozellik secimi zorunludur.");
        }

        if (request.ValueId <= 0)
        {
            throw new ArgumentException("Deger secimi zorunludur.");
        }

        var snapshot = await GetProductConfigurationSnapshotAsync(cancellationToken);
        var selectedFeature = snapshot.Features.FirstOrDefault(x => x.Id == request.FeatureId);
        if (selectedFeature is null)
        {
            throw new ArgumentException("Secilen ozellik bulunamadi.");
        }

        var selectedValue = snapshot.Values.FirstOrDefault(x => x.Id == request.ValueId);
        if (selectedValue is null || selectedValue.FeatureId != request.FeatureId)
        {
            throw new ArgumentException("Secilen deger bu ozellige ait degil.");
        }

        var stockCards = await _repository.GetStockCardsAsync(cancellationToken);
        if (!stockCards.Any(x => string.Equals(x.MaterialCode, relatedMaterialCode, StringComparison.OrdinalIgnoreCase)))
        {
            throw new ArgumentException("Secilen malzeme kodu bulunamadi.");
        }

        var configName = $"{selectedFeature.Name} - {selectedValue.Description}";
        await _repository.AddProductConfigAsync(
            selectedValue.Id,
            relatedMaterialCode,
            configName,
            request.IsActive,
            cancellationToken);
    }

    public async Task SaveProductConfigurationFeatureStocksAsync(
        SaveProductConfigurationFeatureStocksRequest request,
        CancellationToken cancellationToken)
    {
        if (request.FeatureId <= 0)
        {
            throw new ArgumentException("Stok eslestirmesi icin bir ozellik secilmelidir.");
        }

        var records = await _repository.GetProductConfigurationRecordsAsync(cancellationToken);
        var feature = records.FirstOrDefault(x =>
            x.Id == request.FeatureId &&
            string.Equals(x.RecordType, "FEATURE", StringComparison.OrdinalIgnoreCase));

        if (feature is null)
        {
            throw new ArgumentException("Secilen ozellik bulunamadi.");
        }

        var normalizedStockCodes = (request.StockCodes ?? Array.Empty<string>())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => NormalizeRequiredField(x, 50, "Stok kodu").ToUpperInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var stockCards = await _repository.GetStockCardsAsync(cancellationToken);
        var activeStockCodes = stockCards
            .Where(x => x.IsActive)
            .Select(x => x.MaterialCode.Trim().ToUpperInvariant())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var invalidStockCode = normalizedStockCodes.FirstOrDefault(x => !activeStockCodes.Contains(x));
        if (invalidStockCode is not null)
        {
            throw new ArgumentException($"Secilen stok kodu bulunamadi veya aktif degil: {invalidStockCode}.");
        }

        await _repository.ReplaceProductFeatureStockLinksAsync(
            request.FeatureId,
            normalizedStockCodes,
            cancellationToken);
    }

    public async Task UpdateProductConfigurationFeatureAsync(
        UpdateProductConfigurationFeatureRequest request,
        CancellationToken cancellationToken)
    {
        if (request.Id <= 0)
        {
            throw new ArgumentException("Guncellenecek ozellik secilmelidir.");
        }

        var normalizedName = NormalizeRequiredField(request.Name, 255, "Ozellik adi");

        var snapshot = await GetProductConfigurationSnapshotAsync(cancellationToken);
        var feature = snapshot.Features.FirstOrDefault(x => x.Id == request.Id);
        if (feature is null)
        {
            throw new ArgumentException("Secilen ozellik bulunamadi.");
        }

        var hasValues = snapshot.Values.Any(x => x.FeatureId == request.Id);
        if (hasValues && !string.Equals(feature.DataType, ToProductDataTypeValue(request.DataType), StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Deger girilen ozelligin veri tipi degistirilemez.");
        }

        var normalizedDataType = ToProductDataTypeValue(request.DataType);
        var unitOfMeasure = request.DataType == ConfigurationFieldDataType.Numeric && !string.IsNullOrWhiteSpace(request.UnitOfMeasure)
            ? request.UnitOfMeasure.Trim()
            : null;

        await _repository.UpdateProductFeatureAsync(
            request.Id,
            normalizedName,
            normalizedDataType,
            unitOfMeasure,
            cancellationToken);
    }

    public async Task DeleteProductConfigurationFeatureAsync(int id, CancellationToken cancellationToken)
    {
        if (id <= 0)
        {
            throw new ArgumentException("Silinecek ozellik secilmelidir.");
        }

        var records = await _repository.GetProductConfigurationRecordsAsync(cancellationToken);
        var feature = records.FirstOrDefault(x =>
            x.Id == id &&
            string.Equals(x.RecordType, "FEATURE", StringComparison.OrdinalIgnoreCase));

        if (feature is null)
        {
            throw new ArgumentException("Secilen ozellik bulunamadi.");
        }

        var snapshot = await GetProductConfigurationSnapshotAsync(cancellationToken);

        if (snapshot.Values.Any(x => x.FeatureId == id))
        {
            throw new ArgumentException("Degeri olan ozellik silinemez. Once tum degerleri siliniz.");
        }

        await _repository.DeleteProductFeatureAsync(id, cancellationToken);
    }

    public async Task DeleteProductConfigurationValueAsync(int id, CancellationToken cancellationToken)
    {
        if (id <= 0)
        {
            throw new ArgumentException("Silinecek deger secilmelidir.");
        }

        var records = await _repository.GetProductConfigurationRecordsAsync(cancellationToken);
        var value = records.FirstOrDefault(x =>
            x.Id == id &&
            string.Equals(x.RecordType, "VALUE", StringComparison.OrdinalIgnoreCase));

        if (value is null)
        {
            throw new ArgumentException("Secilen deger bulunamadi.");
        }

        await _repository.DeleteProductValueAsync(id, cancellationToken);
    }

    public async Task UpdateProductConfigurationValueAsync(int id, string? description, string? aciklama, CancellationToken cancellationToken)
    {
        if (id <= 0) throw new ArgumentException("Guncellenmek istenen deger secilmelidir.");

        var records = await _repository.GetProductConfigurationRecordsAsync(cancellationToken);
        var record = records.FirstOrDefault(x =>
            x.Id == id && string.Equals(x.RecordType, "VALUE", StringComparison.OrdinalIgnoreCase));
        if (record is null) throw new ArgumentException("Secilen deger bulunamadi.");

        var feature = record.ParentId.HasValue
            ? records.FirstOrDefault(x => x.Id == record.ParentId.Value)
            : null;
        var dataType = ParseProductDataTypeValue(feature?.DataType);
        var (existingDesc, existingTyped) = SplitProductValuePayload(record.RecordName);
        var newDesc   = NormalizeOptionalField(description, 120);
        string newTyped;
        if (string.IsNullOrWhiteSpace(newDesc))
        {
            newTyped = existingTyped;
        }
        else
        {
            newTyped = dataType switch
            {
                ConfigurationFieldDataType.Numeric =>
                    decimal.TryParse(newDesc.Replace(',', '.'), System.Globalization.NumberStyles.Any,
                        System.Globalization.CultureInfo.InvariantCulture, out var parsedNum)
                        ? NormalizeProductTypedValue(dataType, null, parsedNum, null)
                        : throw new ArgumentException($"'{newDesc}' gecerli bir sayi degil."),
                ConfigurationFieldDataType.Date =>
                    DateTime.TryParse(newDesc, System.Globalization.CultureInfo.InvariantCulture,
                        System.Globalization.DateTimeStyles.None, out var parsedDate)
                        ? NormalizeProductTypedValue(dataType, null, null, parsedDate)
                        : throw new ArgumentException($"'{newDesc}' gecerli bir tarih degil."),
                _ => NormalizeProductTypedValue(dataType, newDesc, null, null),
            };
        }
        var payload   = ComposeProductValuePayload(string.IsNullOrWhiteSpace(newDesc) ? existingDesc : newDesc, newTyped);
        var cleanAcik = string.IsNullOrWhiteSpace(aciklama) ? null : aciklama.Trim();

        await _repository.UpdateProductValueAsync(id, payload, cleanAcik, cancellationToken);
    }

    public async Task DeleteProductConfigurationItemAsync(int id, CancellationToken cancellationToken)
    {
        if (id <= 0)
        {
            throw new ArgumentException("Silinecek yapilandirma secilmelidir.");
        }

        var records = await _repository.GetProductConfigurationRecordsAsync(cancellationToken);
        var config = records.FirstOrDefault(x =>
            x.Id == id &&
            string.Equals(x.RecordType, "CONFIG", StringComparison.OrdinalIgnoreCase));

        if (config is null)
        {
            throw new ArgumentException("Secilen yapilandirma bulunamadi.");
        }

        await _repository.DeleteProductConfigAsync(id, cancellationToken);
    }

    public async Task CreateStockCardAsync(CreateStockCardRequest request, CancellationToken cancellationToken)
    {
        var materialCode = request.MaterialCode.Trim();
        var materialName = request.MaterialName.Trim();

        if (string.IsNullOrWhiteSpace(materialCode))
        {
            throw new ArgumentException("Malzeme kodu zorunludur.");
        }

        if (string.IsNullOrWhiteSpace(materialName))
        {
            throw new ArgumentException("Malzeme adi zorunludur.");
        }

        var stockCards = await _repository.GetStockCardsAsync(cancellationToken);
        if (stockCards.Any(x => string.Equals(x.MaterialCode, materialCode, StringComparison.OrdinalIgnoreCase)))
        {
            throw new ArgumentException("Ayni malzeme kodu ile kayit zaten mevcut.");
        }

        var stockCard = new StockCard
        {
            MaterialCode = materialCode,
            MaterialName = materialName,
            MaterialDescription = NormalizeOptionalField(request.MaterialDescription, 500),
            MaterialTypeId = request.MaterialTypeId,
            TrackCombinations = request.TrackCombinations,
            CreatedDate = DateTime.Now,
            CreatedByUserId = request.CreatedByUserId,
            ImageData = request.ImageData,
            ImageMimeType = request.ImageMimeType
        };

        await _repository.AddStockCardAsync(stockCard, cancellationToken);
    }

    public async Task UpdateStockCardAsync(UpdateStockCardRequest request, CancellationToken cancellationToken)
    {
        if (request.StockCardId <= 0)
        {
            throw new ArgumentException("Guncellenecek malzeme karti secimi zorunludur.");
        }

        var materialCode = request.MaterialCode.Trim();
        var materialName = request.MaterialName.Trim();

        if (string.IsNullOrWhiteSpace(materialCode))
        {
            throw new ArgumentException("Malzeme kodu zorunludur.");
        }

        if (string.IsNullOrWhiteSpace(materialName))
        {
            throw new ArgumentException("Malzeme adi zorunludur.");
        }

        var stockCards = await _repository.GetStockCardsAsync(cancellationToken);
        var existing = stockCards.FirstOrDefault(x => x.Id == request.StockCardId);
        if (existing is null)
        {
            throw new ArgumentException("Secilen malzeme karti bulunamadi.");
        }

        if (stockCards.Any(x =>
                x.Id != request.StockCardId &&
                string.Equals(x.MaterialCode, materialCode, StringComparison.OrdinalIgnoreCase)))
        {
            throw new ArgumentException("Ayni malzeme kodu ile baska bir kayit mevcut.");
        }

        var updatedStockCard = new StockCard
        {
            Id = request.StockCardId,
            MaterialCode = materialCode,
            MaterialName = materialName,
            MaterialDescription = NormalizeOptionalField(request.MaterialDescription, 500),
            MaterialTypeId = request.MaterialTypeId,
            TrackCombinations = request.TrackCombinations,
            CreatedDate = existing.CreatedDate,
            CreatedByUserId = existing.CreatedByUserId,
            ModifiedDate = DateTime.Now,
            ModifiedByUserId = request.ModifiedByUserId,
            ImageData = request.ImageData,
            ImageMimeType = request.ImageMimeType
        };

        await _repository.UpdateStockCardAsync(updatedStockCard, cancellationToken);
    }

    public async Task DeactivateStockCardAsync(int stockCardId, CancellationToken cancellationToken)
    {
        if (stockCardId <= 0)
        {
            throw new ArgumentException("Stok karti secimi zorunludur.");
        }

        var stockCards = await _repository.GetStockCardsAsync(cancellationToken);
        var stockCard = stockCards.FirstOrDefault(x => x.Id == stockCardId);
        if (stockCard is null)
        {
            throw new ArgumentException("Secilen stok karti bulunamadi.");
        }

        await _repository.DeleteStockCardAsync(stockCardId, cancellationToken);
    }

    public async Task CreateWarehouseLocationAsync(
        CreateWarehouseLocationRequest request,
        CancellationToken cancellationToken)
    {
        var locationTypeCode = NormalizeLocationTypeCode(request.LocationTypeCode);
        var locationCode = NormalizeRequiredField(request.LocationCode, 50, "Lokasyon kodu");
        var locationName = NormalizeOptionalField(request.LocationName, 100);
        var sortOrder = NormalizeSortOrder(request.SortOrder);
        var maxWeightCapacity = NormalizeCapacity(request.MaxWeightCapacity, "Maksimum agirlik kapasitesi");
        var volumeCapacity = NormalizeCapacity(request.VolumeCapacity, "Hacim kapasitesi");

        var locations = await _repository.GetWarehouseLocationsAsync(cancellationToken);
        ValidateParentLocation(request.ParentId, locations);

        if (locations.Any(x => string.Equals(x.LocationCode, locationCode, StringComparison.OrdinalIgnoreCase)))
        {
            throw new ArgumentException("Ayni lokasyon kodu ile kayit zaten mevcut.");
        }

        var location = new WarehouseLocation
        {
            ParentId = request.ParentId,
            LocationTypeCode = locationTypeCode,
            LocationCode = locationCode,
            LocationName = locationName,
            SortOrder = sortOrder,
            MaxWeightCapacity = maxWeightCapacity,
            VolumeCapacity = volumeCapacity,
            IsActive = request.IsActive
        };

        await _repository.AddWarehouseLocationAsync(location, cancellationToken);
    }

    public async Task UpdateWarehouseLocationAsync(
        UpdateWarehouseLocationRequest request,
        CancellationToken cancellationToken)
    {
        if (request.Id <= 0)
        {
            throw new ArgumentException("Lokasyon secimi zorunludur.");
        }

        var locationTypeCode = NormalizeLocationTypeCode(request.LocationTypeCode);
        var locationCode = NormalizeRequiredField(request.LocationCode, 50, "Lokasyon kodu");
        var locationName = NormalizeOptionalField(request.LocationName, 100);
        var sortOrder = NormalizeSortOrder(request.SortOrder);
        var maxWeightCapacity = NormalizeCapacity(request.MaxWeightCapacity, "Maksimum agirlik kapasitesi");
        var volumeCapacity = NormalizeCapacity(request.VolumeCapacity, "Hacim kapasitesi");

        var locations = await _repository.GetWarehouseLocationsAsync(cancellationToken);
        var existingLocation = locations.FirstOrDefault(x => x.Id == request.Id);
        if (existingLocation is null)
        {
            throw new ArgumentException("Secilen lokasyon bulunamadi.");
        }

        if (request.ParentId.HasValue && request.ParentId.Value == request.Id)
        {
            throw new ArgumentException("Bir lokasyon kendisinin ust kirilimi olamaz.");
        }

        ValidateParentLocation(request.ParentId, locations);
        ValidateNoCircularParent(request.Id, request.ParentId, locations);

        if (locations.Any(x =>
                x.Id != request.Id &&
                string.Equals(x.LocationCode, locationCode, StringComparison.OrdinalIgnoreCase)))
        {
            throw new ArgumentException("Ayni lokasyon kodu ile baska bir kayit mevcut.");
        }

        var location = new WarehouseLocation
        {
            Id = request.Id,
            ParentId = request.ParentId,
            LocationTypeCode = locationTypeCode,
            LocationCode = locationCode,
            LocationName = locationName,
            SortOrder = sortOrder,
            MaxWeightCapacity = maxWeightCapacity,
            VolumeCapacity = volumeCapacity,
            IsActive = request.IsActive
        };

        await _repository.UpdateWarehouseLocationAsync(location, cancellationToken);
    }

    public async Task DeleteWarehouseLocationAsync(int locationId, CancellationToken cancellationToken)
    {
        if (locationId <= 0)
        {
            throw new ArgumentException("Lokasyon secimi zorunludur.");
        }

        var locations = await _repository.GetWarehouseLocationsAsync(cancellationToken);
        var existingLocation = locations.FirstOrDefault(x => x.Id == locationId);
        if (existingLocation is null)
        {
            throw new ArgumentException("Secilen lokasyon bulunamadi.");
        }

        if (locations.Any(x => x.ParentId == locationId))
        {
            throw new ArgumentException("Secilen lokasyonun alt kirilimlari var. Once alt kirilimlari siliniz.");
        }

        await _repository.DeleteWarehouseLocationAsync(locationId, cancellationToken);
    }

    public async Task CreateMeasureUnitDefinitionAsync(
        CreateMeasureUnitDefinitionRequest request,
        CancellationToken cancellationToken)
    {
        var unitCode = NormalizeMeasureUnitCode(request.UnitCode);
        var unitName = NormalizeRequiredField(request.UnitName, 100, "Olcu birimi adi");
        var sortOrder = NormalizeSortOrder(request.SortOrder);

        var definitions = await _repository.GetMeasureUnitDefinitionsAsync(cancellationToken);
        if (definitions.Any(x => string.Equals(x.UnitCode, unitCode, StringComparison.OrdinalIgnoreCase)))
        {
            throw new ArgumentException("Ayni olcu birimi kodu ile kayit zaten mevcut.");
        }

        var definition = new MeasureUnitDefinition
        {
            UnitCode = unitCode,
            UnitName = unitName,
            IntlCode = string.IsNullOrWhiteSpace(request.IntlCode) ? null : request.IntlCode.Trim(),
            SortOrder = sortOrder,
            IsActive = request.IsActive
        };

        await _repository.AddMeasureUnitDefinitionAsync(definition, cancellationToken);
    }

    public async Task UpdateMeasureUnitDefinitionAsync(
        UpdateMeasureUnitDefinitionRequest request,
        CancellationToken cancellationToken)
    {
        if (request.Id <= 0)
        {
            throw new ArgumentException("Olcu birimi secimi zorunludur.");
        }

        var unitCode = NormalizeMeasureUnitCode(request.UnitCode);
        var unitName = NormalizeRequiredField(request.UnitName, 100, "Olcu birimi adi");
        var sortOrder = NormalizeSortOrder(request.SortOrder);

        var definitions = await _repository.GetMeasureUnitDefinitionsAsync(cancellationToken);
        var existing = definitions.FirstOrDefault(x => x.Id == request.Id);
        if (existing is null)
        {
            throw new ArgumentException("Secilen olcu birimi bulunamadi.");
        }

        if (definitions.Any(x =>
                x.Id != request.Id &&
                string.Equals(x.UnitCode, unitCode, StringComparison.OrdinalIgnoreCase)))
        {
            throw new ArgumentException("Ayni olcu birimi kodu ile baska bir kayit mevcut.");
        }

        var definition = new MeasureUnitDefinition
        {
            Id = request.Id,
            UnitCode = unitCode,
            UnitName = unitName,
            IntlCode = string.IsNullOrWhiteSpace(request.IntlCode) ? null : request.IntlCode.Trim(),
            SortOrder = sortOrder,
            IsActive = request.IsActive
        };

        await _repository.UpdateMeasureUnitDefinitionAsync(definition, cancellationToken);
    }

    public async Task DeleteMeasureUnitDefinitionAsync(int id, CancellationToken cancellationToken)
    {
        if (id <= 0)
        {
            throw new ArgumentException("Olcu birimi secimi zorunludur.");
        }

        var definitions = await _repository.GetMeasureUnitDefinitionsAsync(cancellationToken);
        var existing = definitions.FirstOrDefault(x => x.Id == id);
        if (existing is null)
        {
            throw new ArgumentException("Secilen olcu birimi bulunamadi.");
        }

        var stockCards = await _repository.GetStockCardsAsync(cancellationToken);
        // MaterialDefinitions tablosunda artık unit_name alanı yok; bu kontrol kaldırıldı.
        if (false)
        {
            throw new ArgumentException("Bu olcu birimi malzeme kartlarinda kullaniliyor. Once bagli kayitlari guncelleyiniz.");
        }

        await _repository.DeleteMeasureUnitDefinitionAsync(id, cancellationToken);
    }

    public async Task<IReadOnlyCollection<StockUnitConversionDto>> GetStockUnitConversionsAsync(int stockCardId, CancellationToken cancellationToken)
    {
        var items = await _repository.GetStockUnitConversionsAsync(stockCardId, cancellationToken);
        return items.Select(x => new StockUnitConversionDto(x.Id, x.StockCardId, x.LineNo, x.UnitCode, x.Multiplier)).ToList();
    }

    public async Task SaveStockUnitConversionsAsync(int stockCardId, IReadOnlyCollection<SaveStockUnitConversionItem> items, CancellationToken cancellationToken)
    {
        var conversions = items.Select(x => new StockUnitConversion
        {
            StockCardId = stockCardId,
            UnitCode = x.UnitCode.Trim(),
            Multiplier = x.Multiplier,
        }).ToList();
        await _repository.SaveStockUnitConversionsAsync(stockCardId, conversions, cancellationToken);
    }

    public async Task ConfigureStockCardAsync(ConfigureStockCardRequest request, CancellationToken cancellationToken)
    {
        if (request.StockCardId <= 0)
        {
            throw new ArgumentException("Malzeme karti secimi zorunludur.");
        }

        var stockCards = await _repository.GetStockCardsAsync(cancellationToken);
        var stockCard = stockCards.FirstOrDefault(x => x.Id == request.StockCardId);
        if (stockCard is null)
        {
            throw new ArgumentException("Secilen malzeme karti bulunamadi.");
        }

        if (!stockCard.IsActive)
        {
            throw new ArgumentException("Pasif malzeme karti icin yapilandirma ayari degistirilemez.");
        }

        await _repository.UpdateStockCardConfigurableStatusAsync(stockCard.Id, request.IsConfigurable, cancellationToken);

        if (!request.IsConfigurable)
        {
            return;
        }

        var selectedPropertyIds = request.PropertyIds
            .Where(x => x != Guid.Empty)
            .Distinct()
            .ToArray();

        if (selectedPropertyIds.Length == 0)
        {
            return;
        }

        var properties = await _repository.GetPropertiesAsync(cancellationToken);
        var validPropertyIds = properties
            .Where(x => x.IsActive && selectedPropertyIds.Contains(x.Id))
            .Select(x => x.Id)
            .ToHashSet();

        if (validPropertyIds.Count != selectedPropertyIds.Length)
        {
            throw new ArgumentException("Secilen ozelliklerden en az biri gecersiz.");
        }

        var existingMappings = await _repository.GetStockPropertyMappingsAsync(cancellationToken);
        var alreadyLinkedPropertyIds = existingMappings
            .Where(x => x.StockCardId == stockCard.Id && x.IsActive)
            .Select(x => x.PropertyId)
            .ToHashSet();

        foreach (var propertyId in selectedPropertyIds)
        {
            if (alreadyLinkedPropertyIds.Contains(propertyId))
            {
                continue;
            }

            var mapping = new StockCardPropertyMapping
            {
                StockCardId = stockCard.Id,
                PropertyId = propertyId,
                PropertyValueId = null,
                ConfigurationCode = null,
                TextValue = null,
                NumericValue = null,
                DateValue = null
            };

            await _repository.AddStockPropertyMappingAsync(mapping, cancellationToken);
        }
    }

    public async Task CreatePropertyAsync(CreateConfigurationPropertyRequest request, CancellationToken cancellationToken)
    {
        var code = request.Code.Trim().ToUpperInvariant();
        var name = request.Name.Trim();

        if (string.IsNullOrWhiteSpace(code))
        {
            throw new ArgumentException("Ozellik kodu zorunludur.");
        }

        if (!PropertyCodeRegex.IsMatch(code))
        {
            throw new ArgumentException("Ozellik kodu 8 karakterli alfasayisal olmalidir.");
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Ozellik aciklamasi zorunludur.");
        }

        if (!Enum.IsDefined(request.DataType))
        {
            throw new ArgumentException("Ozellik veri tipi gecersiz.");
        }

        var properties = await _repository.GetPropertiesAsync(cancellationToken);
        if (properties.Any(x => string.Equals(x.Code, code, StringComparison.OrdinalIgnoreCase)))
        {
            throw new ArgumentException("Ayni ozellik kodu ile kayit zaten mevcut.");
        }

        var property = new ConfigurationProperty
        {
            Code = code,
            Name = name,
            DataType = request.DataType
        };

        await _repository.AddPropertyAsync(property, cancellationToken);
    }

    public async Task CreateStockPropertyLinkAsync(
        CreateStockCardPropertyLinkRequest request,
        CancellationToken cancellationToken)
    {
        if (request.StockCardId <= 0)
        {
            throw new ArgumentException("Stok karti secimi zorunludur.");
        }

        if (request.PropertyId == Guid.Empty)
        {
            throw new ArgumentException("Ozellik secimi zorunludur.");
        }

        var stockCards = await _repository.GetStockCardsAsync(cancellationToken);
        var properties = await _repository.GetPropertiesAsync(cancellationToken);
        var mappings = await _repository.GetStockPropertyMappingsAsync(cancellationToken);

        var stockCard = stockCards.FirstOrDefault(x => x.Id == request.StockCardId);
        if (stockCard is null)
        {
            throw new ArgumentException("Secilen stok karti bulunamadi.");
        }

        var property = properties.FirstOrDefault(x => x.Id == request.PropertyId);
        if (property is null)
        {
            throw new ArgumentException("Secilen ozellik bulunamadi.");
        }

        var hasExistingLink = mappings.Any(x =>
            x.StockCardId == stockCard.Id &&
            x.PropertyId == property.Id &&
            x.IsActive);

        if (hasExistingLink)
        {
            throw new ArgumentException("Bu stok karti icin secilen ozellik zaten eslestirilmis.");
        }

        var mapping = new StockCardPropertyMapping
        {
            StockCardId = stockCard.Id,
            PropertyId = property.Id,
            PropertyValueId = null,
            ConfigurationCode = null,
            TextValue = null,
            NumericValue = null,
            DateValue = null
        };

        await _repository.AddStockPropertyMappingAsync(mapping, cancellationToken);
    }

    public async Task CreatePropertyValueAsync(CreateConfigurationPropertyValueRequest request, CancellationToken cancellationToken)
    {
        var valueCode = request.Code.Trim().ToUpperInvariant();
        var valueDescription = request.Description.Trim();

        if (request.PropertyId == Guid.Empty)
        {
            throw new ArgumentException("Ozellik secimi zorunludur.");
        }

        if (string.IsNullOrWhiteSpace(valueCode))
        {
            throw new ArgumentException("Deger kodu zorunludur.");
        }

        if (string.IsNullOrWhiteSpace(valueDescription))
        {
            throw new ArgumentException("Deger aciklamasi zorunludur.");
        }

        var properties = await _repository.GetPropertiesAsync(cancellationToken);
        var property = properties.FirstOrDefault(x => x.Id == request.PropertyId);
        if (property is null)
        {
            throw new ArgumentException("Secilen ozellik bulunamadi.");
        }

        var normalizedValue = NormalizePropertyValue(property.DataType, request);

        var propertyValues = await _repository.GetPropertyValuesAsync(cancellationToken);
        if (propertyValues.Any(x =>
                x.PropertyId == request.PropertyId &&
                string.Equals(x.Code, valueCode, StringComparison.OrdinalIgnoreCase)))
        {
            throw new ArgumentException("Ayni ozellik icin bu deger kodu zaten tanimli.");
        }

        if (propertyValues.Any(x =>
                x.PropertyId == request.PropertyId &&
                string.Equals(x.Value, normalizedValue, StringComparison.OrdinalIgnoreCase)))
        {
            throw new ArgumentException("Ayni ozellik icin bu deger zaten tanimli.");
        }

        var propertyValue = new ConfigurationPropertyValue
        {
            PropertyId = request.PropertyId,
            Code = valueCode,
            Description = valueDescription,
            Value = normalizedValue,
            SortOrder = request.SortOrder
        };

        await _repository.AddPropertyValueAsync(propertyValue, cancellationToken);
    }

    public async Task CreateStockPropertyMappingAsync(
        CreateStockCardPropertyMappingRequest request,
        CancellationToken cancellationToken)
    {
        if (request.StockCardId <= 0)
        {
            throw new ArgumentException("Stok karti secimi zorunludur.");
        }

        if (request.PropertyId == Guid.Empty)
        {
            throw new ArgumentException("Ozellik secimi zorunludur.");
        }

        if (request.PropertyValueId == Guid.Empty)
        {
            throw new ArgumentException("Ozellik degeri secimi zorunludur.");
        }

        var stockCards = await _repository.GetStockCardsAsync(cancellationToken);
        var properties = await _repository.GetPropertiesAsync(cancellationToken);
        var propertyValues = await _repository.GetPropertyValuesAsync(cancellationToken);
        var mappings = await _repository.GetStockPropertyMappingsAsync(cancellationToken);

        var stockCard = stockCards.FirstOrDefault(x => x.Id == request.StockCardId);
        if (stockCard is null)
        {
            throw new ArgumentException("Secilen stok karti bulunamadi.");
        }

        var property = properties.FirstOrDefault(x => x.Id == request.PropertyId);
        if (property is null)
        {
            throw new ArgumentException("Secilen ozellik bulunamadi.");
        }

        var selectedValue = propertyValues.FirstOrDefault(x => x.Id == request.PropertyValueId);
        if (selectedValue is null || selectedValue.PropertyId != property.Id)
        {
            throw new ArgumentException("Secilen ozellik degeri bu ozellige ait degil.");
        }

        var existingMappings = mappings
            .Where(x => x.StockCardId == stockCard.Id && x.PropertyId == property.Id && x.IsActive)
            .ToArray();

        if (existingMappings.Length == 0)
        {
            throw new ArgumentException("Once stok karti ve ozelligi eslestiriniz.");
        }

        var existingLink = existingMappings
            .OrderByDescending(x => x.CreatedAt)
            .First();

        string? textValue = null;
        decimal? numericValue = null;
        DateTime? dateValue = null;

        switch (property.DataType)
        {
            case ConfigurationFieldDataType.Text:
                textValue = selectedValue.Value;
                break;
            case ConfigurationFieldDataType.Numeric:
                if (!TryParseNumeric(selectedValue.Value, out var numericParsed))
                {
                    throw new ArgumentException("Secilen ozellik degeri sayisal formatta degil.");
                }
                numericValue = numericParsed;
                break;
            case ConfigurationFieldDataType.Date:
                if (!TryParseDate(selectedValue.Value, out var dateParsed))
                {
                    throw new ArgumentException("Secilen ozellik degeri tarih formatinda degil.");
                }
                dateValue = dateParsed.Date;
                break;
            default:
                throw new ArgumentException("Ozellik veri tipi desteklenmiyor.");
        }

        var configurationCode = BuildConfigurationCode(stockCard.MaterialCode, property.Code, selectedValue.Value, property.DataType);
        await _repository.UpdateStockPropertyMappingValueAsync(
            existingLink.Id,
            selectedValue.Id,
            configurationCode,
            textValue,
            numericValue,
            dateValue,
            cancellationToken);
    }

    private static string NormalizePropertyValue(
        ConfigurationFieldDataType dataType,
        CreateConfigurationPropertyValueRequest request)
    {
        return dataType switch
        {
            ConfigurationFieldDataType.Text => NormalizeTextValue(request.TextValue),
            ConfigurationFieldDataType.Numeric => NormalizeNumericValue(request.NumericValue),
            ConfigurationFieldDataType.Date => NormalizeDateValue(request.DateValue),
            _ => throw new ArgumentException("Ozellik veri tipi desteklenmiyor.")
        };
    }

    private static string NormalizeTextValue(string? textValue)
    {
        var value = textValue?.Trim();
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Metin tipindeki ozellikler icin metin degeri zorunludur.");
        }

        return value;
    }

    private static string NormalizeNumericValue(decimal? numericValue)
    {
        if (!numericValue.HasValue)
        {
            throw new ArgumentException("Sayisal tipteki ozellikler icin sayisal deger zorunludur.");
        }

        return numericValue.Value.ToString("0.####", CultureInfo.InvariantCulture);
    }

    private static string NormalizeDateValue(DateTime? dateValue)
    {
        if (!dateValue.HasValue)
        {
            throw new ArgumentException("Tarih tipindeki ozellikler icin tarih degeri zorunludur.");
        }

        return dateValue.Value.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }

    private static string NormalizeLocationTypeCode(string? locationTypeCode)
    {
        var normalizedTypeCode = NormalizeRequiredField(locationTypeCode, 20, "Lokasyon tipi");
        if (string.Equals(normalizedTypeCode, "AISLE", StringComparison.OrdinalIgnoreCase))
        {
            normalizedTypeCode = "SECTION";
        }

        if (!AllowedLocationTypeCodes.Contains(normalizedTypeCode))
        {
            throw new ArgumentException("Lokasyon tipi gecersiz.");
        }

        return normalizedTypeCode.ToUpperInvariant();
    }

    private static string NormalizeMeasureUnitCode(string? unitCode)
    {
        return NormalizeRequiredField(unitCode, 20, "Olcu birimi kodu").ToUpperInvariant();
    }

    private async Task<(
        IReadOnlyCollection<MaterialCardFieldGroup> Groups,
        IReadOnlyCollection<MaterialCardDynamicFieldDefinition> Fields,
        IReadOnlyCollection<MaterialCardFieldOption> Options)> LoadMaterialCardDynamicSchemaAsync(
        CancellationToken cancellationToken)
    {
        var groups = await _repository.GetMaterialCardFieldGroupsAsync(cancellationToken);
        var fields = await _repository.GetMaterialCardDynamicFieldDefinitionsAsync(cancellationToken);
        var options = await _repository.GetMaterialCardFieldOptionsAsync(cancellationToken);
        return (groups, fields, options);
    }

    private static string NormalizeMetadataKey(string? value, string fieldLabel)
    {
        var normalizedValue = NormalizeRequiredField(value, 60, fieldLabel).ToLowerInvariant();
        if (!MetadataKeyRegex.IsMatch(normalizedValue))
        {
            throw new ArgumentException($"{fieldLabel} kucuk harf, rakam ve alt cizgi icermeli; harfle baslamalidir.");
        }

        return normalizedValue;
    }

    private static Dictionary<Guid, MaterialCardFieldOption> NormalizeMaterialCardFieldOptions(
        IReadOnlyCollection<SaveMaterialCardFieldOptionRequest> requests,
        MaterialCardDynamicFieldDataType dataType,
        Guid fieldId)
    {
        if (dataType is not MaterialCardDynamicFieldDataType.Dropdown and not MaterialCardDynamicFieldDataType.MultiSelect)
        {
            return new Dictionary<Guid, MaterialCardFieldOption>();
        }

        var normalized = new Dictionary<Guid, MaterialCardFieldOption>();
        var optionKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var request in requests)
        {
            var optionKey = NormalizeMetadataKey(request.OptionKey, "Secenek anahtari");
            var optionLabel = NormalizeRequiredField(request.OptionLabel, 160, "Secenek metni");
            var sortOrder = NormalizeSortOrder(request.SortOrder);
            var optionId = request.OptionId ?? Guid.NewGuid();

            if (!optionKeys.Add(optionKey))
            {
                throw new ArgumentException("Ayni alanda tekrar eden secenek anahtari olamaz.");
            }

            var option = new MaterialCardFieldOption
            {
                Id = optionId,
                FieldDefinitionId = fieldId,
                OptionKey = optionKey,
                OptionLabel = optionLabel,
                SortOrder = sortOrder,
                CreatedAt = DateTime.Now
            };
            option.SetActive(request.IsActive);
            normalized[optionId] = option;
        }

        if (normalized.Count == 0 || normalized.Values.All(x => !x.IsActive))
        {
            throw new ArgumentException("Dropdown veya coklu secim alanlarinda en az bir aktif secenek bulunmalidir.");
        }

        return normalized;
    }

    private static string? BuildDynamicAttributesJson(
        IReadOnlyDictionary<string, string?>? rawValues,
        IReadOnlyCollection<MaterialCardDynamicFieldDefinitionDto> fieldDefinitions)
    {
        var payload = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

        foreach (var field in fieldDefinitions
                     .Where(x => x.IsActive && !x.IsSystem)
                     .OrderBy(x => x.DisplayOrder)
                     .ThenBy(x => x.FieldKey, StringComparer.OrdinalIgnoreCase))
        {
            string? rawValue = null;
            rawValues?.TryGetValue(field.FieldKey, out rawValue);
            var normalizedRawValue = string.IsNullOrWhiteSpace(rawValue) ? field.DefaultValue : rawValue?.Trim();

            if (field.DataType == MaterialCardDynamicFieldDataType.MultiSelect)
            {
                var selectedValues = (normalizedRawValue ?? string.Empty)
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();

                if (selectedValues.Length == 0)
                {
                    if (field.IsRequired)
                    {
                        throw new ArgumentException($"{field.FieldLabel} zorunludur.");
                    }

                    continue;
                }

                var activeOptionKeys = field.Options
                    .Where(x => x.IsActive)
                    .Select(x => x.OptionKey)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

                if (selectedValues.Any(x => !activeOptionKeys.Contains(x)))
                {
                    throw new ArgumentException($"{field.FieldLabel} alaninda gecersiz secenek secildi.");
                }

                payload[field.FieldKey] = selectedValues;
                continue;
            }

            if (string.IsNullOrWhiteSpace(normalizedRawValue))
            {
                if (field.IsRequired)
                {
                    throw new ArgumentException($"{field.FieldLabel} zorunludur.");
                }

                continue;
            }

            payload[field.FieldKey] = field.DataType switch
            {
                MaterialCardDynamicFieldDataType.Integer => ParseIntegerDynamicFieldValue(normalizedRawValue, field.FieldLabel),
                MaterialCardDynamicFieldDataType.Decimal => ParseDecimalDynamicFieldValue(normalizedRawValue, field.FieldLabel),
                MaterialCardDynamicFieldDataType.Date => ParseDateDynamicFieldValue(normalizedRawValue, field.FieldLabel),
                MaterialCardDynamicFieldDataType.Boolean => ParseBooleanDynamicFieldValue(normalizedRawValue, field.FieldLabel),
                MaterialCardDynamicFieldDataType.Dropdown => ParseDropdownDynamicFieldValue(normalizedRawValue, field),
                _ => NormalizeRequiredField(normalizedRawValue, 500, field.FieldLabel)
            };
        }

        return payload.Count == 0 ? null : JsonSerializer.Serialize(payload);
    }


    private static int ParseIntegerDynamicFieldValue(string rawValue, string fieldLabel)
    {
        if (!int.TryParse(rawValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) &&
            !int.TryParse(rawValue, NumberStyles.Integer, CultureInfo.GetCultureInfo("tr-TR"), out parsed))
        {
            throw new ArgumentException($"{fieldLabel} tam sayi olmalidir.");
        }

        return parsed;
    }

    private static decimal ParseDecimalDynamicFieldValue(string rawValue, string fieldLabel)
    {
        if (!TryParseNumeric(rawValue, out var parsed))
        {
            throw new ArgumentException($"{fieldLabel} sayisal olmalidir.");
        }

        return decimal.Round(parsed, 4, MidpointRounding.AwayFromZero);
    }

    private static string ParseDateDynamicFieldValue(string rawValue, string fieldLabel)
    {
        if (!TryParseDate(rawValue, out var parsed))
        {
            throw new ArgumentException($"{fieldLabel} gecerli bir tarih olmalidir.");
        }

        return parsed.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }

    private static bool ParseBooleanDynamicFieldValue(string rawValue, string fieldLabel)
    {
        var normalized = rawValue.Trim().ToLowerInvariant();
        return normalized switch
        {
            "true" or "1" or "evet" or "yes" => true,
            "false" or "0" or "hayir" or "no" => false,
            _ => throw new ArgumentException($"{fieldLabel} evet/hayir tipinde olmalidir.")
        };
    }

    private static string ParseDropdownDynamicFieldValue(
        string rawValue,
        MaterialCardDynamicFieldDefinitionDto field)
    {
        var activeOptionKeys = field.Options
            .Where(x => x.IsActive)
            .Select(x => x.OptionKey)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (!activeOptionKeys.Contains(rawValue))
        {
            throw new ArgumentException($"{field.FieldLabel} alaninda gecersiz secenek secildi.");
        }

        return rawValue;
    }

    private static string NormalizeRequiredField(string? value, int maxLength, string fieldLabel)
    {
        var normalizedValue = value?.Trim();
        if (string.IsNullOrWhiteSpace(normalizedValue))
        {
            throw new ArgumentException($"{fieldLabel} zorunludur.");
        }

        if (normalizedValue.Length > maxLength)
        {
            throw new ArgumentException($"{fieldLabel} en fazla {maxLength} karakter olabilir.");
        }

        return normalizedValue;
    }

    private static string ToProductDataTypeValue(ConfigurationFieldDataType dataType) =>
        dataType switch
        {
            ConfigurationFieldDataType.Text => "TEXT",
            ConfigurationFieldDataType.Numeric => "NUMBER",
            ConfigurationFieldDataType.Date => "DATE",
            _ => "TEXT"
        };

    private static string NormalizeProductDataTypeValue(string? dataType)
    {
        var parsed = ParseProductDataTypeValue(dataType);
        return ToProductDataTypeValue(parsed);
    }

    private static ConfigurationFieldDataType ParseProductDataTypeValue(string? dataType)
    {
        var normalized = (dataType ?? string.Empty).Trim().ToUpperInvariant();
        return normalized switch
        {
            "TEXT" => ConfigurationFieldDataType.Text,
            "NUMBER" => ConfigurationFieldDataType.Numeric,
            "DATE" => ConfigurationFieldDataType.Date,
            _ => ConfigurationFieldDataType.Text
        };
    }

    private static string NormalizeProductTypedValue(
        ConfigurationFieldDataType dataType,
        string? textValue,
        decimal? numericValue,
        DateTime? dateValue)
    {
        return dataType switch
        {
            ConfigurationFieldDataType.Text => NormalizeRequiredField(textValue, 100, "Deger verisi"),
            ConfigurationFieldDataType.Numeric => NormalizeNumericValue(numericValue),
            ConfigurationFieldDataType.Date => NormalizeDateValue(dateValue),
            _ => NormalizeRequiredField(textValue, 100, "Deger verisi")
        };
    }

    private static string ComposeProductValuePayload(string? description, string value) =>
        $"{description ?? string.Empty}{ProductValueSeparator}{value}";

    private static (string Description, string Value) SplitProductValuePayload(string rawName)
    {
        if (string.IsNullOrWhiteSpace(rawName))
        {
            return ("-", "-");
        }

        var index = rawName.IndexOf(ProductValueSeparator, StringComparison.Ordinal);
        if (index < 0)
        {
            return (rawName.Trim(), rawName.Trim());
        }

        var description = rawName[..index].Trim();
        var value = rawName[(index + ProductValueSeparator.Length)..].Trim();

        return (
            string.IsNullOrWhiteSpace(description) ? (string.IsNullOrWhiteSpace(value) ? "-" : value) : description,
            string.IsNullOrWhiteSpace(value) ? "-" : value);
    }

    private static int NormalizeSortOrder(int sortOrder)
    {
        if (sortOrder < 0)
        {
            throw new ArgumentException("Siralama 0'dan kucuk olamaz.");
        }

        return sortOrder;
    }

    private static int NormalizeMaterialCardColumnSpan(int columnSpan)
    {
        if (columnSpan < 1 || columnSpan > 3)
        {
            throw new ArgumentException("Alan genisligi 1 ile 3 arasinda olmalidir.");
        }

        return columnSpan;
    }

    private static decimal? NormalizeCapacity(decimal? value, string fieldLabel)
    {
        if (!value.HasValue)
        {
            return null;
        }

        if (value.Value < 0)
        {
            throw new ArgumentException($"{fieldLabel} negatif olamaz.");
        }

        return decimal.Round(value.Value, 2, MidpointRounding.AwayFromZero);
    }

    private static void ValidateParentLocation(int? parentId, IReadOnlyCollection<WarehouseLocation> locations)
    {
        if (!parentId.HasValue)
        {
            return;
        }

        if (!locations.Any(x => x.Id == parentId.Value))
        {
            throw new ArgumentException("Secilen ust lokasyon bulunamadi.");
        }
    }

    private static void ValidateNoCircularParent(
        int locationId,
        int? parentId,
        IReadOnlyCollection<WarehouseLocation> locations)
    {
        if (!parentId.HasValue)
        {
            return;
        }

        var parentLookup = locations.ToDictionary(x => x.Id, x => x.ParentId);
        var currentParentId = parentId;
        var visited = new HashSet<int>();

        while (currentParentId.HasValue)
        {
            if (!visited.Add(currentParentId.Value))
            {
                break;
            }

            if (currentParentId.Value == locationId)
            {
                throw new ArgumentException("Lokasyon hiyerarsisinde dongu olusamaz.");
            }

            currentParentId = parentLookup.GetValueOrDefault(currentParentId.Value);
        }
    }

    private static string? NormalizeOptionalField(string? value, int maxLength)
    {
        var normalizedValue = value?.Trim();
        if (string.IsNullOrWhiteSpace(normalizedValue))
        {
            return null;
        }

        if (normalizedValue.Length > maxLength)
        {
            throw new ArgumentException($"Alan en fazla {maxLength} karakter olabilir.");
        }

        return normalizedValue;
    }

    private static string NormalizeMaterialType(string? materialType)
    {
        var normalizedType = materialType?.Trim();
        if (string.IsNullOrWhiteSpace(normalizedType))
        {
            return "Stockable";
        }

        if (!AllowedMaterialTypes.Contains(normalizedType))
        {
            throw new ArgumentException("Malzeme tipi gecersiz.");
        }

        if (string.Equals(normalizedType, "Consumable", StringComparison.OrdinalIgnoreCase))
        {
            return "Consumable";
        }

        if (string.Equals(normalizedType, "Service", StringComparison.OrdinalIgnoreCase))
        {
            return "Service";
        }

        return "Stockable";
    }

    private static string? NormalizeTrackingType(string? trackingType)
    {
        var normalizedType = trackingType?.Trim();
        if (string.IsNullOrWhiteSpace(normalizedType))
        {
            return null;
        }

        if (!AllowedTrackingTypes.Contains(normalizedType))
        {
            throw new ArgumentException("Takip yontemi gecersiz.");
        }

        if (string.Equals(normalizedType, "Lot", StringComparison.OrdinalIgnoreCase))
        {
            return "Lot";
        }

        if (string.Equals(normalizedType, "Serial", StringComparison.OrdinalIgnoreCase))
        {
            return "Serial";
        }

        return "None";
    }

    private static decimal? NormalizeNonNegativeDecimal(decimal? value, string fieldLabel)
    {
        if (!value.HasValue)
        {
            return null;
        }

        if (value.Value < 0)
        {
            throw new ArgumentException($"{fieldLabel} negatif olamaz.");
        }

        return decimal.Round(value.Value, 4, MidpointRounding.AwayFromZero);
    }

    private static bool TryParseNumeric(string rawValue, out decimal value)
    {
        return decimal.TryParse(rawValue, NumberStyles.Number, CultureInfo.InvariantCulture, out value) ||
               decimal.TryParse(rawValue, NumberStyles.Number, CultureInfo.GetCultureInfo("tr-TR"), out value);
    }

    private static bool TryParseDate(string rawValue, out DateTime value)
    {
        return DateTime.TryParseExact(rawValue, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out value) ||
               DateTime.TryParse(rawValue, CultureInfo.GetCultureInfo("tr-TR"), DateTimeStyles.None, out value) ||
               DateTime.TryParse(rawValue, CultureInfo.InvariantCulture, DateTimeStyles.None, out value);
    }

    private static string BuildConfigurationCode(
        string stockCode,
        string propertyCode,
        string rawValue,
        ConfigurationFieldDataType dataType)
    {
        var stockToken = ToAlphaNumericToken(stockCode, 12);
        var propertyToken = ToAlphaNumericToken(propertyCode, 8);

        var valueToken = dataType switch
        {
            ConfigurationFieldDataType.Date when TryParseDate(rawValue, out var dateValue) =>
                dateValue.ToString("yyyyMMdd", CultureInfo.InvariantCulture),
            ConfigurationFieldDataType.Numeric when TryParseNumeric(rawValue, out var numericValue) =>
                ToAlphaNumericToken(numericValue.ToString("0.####", CultureInfo.InvariantCulture), 14),
            _ => ToAlphaNumericToken(rawValue, 16)
        };

        return $"{stockToken}-{propertyToken}-{valueToken}";
    }

    private static string ToAlphaNumericToken(string? rawValue, int maxLength)
    {
        if (maxLength <= 0)
        {
            return "NA";
        }

        if (string.IsNullOrWhiteSpace(rawValue))
        {
            return "NA";
        }

        var token = new string(rawValue
            .Where(char.IsLetterOrDigit)
            .Select(char.ToUpperInvariant)
            .Take(maxLength)
            .ToArray());

        return string.IsNullOrWhiteSpace(token) ? "NA" : token;
    }

    public async Task<IReadOnlyCollection<StockCardDto>> GetStockCardsForLookupAsync(CancellationToken cancellationToken)
    {
        var cards = await _repository.GetStockCardsAsync(cancellationToken);
        return cards
            .Where(c => c.IsActive)
            .OrderBy(c => c.MaterialCode)
            .Select(c => new StockCardDto(
                c.Id,
                c.MaterialCode,
                c.MaterialName,
                c.MaterialDescription,
                null,
                c.IsActive,
                c.CreatedDate,
                c.CreatedByUserId,
                c.ModifiedDate,
                c.ModifiedByUserId,
                c.TrackCombinations))
            .ToList();
    }

    public async Task<IReadOnlyCollection<CombinationLookupRow>> GetCombinationsForLookupAsync(
        string materialCode, CancellationToken cancellationToken)
        => await _repository.GetCombinationsByMaterialCodeAsync(materialCode, cancellationToken);

    /* ── Ürün Ağacı (Reçete) ─────────────────────────────────────── */

    public async Task<IReadOnlyCollection<ProductTreeDto>> GetProductTreesAsync(CancellationToken cancellationToken)
    {
        var trees = await _repository.GetProductTreesAsync(cancellationToken);
        return trees.Select(t => new ProductTreeDto(
            t.Id,
            t.ParentMaterialCode,
            t.ConfigurationCode,
            t.Description,
            t.ImageData,
            t.ImageMimeType,
            t.Lines.Select(l => new ProductTreeLineDto(
                l.Id,
                l.ProductTreeId,
                l.ComponentMaterialCode,
                l.ComponentConfigCode,
                l.Quantity,
                l.ScrapRatio,
                l.LineGuid)).ToList())).ToList();
    }

    public async Task<ProductTreeWithNames?> GetProductTreeByCodeAsync(string materialCode, string? configCode, CancellationToken cancellationToken)
        => await _repository.GetProductTreeByCodeAsync(materialCode, configCode, cancellationToken);

    public async Task<int> SaveProductTreeAsync(SaveProductTreeRequest request, CancellationToken cancellationToken)
    {
        var parentCode = NormalizeRequiredField(request.ParentMaterialCode, 100, "Mamul kodu");

        var stockCards = await _repository.GetStockCardsAsync(cancellationToken);
        var activeCodes = stockCards
            .Where(x => x.IsActive)
            .Select(x => x.MaterialCode.Trim().ToUpperInvariant())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (!activeCodes.Contains(parentCode.ToUpperInvariant()))
            throw new ArgumentException($"Mamul kodu bulunamadi veya aktif degil: {parentCode}");

        var lines = (request.Lines ?? Array.Empty<SaveProductTreeLineRequest>()).ToList();
        if (lines.Count == 0)
            throw new ArgumentException("Recetede en az bir bilesen olmalidir.");

        foreach (var line in lines)
        {
            var compCode = NormalizeRequiredField(line.ComponentMaterialCode, 100, "Bilesen kodu");
            if (!activeCodes.Contains(compCode.ToUpperInvariant()))
                throw new ArgumentException($"Bilesen kodu bulunamadi veya aktif degil: {compCode}");
            if (line.Quantity <= 0)
                throw new ArgumentException($"Bilesen miktari sifirdan buyuk olmalidir: {compCode}");
            if (line.ScrapRatio < 0)
                throw new ArgumentException($"Fire miktari negatif olamaz: {compCode}");
        }

        byte[]? imageData = null;
        string? imageMimeType = null;
        if (!string.IsNullOrWhiteSpace(request.ImageBase64))
        {
            imageData     = Convert.FromBase64String(request.ImageBase64);
            imageMimeType = string.IsNullOrWhiteSpace(request.ImageMimeType) ? "image/png" : request.ImageMimeType.Trim();
        }

        var configCode = string.IsNullOrWhiteSpace(request.ConfigurationCode) ? null : request.ConfigurationCode.Trim();
        var fitMode    = string.IsNullOrWhiteSpace(request.ImageFitMode)       ? "square" : request.ImageFitMode.Trim();

        // UPSERT: if no ID given, look up existing record and reuse its ID to avoid duplicates
        int resolvedId = request.Id ?? 0;
        if (resolvedId <= 0)
        {
            var existing = await _repository.GetProductTreeByCodeAsync(parentCode, configCode, cancellationToken);
            if (existing is not null)
                resolvedId = existing.Id;
        }

        var entity = new ProductTree
        {
            Id                 = resolvedId,
            ParentMaterialCode = parentCode,
            ConfigurationCode  = configCode,
            Description        = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim(),
            ImageData          = imageData,
            ImageMimeType      = imageMimeType,
            ImageFitMode       = fitMode,
            Lines = lines.Select(l => new ProductTreeLine
            {
                ComponentMaterialCode = l.ComponentMaterialCode.Trim().ToUpperInvariant(),
                ComponentConfigCode   = string.IsNullOrWhiteSpace(l.ComponentConfigCode) ? null : l.ComponentConfigCode.Trim(),
                Quantity   = l.Quantity,
                ScrapRatio = l.ScrapRatio,
                LineGuid   = Guid.NewGuid()
            }).ToList()
        };

        if (entity.Id <= 0)
            return await _repository.AddProductTreeAsync(entity, cancellationToken);

        await _repository.UpdateProductTreeAsync(entity, cancellationToken);
        return entity.Id;
    }

    public async Task DeleteProductTreeAsync(int id, CancellationToken cancellationToken)
    {
        if (id <= 0) throw new ArgumentException("Silinecek recete secilmelidir.");
        await _repository.DeleteProductTreeAsync(id, cancellationToken);
    }

    /* ── Malzeme Grupları ─────────────────────────────────────── */

    public async Task<IReadOnlyCollection<MaterialGroupDto>> GetMaterialGroupsAsync(int? category, CancellationToken cancellationToken)
    {
        var groups = await _repository.GetMaterialGroupsAsync(category, cancellationToken);
        return groups.Select(g => new MaterialGroupDto(g.Id, g.GroupCategory, g.GroupCode, g.GroupDescription)).ToList();
    }

    public async Task CreateMaterialGroupAsync(SaveMaterialGroupRequest request, CancellationToken cancellationToken)
    {
        if (request.GroupCategory is < 1 or > 5)
            throw new ArgumentException("Grup kategorisi 1-5 arasında olmalıdır.");
        var code = (request.GroupCode ?? string.Empty).Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(code))
            throw new ArgumentException("Grup kodu boş olamaz.");
        if (code.Length > 10)
            throw new ArgumentException("Grup kodu en fazla 10 karakter olabilir.");
        var existing = await _repository.GetMaterialGroupsAsync(request.GroupCategory, cancellationToken);
        if (existing.Any(g => string.Equals(g.GroupCode, code, StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException($"Bu grup kodu bu kategoride zaten mevcut: {code}");
        var entity = new MaterialGroup
        {
            GroupCategory    = request.GroupCategory,
            GroupCode        = code,
            GroupDescription = string.IsNullOrWhiteSpace(request.GroupDescription) ? null : request.GroupDescription.Trim()
        };
        await _repository.AddMaterialGroupAsync(entity, cancellationToken);
    }

    public async Task UpdateMaterialGroupAsync(SaveMaterialGroupRequest request, CancellationToken cancellationToken)
    {
        if (request.Id is null) throw new ArgumentException("Güncellenecek kayıt bulunamadı.");
        if (request.GroupCategory is < 1 or > 5)
            throw new ArgumentException("Grup kategorisi 1-5 arasında olmalıdır.");
        var code = (request.GroupCode ?? string.Empty).Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(code))
            throw new ArgumentException("Grup kodu boş olamaz.");
        if (code.Length > 10)
            throw new ArgumentException("Grup kodu en fazla 10 karakter olabilir.");
        var existing = await _repository.GetMaterialGroupsAsync(request.GroupCategory, cancellationToken);
        if (existing.Any(g => string.Equals(g.GroupCode, code, StringComparison.OrdinalIgnoreCase) && g.Id != request.Id.Value))
            throw new ArgumentException($"Bu grup kodu bu kategoride zaten mevcut: {code}");
        var entity = new MaterialGroup
        {
            Id               = request.Id.Value,
            GroupCategory    = request.GroupCategory,
            GroupCode        = code,
            GroupDescription = string.IsNullOrWhiteSpace(request.GroupDescription) ? null : request.GroupDescription.Trim()
        };
        await _repository.UpdateMaterialGroupAsync(entity, cancellationToken);
    }

    public async Task DeleteMaterialGroupAsync(int id, CancellationToken cancellationToken)
        => await _repository.DeleteMaterialGroupAsync(id, cancellationToken);

    public async Task<IReadOnlyCollection<MaterialGroupMappingDto>> GetMaterialGroupMappingsAsync(int stockCardId, CancellationToken cancellationToken)
        => await _repository.GetMaterialGroupMappingsAsync(stockCardId, cancellationToken);

    public async Task SaveMaterialGroupMappingsAsync(SaveMaterialGroupMappingsRequest request, CancellationToken cancellationToken)
    {
        // SlotCodes has 5 items: index 0 = category 1, index 1 = category 2, ..., index 4 = category 5
        var allGroups = await _repository.GetMaterialGroupsAsync(null, cancellationToken);
        var groupsByCategory = allGroups
            .GroupBy(g => g.GroupCategory)
            .ToDictionary(grp => grp.Key, grp => grp.Select(g => g.GroupCode.ToUpperInvariant()).ToHashSet(StringComparer.OrdinalIgnoreCase));

        var mappings = new List<(int Slot, string Code)>();
        var slotCodes = request.SlotCodes?.ToList() ?? new List<string?>();
        for (int i = 0; i < slotCodes.Count && i < 5; i++)
        {
            var code = (slotCodes[i] ?? string.Empty).Trim().ToUpperInvariant();
            if (string.IsNullOrWhiteSpace(code)) continue;
            var cat = i + 1;
            if (!groupsByCategory.TryGetValue(cat, out var catCodes) || !catCodes.Contains(code))
                throw new ArgumentException($"Geçersiz grup kodu (Grup {cat}): {code}");
            mappings.Add((cat, code));
        }

        await _repository.SaveMaterialGroupMappingsAsync(request.StockCardId, mappings, cancellationToken);
    }
}
