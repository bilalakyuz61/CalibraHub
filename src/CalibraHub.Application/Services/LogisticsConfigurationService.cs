using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Application.Abstractions.Services;
using CalibraHub.Application.Auditing;
using CalibraHub.Application.Constants;
using CalibraHub.Application.Contracts;
using CalibraHub.Domain.Entities;
using CalibraHub.Domain.Enums;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace CalibraHub.Application.Services;

public sealed partial class LogisticsConfigurationService : ILogisticsConfigurationService
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
    private readonly ILogisticsConfigurationRepository _repository;
    private readonly IAuditTrailService? _audit;

    public LogisticsConfigurationService(
        ILogisticsConfigurationRepository repository,
        IAuditTrailService? audit = null)
    {
        _repository = repository;
        _audit = audit;
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
                .Select(x => new FieldGroupDto(
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

    public async Task SaveFieldGroupAsync(
        SaveFieldGroupRequest request,
        CancellationToken cancellationToken)
    {
        var groupKey = NormalizeMetadataKey(request.GroupKey, "Grup teknik adi");
        var groupLabel = NormalizeRequiredField(request.GroupLabel, 120, "Grup etiketi");
        var displayOrder = NormalizeSortOrder(request.DisplayOrder);

        var existingGroups = await _repository.GetFieldGroupsAsync(cancellationToken);
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
        // default "items"; mevcut grubun ScreenCode'u korunur.
        var normalizedScreenCode = NormalizeScreenCode(
            existing?.ScreenCode ?? request.ScreenCode ?? "items");

        var group = new FieldGroup
        {
            Id = existing?.Id ?? request.GroupId ?? Guid.NewGuid(),
            ScreenCode = normalizedScreenCode,
            LayerKey = NormalizeLayerKey(existing?.LayerKey ?? request.LayerKey),
            GroupKey = groupKey,
            GroupLabel = groupLabel,
            DisplayOrder = displayOrder,
            Created = existing?.Created ?? DateTime.Now
        };

        group.SetActive(request.IsActive);
        await _repository.UpsertFieldGroupAsync(group, cancellationToken);
    }

    /// <summary>
    /// Eski "MaterialCards" (CamelCase) ScreenCode kayitlari ile yeni snake_case
    /// uretim kodlari arasindaki uyumu saglar. Her iki format da ayni anlama
    /// gelir; kanonik form snake_case (material_cards, contact_accounts, sales_quotes).
    /// </summary>
    private static string NormalizeScreenCode(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "items";
        var lower = raw.Trim().ToLowerInvariant();
        return lower switch
        {
            "materialcards"   => "items",
            "contactaccounts" => "contacts",
            "salesquotes"     => "documents",
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
            existing?.ScreenCode ?? request.ScreenCode ?? "items");
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
        // kullanilir, yoksa default "items".
        var normalizedFieldScreenCode = NormalizeScreenCode(
            existing?.ScreenCode ?? request.ScreenCode ?? "items");

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
            Created = existing?.Created ?? DateTime.Now
        };

        field.SetActive(request.IsActive);
        await _repository.UpsertMaterialCardDynamicFieldAsync(field, normalizedOptions.Values.ToArray(), cancellationToken);
    }

    public async Task<IReadOnlyCollection<FieldDto>> GetFieldsAsync(
        CancellationToken cancellationToken)
    {
        var persistedSettings = await _repository.GetFieldsAsync(cancellationToken);
        var persistedByKey = persistedSettings
            .GroupBy(x => x.FieldKey, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.OrderByDescending(y => y.Updated).First(), StringComparer.OrdinalIgnoreCase);

        return MaterialCardFieldCatalog.Definitions
            .Select(definition =>
            {
                if (!persistedByKey.TryGetValue(definition.Key, out var persisted))
                {
                    return new FieldDto(
                        definition.Key,
                        definition.Label,
                        definition.DefaultVisible,
                        definition.DefaultVisible && definition.DefaultRequired,
                        definition.DisplayOrder);
                }

                return new FieldDto(
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

    public async Task SaveFieldsAsync(
        IReadOnlyCollection<SaveFieldRequest> requests,
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

        var existingSettings = await _repository.GetFieldsAsync(cancellationToken);
        var existingByKey = existingSettings
            .GroupBy(x => x.FieldKey, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.OrderByDescending(y => y.Updated).First(), StringComparer.OrdinalIgnoreCase);

        var now = DateTime.Now;
        var settingsToPersist = MaterialCardFieldCatalog.Definitions
            .Select(definition =>
            {
                var hasRequest = requestByKey.TryGetValue(definition.Key, out var request);
                var visibleValue = hasRequest ? request!.IsVisible : definition.DefaultVisible;
                var requiredValue = visibleValue && (hasRequest ? request!.IsRequired : definition.DefaultRequired);

                var hasExisting = existingByKey.TryGetValue(definition.Key, out var existing);
                return new Field
                {
                    Id = hasExisting ? existing!.Id : Guid.NewGuid(),
                    FieldKey = definition.Key,
                    FieldLabel = definition.Label,
                    IsVisible = visibleValue,
                    IsRequired = requiredValue,
                    DisplayOrder = definition.DisplayOrder,
                    Created = hasExisting ? existing!.Created : now,
                    Updated = now
                };
            })
            .ToArray();

        await _repository.UpsertFieldsAsync(settingsToPersist, cancellationToken);
    }

    public async Task<IReadOnlyCollection<LocationDto>> GetLocationsAsync(CancellationToken cancellationToken)
    {
        var locations = await _repository.GetLocationsAsync(cancellationToken);

        return locations
            .OrderBy(x => x.SortOrder)
            .ThenBy(x => x.LocationTypeCode, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.LocationCode, StringComparer.OrdinalIgnoreCase)
            .Select(x => new LocationDto(
                x.Id,
                x.ParentId,
                x.LocationTypeCode,
                x.LocationCode,
                x.LocationName,
                x.SortOrder,
                x.MaxWeightCapacity,
                x.VolumeCapacity,
                x.IsActive,
                x.IsMachinePark,
                x.IsStorageArea,
                x.IsCountReference,
                x.IsSingleChildType,
                x.AllowNegativeBalance))
            .ToArray();
    }

    public async Task<IReadOnlyCollection<UnitDto>> GetUnitsAsync(
        CancellationToken cancellationToken)
    {
        var definitions = await _repository.GetUnitsAsync(cancellationToken);
        return definitions
            .OrderBy(x => x.SortOrder)
            .ThenBy(x => x.Code, StringComparer.OrdinalIgnoreCase)
            .Select(x => new UnitDto(
                x.Id,
                x.Code,
                x.Name,
                x.IntlCode,
                x.SortOrder,
                x.IsActive))
            .ToArray();
    }

    public async Task<LogisticsConfigurationSnapshotDto> GetSnapshotAsync(CancellationToken cancellationToken)
    {
        var stockCards = await _repository.GetItemsAsync(cancellationToken);
        var properties = await _repository.GetPropertiesAsync(cancellationToken);
        var propertyValues = await _repository.GetPropertyValuesAsync(cancellationToken);
        var mappings = await _repository.GetStockPropertyMappingsAsync(cancellationToken);

        var stockLookup = stockCards.ToDictionary(x => x.Id, x => x.Code);
        var propertyLookup = properties.ToDictionary(x => x.Id);
        var valueLookup = propertyValues.ToDictionary(x => x.Id, x => x.Value);

        return new LogisticsConfigurationSnapshotDto(
            Items: stockCards
                .Select(x => new ItemDto(
                    x.Id,
                    x.Code,
                    x.Name,
                    x.TypeId,
                    x.IsActive,
                    x.Created,
                    x.Updated,
                    x.UnitId,
                    x.Combinations,
                    x.TaxRate,
                    TrackingType: x.TrackingType,
                    MinStock: x.MinStock,
                    AutoSerial: x.AutoSerial,
                    Barcode: x.Barcode))
                .ToArray(),
            Properties: properties
                .Select(x => new FeatureDto(
                    x.Id,
                    x.Name,
                    x.DataType.ToString(),
                    x.IsActive))
                .ToArray(),
            PropertyValues: propertyValues
                .Select(x =>
                {
                    var property = propertyLookup.GetValueOrDefault(x.PropertyId);
                    return new FeatureValueDto(
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
                    var property = propertyLookup.GetValueOrDefault(x.FeatureId);
                    var stockCode = stockLookup.GetValueOrDefault(x.ItemId, "-");
                    var propertyDataType = property?.DataType ?? ConfigurationFieldDataType.Text;
                    var resolvedValue = x.FeatureValueId.HasValue ? valueLookup.GetValueOrDefault(x.FeatureValueId.Value) : null;

                    return new ItemFeatureMappingDto(
                        x.Id,
                        x.ItemId,
                        stockCode,
                        x.FeatureId,
                        property?.Name ?? "-",
                        propertyDataType.ToString(),
                        x.FeatureValueId,
                        resolvedValue,
                        x.IsActive);
                })
                .ToArray());
    }

    public async Task<ProductConfigurationSnapshotDto> GetProductConfigurationSnapshotAsync(CancellationToken cancellationToken)
    {
        var records = await _repository.GetItemConfigurationsAsync(cancellationToken);
        // Master FEATURE tanimlari ItemFeature'dan, VALUE'lar FeatureValue'dan okunur
        var itemFeatures = await _repository.GetPropertiesAsync(cancellationToken);
        var featureValues = await _repository.GetPropertyValuesAsync(cancellationToken);
        // Stok-Item haritasi (combination'larin malzeme kodunu gostermek icin)
        var items = await _repository.GetItemsAsync(cancellationToken);
        var itemCodeById = items.ToDictionary(i => i.Id, i => i.Code);

        var features = itemFeatures
            .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .Select(x => new ProductConfigurationFeatureDto(
                x.Id,
                string.Empty,
                x.Name,
                ToProductDataTypeValue(x.DataType),
                x.IsActive,
                x.CreatedAt,
                x.UnitOfMeasure,
                x.VisibleInDesign))
            .ToArray();

        var featureLookup = features.ToDictionary(x => x.Id);

        // VALUE'lar artik FeatureValue tablosundan
        var values = featureValues
            .OrderBy(x => x.PropertyId)
            .ThenBy(x => x.SortOrder)
            .ThenBy(x => x.Code, StringComparer.OrdinalIgnoreCase)
            .Select(x =>
            {
                var feature = featureLookup.GetValueOrDefault(x.PropertyId);
                return new ProductConfigurationValueDto(
                    x.Id,
                    x.PropertyId,
                    feature?.Code ?? "-",
                    feature?.Name ?? "Tanimsiz Ozellik",
                    x.Code,
                    x.Description,
                    x.Value,
                    x.IsActive,
                    x.CreatedAt,
                    null); // aciklama (legacy) — FeatureValue'da yok
            })
            .ToArray();

        var valueLookup = values.ToDictionary(x => x.Id);

        // Kombinasyonlar — ItemConfiguration'da artik sadece CONFIG record'lari
        var configurations = records
            .Where(x => string.Equals(x.RecordType, "CONFIG", StringComparison.OrdinalIgnoreCase) && x.ParentId == null)
            .OrderBy(x => x.ItemId)
            .ThenBy(x => x.Id)
            .Select(x =>
            {
                var materialCode = x.ItemId.HasValue ? itemCodeById.GetValueOrDefault(x.ItemId.Value, x.RelatedMaterialCode ?? "-") : (x.RelatedMaterialCode ?? "-");

                var childValueIds = records
                    .Where(r => string.Equals(r.RecordType, "CONFIG", StringComparison.OrdinalIgnoreCase) && r.ParentId == x.Id)
                    .Select(r => int.TryParse(r.RecordName, out var vId) ? vId : 0)
                    .Where(vId => vId > 0)
                    .ToList();

                return new ProductConfigurationItemDto(
                    x.Id,
                    null,
                    null,
                    x.RecordCode,
                    x.RecordName,
                    materialCode,
                    "-",
                    "-",
                    "-",
                    "-",
                    "-",
                    x.IsActive,
                    x.CreatedDate,
                    childValueIds.ToArray());
            })
            .ToArray();

        // Feature-Stock linkleri ItemFeatureMappings'ten:
        //   - FeatureValueId IS NULL = "header" satir (bu stok'a bu ozellik bagli, PrintDescriptionInDesign tasir)
        //   - FeatureValueId NOT NULL = bu (stok, ozellik) icin izin verilen deger kisitlamasi
        var stockMappings = await _repository.GetStockPropertyMappingsAsync(cancellationToken);
        var activeMappings = stockMappings.Where(m => m.IsActive).ToArray();
        var featureStockLinks = activeMappings
            .Where(m => m.FeatureValueId == null)
            .Select(m =>
            {
                var stockCode = itemCodeById.GetValueOrDefault(m.ItemId, string.Empty);
                var normalizedCode = (stockCode ?? string.Empty).Trim().ToUpperInvariant();
                var allowed = activeMappings
                    .Where(x => x.ItemId == m.ItemId
                                && x.FeatureId == m.FeatureId
                                && x.FeatureValueId.HasValue)
                    .Select(x => x.FeatureValueId!.Value)
                    .Distinct()
                    .ToArray();
                return new ProductConfigurationFeatureStockLinkDto(
                    m.FeatureId,
                    normalizedCode,
                    m.PrintDescriptionInDesign,
                    allowed);
            })
            .Where(x => !string.IsNullOrWhiteSpace(x.StockCode))
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

        // Duplicate ozellik adi kontrolu
        var existingFeatures = await _repository.GetPropertiesAsync(cancellationToken);
        if (existingFeatures.Any(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            throw new ArgumentException("Ayni ozellik adi ile kayit zaten mevcut.");
        }

        var unitOfMeasure = request.DataType == ConfigurationFieldDataType.Numeric && !string.IsNullOrWhiteSpace(request.UnitOfMeasure)
            ? request.UnitOfMeasure.Trim()
            : null;

        // Master ozellik tanimi ItemFeature tablosunda — buraya kaydet
        var feature = new ItemFeature
        {
            Name = name,
            DataType = request.DataType,
            UnitOfMeasure = unitOfMeasure,
            VisibleInDesign = request.VisibleInDesign,
            CreatedAt = DateTime.Now
            // CompanyId repository tarafinda ResolveCurrentCompanyId() ile doldurulur
        };
        if (!request.IsActive) feature.Deactivate();

        return await _repository.AddPropertyAsync(feature, cancellationToken);
    }

    public async Task<(int Id, string Code)> CreateProductConfigurationValueAsync(
        CreateProductConfigurationValueRequest request,
        CancellationToken cancellationToken)
    {
        if (request.FeatureId <= 0)
        {
            throw new ArgumentException("Ozellik secimi zorunludur.");
        }

        var itemFeatures = await _repository.GetPropertiesAsync(cancellationToken);
        var selectedFeature = itemFeatures.FirstOrDefault(x => x.Id == request.FeatureId);
        if (selectedFeature is null)
        {
            throw new ArgumentException("Secilen ozellik bulunamadi.");
        }

        var dataType = selectedFeature.DataType;
        var description = NormalizeOptionalField(request.Description, 160) ?? string.Empty;
        var typedValue = NormalizeProductTypedValue(dataType, request.TextValue, request.NumericValue, request.DateValue);

        if (string.IsNullOrWhiteSpace(typedValue))
        {
            throw new ArgumentException("Deger zorunludur.");
        }

        // Duplicate check (ayni feature icin ayni value)
        var existingValues = await _repository.GetPropertyValuesAsync(cancellationToken);
        if (existingValues.Any(v => v.PropertyId == request.FeatureId &&
                                    string.Equals(v.Value, typedValue, StringComparison.OrdinalIgnoreCase)))
        {
            throw new ArgumentException("Bu ozellik icin ayni deger zaten tanimli.");
        }

        // Otomatik kod uret: ozellik degerlerinin sirasi
        var sortOrder = existingValues.Where(v => v.PropertyId == request.FeatureId).Count() + 1;
        var generatedCode = $"V{sortOrder:D3}";

        var fv = new FeatureValue
        {
            PropertyId = request.FeatureId,
            Code = generatedCode,
            Description = string.IsNullOrWhiteSpace(description) ? typedValue : description,
            Value = typedValue,
            SortOrder = sortOrder
        };
        if (!request.IsActive) fv.Deactivate();

        await _repository.AddPropertyValueAsync(fv, cancellationToken);

        // INSERT'in ID'sini almak icin yeniden cek (hizli yol; daha sonra repo'dan SCOPE_IDENTITY donusumu eklenebilir)
        var refreshed = await _repository.GetPropertyValuesAsync(cancellationToken);
        var inserted = refreshed
            .Where(v => v.PropertyId == request.FeatureId && string.Equals(v.Value, typedValue, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(v => v.Id)
            .FirstOrDefault();

        return (inserted?.Id ?? 0, inserted?.Code ?? generatedCode);
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

        var stockCards = await _repository.GetItemsAsync(cancellationToken);
        if (!stockCards.Any(x => string.Equals(x.Code, relatedMaterialCode, StringComparison.OrdinalIgnoreCase)))
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

        var stockCards = await _repository.GetItemsAsync(cancellationToken);
        if (!stockCards.Any(x => string.Equals(x.Code, relatedMaterialCode, StringComparison.OrdinalIgnoreCase)))
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

        // Master ozellik ItemFeature tablosundan
        var itemFeatures = await _repository.GetPropertiesAsync(cancellationToken);
        var feature = itemFeatures.FirstOrDefault(x => x.Id == request.FeatureId);

        if (feature is null)
        {
            throw new ArgumentException("Secilen ozellik bulunamadi.");
        }

        var stockItems = (request.Stocks ?? Array.Empty<SaveProductConfigurationFeatureStockItem>())
            .Where(x => !string.IsNullOrWhiteSpace(x.StockCode))
            .Select(x => (
                Code: NormalizeRequiredField(x.StockCode, 50, "Stok kodu").ToUpperInvariant(),
                PrintDescriptionInDesign: x.PrintDescriptionInDesign,
                AllowedValueIds: (x.AllowedValueIds ?? Array.Empty<int>()).Where(v => v > 0).Distinct().ToArray()))
            .GroupBy(x => x.Code, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.Last())
            .ToArray();

        var stockCards = await _repository.GetItemsAsync(cancellationToken);
        var activeItemsByCode = stockCards
            .Where(x => x.IsActive)
            .GroupBy(x => x.Code.Trim().ToUpperInvariant(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().Id, StringComparer.OrdinalIgnoreCase);

        // Geçerli value ID'lerini hazirla — kisitlamada gecersiz id'ye izin verme
        var validValueIds = (await _repository.GetPropertyValuesAsync(cancellationToken))
            .Where(v => v.PropertyId == request.FeatureId)
            .Select(v => v.Id)
            .ToHashSet();

        var resolved = new List<(int ItemId, int[] AllowedValueIds, bool PrintDescriptionInDesign)>();
        foreach (var item in stockItems)
        {
            if (!activeItemsByCode.TryGetValue(item.Code, out var itemId))
            {
                throw new ArgumentException($"Secilen stok kodu bulunamadi veya aktif degil: {item.Code}.");
            }

            var filteredValueIds = item.AllowedValueIds.Where(v => validValueIds.Contains(v)).ToArray();
            resolved.Add((itemId, filteredValueIds, item.PrintDescriptionInDesign));
        }

        await _repository.ReplaceFeatureStockLinksAsync(
            request.FeatureId,
            resolved.ToArray(),
            cancellationToken);
    }

    public async Task SetFeaturesForItemAsync(
        string stockCode,
        IReadOnlyCollection<(int FeatureId, bool PrintDescriptionInDesign, int[] AllowedValueIds)> items,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(stockCode))
            throw new ArgumentException("Stok kodu zorunludur.");

        var normalizedCode = stockCode.Trim().ToUpperInvariant();

        // Stok kartinin gerçekten var ve aktif oldugunu kontrol et
        var stockCards = await _repository.GetItemsAsync(cancellationToken);
        var stockCard = stockCards.FirstOrDefault(x => x.IsActive &&
            string.Equals(x.Code.Trim(), normalizedCode, StringComparison.OrdinalIgnoreCase));
        if (stockCard is null)
            throw new ArgumentException($"Stok kartı bulunamadı veya aktif değil: {normalizedCode}");

        // Ozellikleri validate et — ItemFeature tablosundan
        var itemFeatures = await _repository.GetPropertiesAsync(cancellationToken);
        var validFeatureIds = itemFeatures
            .Where(r => r.IsActive)
            .Select(r => r.Id)
            .ToHashSet();

        // Tum aktif feature value'lari ile feature-by-id bag kur
        var allFeatureValues = await _repository.GetPropertyValuesAsync(cancellationToken);
        var validValueIdsByFeatureId = allFeatureValues
            .Where(v => v.IsActive)
            .GroupBy(v => v.PropertyId)
            .ToDictionary(g => g.Key, g => g.Select(v => v.Id).ToHashSet());

        var requestedItems = (items ?? Array.Empty<(int, bool, int[])>())
            .Where(x => x.FeatureId > 0)
            .GroupBy(x => x.FeatureId)
            .Select(g =>
            {
                var last = g.Last();
                var allowed = (last.AllowedValueIds ?? Array.Empty<int>())
                    .Where(v => v > 0)
                    .Distinct()
                    .Where(v => validValueIdsByFeatureId.TryGetValue(g.Key, out var ok) && ok.Contains(v))
                    .ToArray();
                return (FeatureId: g.Key, PrintDescriptionInDesign: last.PrintDescriptionInDesign, AllowedValueIds: allowed);
            })
            .ToArray();

        var invalid = requestedItems.FirstOrDefault(x => !validFeatureIds.Contains(x.FeatureId));
        if (invalid.FeatureId != 0)
            throw new ArgumentException($"Geçersiz özellik ID: {invalid.FeatureId}");

        // Kombinasyon koruma 1: Stok'a daha once baglanmis olup, simdi cikarilmak istenen feature'lar
        // arasinda aktif kombinasyonda kullanilan varsa hata firlat.
        var existingMappings = await _repository.GetStockPropertyMappingsAsync(cancellationToken);
        var currentFeatureIds = existingMappings
            .Where(m => m.IsActive && m.ItemId == stockCard.Id)
            .Select(m => m.FeatureId)
            .Distinct()
            .ToHashSet();
        var requestedFeatureIds = requestedItems.Select(x => x.FeatureId).ToHashSet();
        var removedFeatureIds = currentFeatureIds.Except(requestedFeatureIds).ToHashSet();
        if (removedFeatureIds.Count > 0)
        {
            var usedFeatureIds = await _repository.GetUsedFeatureIdsInCombinationsAsync(stockCard.Id, cancellationToken);
            var blocked = removedFeatureIds.Intersect(usedFeatureIds).ToArray();
            if (blocked.Length > 0)
            {
                var blockedNames = itemFeatures
                    .Where(f => blocked.Contains(f.Id))
                    .Select(f => f.Name)
                    .ToArray();
                throw new ArgumentException(
                    "Bu ozellik(ler) aktif kombinasyon(lar)da kullaniliyor, kaldirilamaz: " +
                    string.Join(", ", blockedNames));
            }
        }

        // Kombinasyon koruma 2: (feature, value) cifti aktif kombinasyonda kullaniliyorsa,
        // bu stok icin o pair'in AllowedValueIds listesinden cikarilmasi engellenir.
        // Kural: AllowedValueIds dolu ise (kisitlama varsa), kombinasyonda kullanilan tum value'lar listede olmali.
        // AllowedValueIds bos ise (kisitlama yok = tum degerler gecerli), kontrol gerek yok.
        var usedFeatureValuePairs = await _repository.GetUsedFeatureValueIdsInCombinationsAsync(stockCard.Id, cancellationToken);
        var usedByFeature = usedFeatureValuePairs
            .GroupBy(x => x.FeatureId)
            .ToDictionary(g => g.Key, g => g.Select(x => x.ValueId).ToHashSet());

        var blockedValuesByFeature = new Dictionary<int, List<int>>();
        foreach (var item in requestedItems)
        {
            if (item.AllowedValueIds == null || item.AllowedValueIds.Length == 0) continue; // kisitlama yok
            if (!usedByFeature.TryGetValue(item.FeatureId, out var usedValues)) continue;
            var requestedSet = new HashSet<int>(item.AllowedValueIds);
            var missing = usedValues.Where(v => !requestedSet.Contains(v)).ToList();
            if (missing.Count > 0)
            {
                blockedValuesByFeature[item.FeatureId] = missing;
            }
        }
        if (blockedValuesByFeature.Count > 0)
        {
            var allValues = await _repository.GetPropertyValuesAsync(cancellationToken);
            var valueLabel = allValues.ToDictionary(v => v.Id, v => v.Description ?? v.Code ?? v.Id.ToString());
            var msgParts = new List<string>();
            foreach (var kv in blockedValuesByFeature)
            {
                var fname = itemFeatures.FirstOrDefault(f => f.Id == kv.Key)?.Name ?? ("#" + kv.Key);
                var vNames = kv.Value.Select(vid => valueLabel.TryGetValue(vid, out var n) ? n : ("#" + vid));
                msgParts.Add(fname + ": " + string.Join(", ", vNames));
            }
            throw new ArgumentException(
                "Asagidaki deger(ler) aktif kombinasyon(lar)da kullaniliyor, izinli listeden cikarilamaz — " +
                string.Join(" | ", msgParts));
        }

        await _repository.ReplaceStockFeatureLinksAsync(normalizedCode, requestedItems, cancellationToken);
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

        // Duplicate ozellik adi kontrolu (kendi disindaki kayitlarda)
        if (snapshot.Features.Any(x => x.Id != request.Id &&
                                       string.Equals(x.Name, normalizedName, StringComparison.OrdinalIgnoreCase)))
        {
            throw new ArgumentException("Ayni ozellik adi ile baska bir kayit mevcut.");
        }

        var hasValues = snapshot.Values.Any(x => x.FeatureId == request.Id);
        if (hasValues && !string.Equals(feature.DataType, ToProductDataTypeValue(request.DataType), StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Deger girilen ozelligin veri tipi degistirilemez.");
        }
        // Deger girilmis ozelligin adi da degistirilemez (referans butunlugu + UI tutarliligi)
        if (hasValues && !string.Equals(feature.Name, normalizedName, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Deger girilen ozelligin adi degistirilemez.");
        }

        var normalizedDataType = ToProductDataTypeValue(request.DataType);
        var unitOfMeasure = request.DataType == ConfigurationFieldDataType.Numeric && !string.IsNullOrWhiteSpace(request.UnitOfMeasure)
            ? request.UnitOfMeasure.Trim()
            : null;

        // Master ozellik ItemFeature tablosunda — oraya update et
        await _repository.UpdateItemFeatureAsync(
            request.Id,
            normalizedName,
            normalizedDataType,
            unitOfMeasure,
            request.VisibleInDesign,
            feature.IsActive,
            cancellationToken);
    }

    public async Task DeleteProductConfigurationFeatureAsync(int id, CancellationToken cancellationToken)
    {
        if (id <= 0)
        {
            throw new ArgumentException("Silinecek ozellik secilmelidir.");
        }

        var itemFeatures = await _repository.GetPropertiesAsync(cancellationToken);
        var feature = itemFeatures.FirstOrDefault(x => x.Id == id);

        if (feature is null)
        {
            throw new ArgumentException("Secilen ozellik bulunamadi.");
        }

        var snapshot = await GetProductConfigurationSnapshotAsync(cancellationToken);

        if (snapshot.Values.Any(x => x.FeatureId == id))
        {
            throw new ArgumentException("Degeri olan ozellik silinemez. Once tum degerleri siliniz.");
        }

        await _repository.DeleteItemFeatureAsync(id, cancellationToken);
    }

    public async Task DeleteProductConfigurationValueAsync(int id, CancellationToken cancellationToken)
    {
        if (id <= 0)
        {
            throw new ArgumentException("Silinecek deger secilmelidir.");
        }

        // VALUE rows ItemConfiguration'dan FeatureValue tablosuna tasinmis (schema migration);
        // Eski kod hala ItemConfiguration'da RecordType='VALUE' ariyordu — artik bos donuyor
        // ve "Secilen deger bulunamadi" hatasi atiyordu. Dogru kaynak: GetPropertyValuesAsync.
        var values = await _repository.GetPropertyValuesAsync(cancellationToken);
        var value = values.FirstOrDefault(v => v.Id == id);
        if (value is null)
        {
            throw new ArgumentException("Secilen deger bulunamadi.");
        }

        await _repository.DeleteProductValueAsync(id, cancellationToken);
    }

    public async Task UpdateProductConfigurationValueAsync(int id, string? description, string? aciklama, CancellationToken cancellationToken)
    {
        if (id <= 0) throw new ArgumentException("Guncellenmek istenen deger secilmelidir.");

        var records = await _repository.GetItemConfigurationsAsync(cancellationToken);
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

        var records = await _repository.GetItemConfigurationsAsync(cancellationToken);
        var config = records.FirstOrDefault(x =>
            x.Id == id &&
            string.Equals(x.RecordType, "CONFIG", StringComparison.OrdinalIgnoreCase));

        if (config is null)
        {
            throw new ArgumentException("Secilen yapilandirma bulunamadi.");
        }

        await _repository.DeleteProductConfigAsync(id, cancellationToken);
    }

    public async Task UpdateProductCombinationDescriptionAsync(int id, string? description, CancellationToken cancellationToken)
    {
        if (id <= 0)
        {
            throw new ArgumentException("Guncellenecek kombinasyon secilmelidir.");
        }
        var clean = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        await _repository.UpdateProductConfigDescriptionAsync(id, clean, cancellationToken);
    }

    public async Task CreateItemAsync(CreateItemRequest request, CancellationToken cancellationToken)
    {
        var code = request.Code.Trim();
        var name = request.Name.Trim();

        if (string.IsNullOrWhiteSpace(code))
        {
            throw new ArgumentException("Malzeme kodu zorunludur.");
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Malzeme adi zorunludur.");
        }

        var stockCards = await _repository.GetItemsAsync(cancellationToken);
        if (stockCards.Any(x => string.Equals(x.Code, code, StringComparison.OrdinalIgnoreCase)))
        {
            throw new ArgumentException("Ayni malzeme kodu ile kayit zaten mevcut.");
        }

        var normalizedBarcode = NormalizeOptionalField(request.Barcode, 50);
        EnsureBarcodeUnique(normalizedBarcode, excludeItemId: null, stockCards);

        var stockCard = new Item
        {
            Code = code,
            Name = name,
            Barcode = normalizedBarcode,
            TypeId = request.TypeId,
            UnitId = request.UnitId,
            Combinations = request.Combinations,
            TaxRate = request.TaxRate,
            TrackingType = NormalizeTrackingType(request.TrackingType) ?? "None",
            MinStock = request.MinStock,
            AutoSerial = request.AutoSerial,
            Created = DateTime.Now
        };

        var newItemId = await _repository.AddItemAsync(stockCard, cancellationToken);

        // İşlem logu — yeni malzeme kartı (ilk değer dökümüyle)
        _audit?.LogInsert("Item", newItemId, name, detail: code,
            snapshot: stockCard, snapshotIgnore: ["CompanyId"]);
    }

    public async Task SetItemActiveAsync(int itemId, bool isActive, CancellationToken cancellationToken)
    {
        if (itemId <= 0) throw new ArgumentException("Gecersiz malzeme kaydi.");

        var items = await _repository.GetItemsAsync(cancellationToken);
        var existing = items.FirstOrDefault(x => x.Id == itemId)
            ?? throw new ArgumentException("Malzeme kaydi bulunamadi.");

        if (existing.IsActive == isActive) return;   // degisiklik yoksa yazma da log da yok

        await _repository.SetItemActiveAsync(itemId, isActive, cancellationToken);

        _audit?.LogUpdate("Item", itemId, existing.Name, detail: existing.Code,
            oldSnapshot: new { existing.IsActive },
            newSnapshot: new { IsActive = isActive });
    }

    public async Task UpdateItemAsync(UpdateItemRequest request, CancellationToken cancellationToken)
    {
        if (request.ItemId <= 0)
        {
            throw new ArgumentException("Guncellenecek malzeme karti secimi zorunludur.");
        }

        var code = request.Code.Trim();
        var name = request.Name.Trim();

        if (string.IsNullOrWhiteSpace(code))
        {
            throw new ArgumentException("Malzeme kodu zorunludur.");
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Malzeme adi zorunludur.");
        }

        var stockCards = await _repository.GetItemsAsync(cancellationToken);
        var existing = stockCards.FirstOrDefault(x => x.Id == request.ItemId);
        if (existing is null)
        {
            throw new ArgumentException("Secilen malzeme karti bulunamadi.");
        }

        if (stockCards.Any(x =>
                x.Id != request.ItemId &&
                string.Equals(x.Code, code, StringComparison.OrdinalIgnoreCase)))
        {
            throw new ArgumentException("Ayni malzeme kodu ile baska bir kayit mevcut.");
        }

        var normalizedBarcode = NormalizeOptionalField(request.Barcode, 50);
        EnsureBarcodeUnique(normalizedBarcode, excludeItemId: request.ItemId, stockCards);

        var updatedItem = new Item
        {
            Id = request.ItemId,
            Code = code,
            Name = name,
            Barcode = normalizedBarcode,
            TypeId = request.TypeId,
            UnitId = request.UnitId,
            Combinations = request.Combinations,
            TaxRate = request.TaxRate,
            TrackingType = NormalizeTrackingType(request.TrackingType) ?? existing.TrackingType ?? "None",
            MinStock = request.MinStock,
            AutoSerial = request.AutoSerial,
            Created = existing.Created,
            Updated = DateTime.Now
        };

        await _repository.UpdateItemAsync(updatedItem, cancellationToken);

        // İşlem logu — yalnızca değişen alanlar (CompanyId yeni nesnede set edilmediği için hariç)
        if (_audit is not null)
        {
            try
            {
                var changes = AuditDiff.Compute(existing, updatedItem, "Item", ignore: new[] { "CompanyId" });
                _audit.LogChanges("Item", request.ItemId, name, changes);
            }
            catch { /* audit yazımı kaydı asla bozmaz */ }
        }
    }

    public async Task DeactivateItemAsync(int stockCardId, CancellationToken cancellationToken)
    {
        if (stockCardId <= 0)
        {
            throw new ArgumentException("Stok karti secimi zorunludur.");
        }

        var stockCards = await _repository.GetItemsAsync(cancellationToken);
        var stockCard = stockCards.FirstOrDefault(x => x.Id == stockCardId);
        if (stockCard is null)
        {
            throw new ArgumentException("Secilen stok karti bulunamadi.");
        }

        await _repository.DeleteItemAsync(stockCardId, cancellationToken);

        // İşlem logu — malzeme kartı silme (soft-delete)
        _audit?.LogDelete("Item", stockCardId, stockCard.Name, detail: stockCard.Code);
    }

    public async Task CreateLocationAsync(
        CreateLocationRequest request,
        CancellationToken cancellationToken)
    {
        var locationTypeCode = NormalizeLocationTypeCode(request.LocationTypeCode);
        var locationCode = NormalizeRequiredField(request.LocationCode, 50, "Lokasyon kodu");
        var locationName = NormalizeOptionalField(request.LocationName, 100);
        var sortOrder = NormalizeSortOrder(request.SortOrder);
        var maxWeightCapacity = NormalizeCapacity(request.MaxWeightCapacity, "Maksimum agirlik kapasitesi");
        var volumeCapacity = NormalizeCapacity(request.VolumeCapacity, "Hacim kapasitesi");

        var locations = await _repository.GetLocationsAsync(cancellationToken);
        ValidateParentLocation(request.ParentId, locations);

        // Hiyerarsi: child tipi parent tipinden daha alt seviyede olmali
        var typesForHierarchy = await _repository.GetLocationTypesAsync(cancellationToken);
        ValidateLocationTypeHierarchy(locationTypeCode, request.ParentId, locations, typesForHierarchy);

        // Kardes tekduzeligi (genel kural, 2026-07-25): yeni kayit, parent altindaki mevcut
        // aktif kardeslerin tipiyle eslesmeli (ilk cocuk serbest, tipi o belirler).
        ValidateSingleChildTypeConstraint(locationTypeCode, request.ParentId, excludeLocationId: null, locations, typesForHierarchy);

        if (locations.Any(x => string.Equals(x.LocationCode, locationCode, StringComparison.OrdinalIgnoreCase)))
        {
            throw new ArgumentException("Ayni lokasyon kodu ile kayit zaten mevcut.");
        }

        // Yeni eklenen lokasyon parent'inin "leaf" olma kuralini bozar — eger
        // parent'a IsMachinePark/IsStorageArea atanmissa, child eklendigi anda
        // parent artik leaf degil; flag'lerini sessizce KAPAT (data tutarliligi).
        if (request.ParentId.HasValue && request.ParentId.Value > 0)
        {
            var parent = locations.FirstOrDefault(x => x.Id == request.ParentId.Value);
            if (parent is not null && (parent.IsMachinePark || parent.IsStorageArea))
            {
                var clearedParent = new Location
                {
                    Id = parent.Id, ParentId = parent.ParentId,
                    LocationTypeCode = parent.LocationTypeCode,
                    LocationCode = parent.LocationCode,
                    LocationName = parent.LocationName,
                    SortOrder = parent.SortOrder,
                    MaxWeightCapacity = parent.MaxWeightCapacity,
                    VolumeCapacity = parent.VolumeCapacity,
                    IsActive = parent.IsActive,
                    IsMachinePark = false,
                    IsStorageArea = false,
                    // Konteyner bayraklarını KORU — child eklendiğinde parent artık leaf değil,
                    // ama bu bayraklar tam da konteyner (bölüm) lokasyon için anlamlıdır. Repo UPDATE
                    // tüm kolonları yazdığından, atlanırlarsa sessizce false'a düşerlerdi (veri kaybı).
                    IsCountReference = parent.IsCountReference,
                    IsSingleChildType = parent.IsSingleChildType,
                };
                await _repository.UpdateLocationAsync(clearedParent, cancellationToken);
            }
            // Parent artik leaf degil — item_locations atamalarini temizle
            await _repository.NullifyItemLocationsByLocationIdAsync(request.ParentId.Value, cancellationToken);
        }

        var location = new Location
        {
            ParentId = request.ParentId,
            LocationTypeCode = locationTypeCode,
            LocationCode = locationCode,
            LocationName = locationName,
            SortOrder = sortOrder,
            MaxWeightCapacity = maxWeightCapacity,
            VolumeCapacity = volumeCapacity,
            IsActive = request.IsActive,
            IsMachinePark = request.IsMachinePark,
            IsStorageArea = request.IsStorageArea,
            // 2026-07-25: UI bu iki alanı artık göstermiyor/ayarlamıyor (bkz. LocationTree.jsx).
            // IsSingleChildType kural katmanında da artık okunmuyor (kardeş tekdüzeliği koşulsuz
            // genel kural oldu, bkz. ValidateSingleChildTypeConstraint). Yeni kayıtta önceki bir
            // değer yok — request üzerinden geçer (UI hep false gönderir) → daima false.
            // Kolonlar geriye-uyumluluk için kalır (proje konvansiyonu: UI'dan kalkar, kolon kalır).
            IsCountReference = request.IsCountReference,
            IsSingleChildType = request.IsSingleChildType,
            AllowNegativeBalance = request.AllowNegativeBalance
        };

        await _repository.AddLocationAsync(location, cancellationToken);
    }

    public async Task UpdateLocationAsync(
        UpdateLocationRequest request,
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

        var locations = await _repository.GetLocationsAsync(cancellationToken);
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

        // Hiyerarsi: child tipi parent tipinden daha alt seviyede olmali
        var typesForHierarchyU = await _repository.GetLocationTypesAsync(cancellationToken);
        ValidateLocationTypeHierarchy(locationTypeCode, request.ParentId, locations, typesForHierarchyU);

        // Kardes tekduzeligi (genel kural, 2026-07-25): yalnizca gercekten tasiniyorsa (parent
        // degisti) veya tipi degisiyorsa kontrol edilir — parent/tip ayniysa (ornegin sadece
        // ad/kapasite duzenleniyorsa) sessizce atlanir; aksi halde kural genellenmeden once
        // olusmus mevcut karisik-tipli kardes verisi, bu alanla ilgisiz bir duzenlemeyi bile
        // bloke ederdi. Eski karisik veri bu sekilde DOKUNULMADAN kalir — kural yalnizca o
        // parent altina YENİ ekleme/taşıma yapılınca devreye girer.
        var parentOrTypeChanged =
            existingLocation.ParentId != request.ParentId ||
            !string.Equals(existingLocation.LocationTypeCode, locationTypeCode, StringComparison.OrdinalIgnoreCase);
        if (parentOrTypeChanged)
        {
            ValidateSingleChildTypeConstraint(locationTypeCode, request.ParentId, excludeLocationId: request.Id, locations, typesForHierarchyU);
        }

        // Bu lokasyonun kendi child'lari icin de kontrol: eger tipi degistirilirse,
        // child'larin tipinin sortOrder'i bu yeni tipinkinden buyuk olmali.
        var directChildren = locations.Where(x => x.ParentId == request.Id).ToList();
        if (directChildren.Count > 0)
        {
            var newType = typesForHierarchyU.FirstOrDefault(t =>
                string.Equals(t.Code, locationTypeCode, StringComparison.OrdinalIgnoreCase));
            if (newType is not null)
            {
                foreach (var ch in directChildren)
                {
                    var chType = typesForHierarchyU.FirstOrDefault(t =>
                        string.Equals(t.Code, ch.LocationTypeCode, StringComparison.OrdinalIgnoreCase));
                    if (chType is not null && chType.SortOrder <= newType.SortOrder)
                    {
                        throw new ArgumentException(
                            $"Bu lokasyonun tipini '{newType.Name}' yapamayiz: alt lokasyon " +
                            $"'{ch.LocationCode}' tipi '{chType.Name}' ayni veya daha ust seviyede.");
                    }
                }
            }
        }

        if (locations.Any(x =>
                x.Id != request.Id &&
                string.Equals(x.LocationCode, locationCode, StringComparison.OrdinalIgnoreCase)))
        {
            throw new ArgumentException("Ayni lokasyon kodu ile baska bir kayit mevcut.");
        }

        // Leaf-only kuralı: bu lokasyonun child'i varsa IsMachinePark/IsStorageArea
        // FALSE'a zorlanır. Sadece yaprak (alt kırılımı olmayan) lokasyonlar makine
        // parkuru veya depolama alanı olabilir.
        var hasChildren = locations.Any(x => x.ParentId == request.Id);
        var isMachinePark = hasChildren ? false : request.IsMachinePark;
        var isStorageArea = hasChildren ? false : request.IsStorageArea;
        // Eksi bakiye ayarı yalnızca yaprak (stok tutan) lokasyonda anlamlı — alt
        // kırılımı olan lokasyonda "devral" (null) durumuna zorlanır.
        var allowNegativeBalance = hasChildren ? (bool?)null : request.AllowNegativeBalance;

        // Maks 7 kırılım — yeni parent zincirinde derinlik kontrolü
        if (request.ParentId.HasValue && request.ParentId.Value > 0)
        {
            var depth = ComputeLocationDepth(request.ParentId.Value, locations);
            if (depth + 1 > 7)
            {
                throw new ArgumentException("Maksimum 7 seviye kırılım izinlidir.");
            }
        }

        var location = new Location
        {
            Id = request.Id,
            ParentId = request.ParentId,
            LocationTypeCode = locationTypeCode,
            LocationCode = locationCode,
            LocationName = locationName,
            SortOrder = sortOrder,
            MaxWeightCapacity = maxWeightCapacity,
            VolumeCapacity = volumeCapacity,
            IsActive = request.IsActive,
            IsMachinePark = isMachinePark,
            IsStorageArea = isStorageArea,
            // 2026-07-25: UI bu iki alanı artık göstermiyor/ayarlamıyor (bkz. LocationTree.jsx) —
            // IsSingleChildType kural katmanında da artık okunmuyor (kardeş tekdüzeliği koşulsuz
            // genel kural oldu, bkz. ValidateSingleChildTypeConstraint). request.IsCountReference /
            // request.IsSingleChildType artık her zaman false gelir (UI göndermiyor); bunu olduğu
            // gibi yazarsak önceden true olan legacy kayıtları her düzenlemede sessizce false'a
            // düşürürüz. Bunun yerine mevcut kaydın değeri korunur (kolon geriye-uyumluluk için
            // kalıyor — proje konvansiyonu: UI'dan kalkar, kolon ve veri kalır).
            IsCountReference = existingLocation.IsCountReference,
            IsSingleChildType = existingLocation.IsSingleChildType,
            AllowNegativeBalance = allowNegativeBalance
        };

        await _repository.UpdateLocationAsync(location, cancellationToken);
    }

    /// <summary>
    /// Tip kodunu Ad'dan tureti̇r (TR karakter -> ASCII, ucasing, alfanumerik+_).
    /// Bos kalirsa TYPE_{6hex} fallback. Cakisirsa _2, _3 … ekler.
    /// </summary>
    private static string DeriveLocationTypeCode(string name, IReadOnlyCollection<LocationType> existing)
    {
        // TR karakter eslemesi + ASCII'ye dusur
        var src = name?.Trim() ?? string.Empty;
        var sb = new System.Text.StringBuilder(src.Length);
        foreach (var ch in src.ToUpperInvariant())
        {
            char m = ch switch
            {
                'Ç' => 'C', 'Ğ' => 'G', 'İ' => 'I', 'I' => 'I',
                'Ö' => 'O', 'Ş' => 'S', 'Ü' => 'U',
                _ => ch,
            };
            if (char.IsLetterOrDigit(m)) sb.Append(m);
            else if (m == ' ' || m == '-' || m == '_') sb.Append('_');
        }
        var baseCode = sb.ToString().Trim('_');
        if (baseCode.Length == 0)
            baseCode = "TYPE_" + Guid.NewGuid().ToString("N").Substring(0, 6).ToUpperInvariant();
        if (baseCode.Length > 50) baseCode = baseCode.Substring(0, 50);

        var candidate = baseCode;
        var i = 2;
        while (existing.Any(x => string.Equals(x.Code, candidate, StringComparison.OrdinalIgnoreCase)))
        {
            var suffix = "_" + i++;
            candidate = (baseCode.Length + suffix.Length > 50)
                ? baseCode.Substring(0, 50 - suffix.Length) + suffix
                : baseCode + suffix;
        }
        return candidate;
    }

    /// <summary>
    /// Hi̇yerarsi̇ validasyonu: child lokasyonun ti̇p sortOrder'i parent lokasyonun
    /// ti̇p sortOrder'indan kesi̇nli̇kle BUYUK olmali. Esi̇t veya kucuk olamaz.
    /// Ornek: Bolum (3) altina Bolum (3) konamaz, Fabrika (1) altinda Bolum (3) olur.
    /// </summary>
    private static void ValidateLocationTypeHierarchy(
        string childTypeCode,
        int? parentLocationId,
        IReadOnlyCollection<Location> locations,
        IReadOnlyCollection<LocationType> types)
    {
        if (!parentLocationId.HasValue || parentLocationId.Value <= 0) return;
        if (string.IsNullOrWhiteSpace(childTypeCode)) return;

        var parent = locations.FirstOrDefault(x => x.Id == parentLocationId.Value);
        if (parent is null || string.IsNullOrWhiteSpace(parent.LocationTypeCode)) return;

        var childType  = types.FirstOrDefault(t =>
            string.Equals(t.Code, childTypeCode, StringComparison.OrdinalIgnoreCase));
        var parentType = types.FirstOrDefault(t =>
            string.Equals(t.Code, parent.LocationTypeCode, StringComparison.OrdinalIgnoreCase));

        if (childType is null || parentType is null) return;

        if (childType.SortOrder <= parentType.SortOrder)
        {
            var parentLabel = string.IsNullOrWhiteSpace(parent.LocationName) ? parent.LocationCode : parent.LocationName;
            throw new ArgumentException(
                $"'{childType.Name}' tipindeki bir lokasyon '{parentType.Name}' (parent: {parentLabel}) altina eklenemez. " +
                $"Hiyerarsi: child tipi parent tipinden daha alt seviyede olmalidir.");
        }
    }

    /// <summary>
    /// Kardeş tekdüzeliği — GENEL KURAL (2026-07-25, kullanıcı kararı): bir parent'ın altındaki
    /// tüm AKTİF çocuklar her zaman aynı LocationTypeCode'a sahip olmak zorundadır — ilk çocuk
    /// serbesttir (tipi o belirler), sonraki her ekleme/taşıma/tip değişikliği bu tiple eşleşmeli.
    /// Örnek: bir bölümün altına "Raf" tanımlandıysa, aynı seviyede "Raf" dışında bir tip
    /// eklenemez; "Hücre" ancak o Raf'ın altında (bir alt seviyede) olabilir. Bu kural artık
    /// bayrak (eski adıyla IsSingleChildType) koşuluna bağlı DEĞİL, her parent için koşulsuz
    /// uygulanır. Parent-child HİYERARŞİ sırası (bölüm→raf→hücre) ValidateLocationTypeHierarchy'nin
    /// işidir — bu metod yalnızca aynı seviyedeki (sibling) kırılımların tek tip olmasını denetler.
    /// Karşılaştırma tip KODU üzerinden yapılır (ad değil). excludeLocationId, update sırasında
    /// düzenlenen kaydın kendisini sibling kümesinden çıkarmak için kullanılır (kendi eski tipiyle
    /// kendine çakışma raporlamasın). Kural genellenmeden önce oluşmuş karışık-tipli mevcut
    /// kardeş grupları bu kontrolle GERİYE DÖNÜK bloklanmaz — yalnızca o parent altına yeni bir
    /// ekleme/taşıma/tip değişikliği denendiğinde devreye girer (bkz. çağıran kod).
    /// </summary>
    private static void ValidateSingleChildTypeConstraint(
        string childTypeCode,
        int? parentLocationId,
        int? excludeLocationId,
        IReadOnlyCollection<Location> locations,
        IReadOnlyCollection<LocationType> types)
    {
        if (!parentLocationId.HasValue || parentLocationId.Value <= 0) return;
        if (string.IsNullOrWhiteSpace(childTypeCode)) return;

        var parent = locations.FirstOrDefault(x => x.Id == parentLocationId.Value);
        if (parent is null) return;

        // Mevcut aktif kardeşlerden (kendisi hariç) yeni tiple çakışan ilk kayıt — varsa
        // parent zaten homojen bir tip belirlemiş demektir, yeni/taşınan kayıt onunla eşleşmeli.
        var conflictingSiblingTypeCode = locations
            .Where(x => x.ParentId == parentLocationId.Value && x.IsActive)
            .Where(x => !excludeLocationId.HasValue || x.Id != excludeLocationId.Value)
            .Select(x => x.LocationTypeCode)
            .FirstOrDefault(code => !string.Equals(code, childTypeCode, StringComparison.OrdinalIgnoreCase));

        if (conflictingSiblingTypeCode is null) return; // hiç kardeş yok veya hepsi zaten aynı tip

        var parentLabel = string.IsNullOrWhiteSpace(parent.LocationName) ? parent.LocationCode : parent.LocationName;
        var existingTypeName = types.FirstOrDefault(t =>
            string.Equals(t.Code, conflictingSiblingTypeCode, StringComparison.OrdinalIgnoreCase))?.Name
            ?? conflictingSiblingTypeCode;

        throw new ArgumentException(
            $"'{parentLabel}' altında zaten '{existingTypeName}' tipinde kırılım var — aynı seviyeye " +
            $"yalnız '{existingTypeName}' eklenebilir.");
    }

    /// <summary>
    /// ParentId'den koke kadar derinligi hesaplar (1 = root, 2 = altinda, …).
    /// Maks 7 kirilim kontrolunu Update sirasinda yapmak icin kullanilir.
    /// </summary>
    private static int ComputeLocationDepth(int locationId, IReadOnlyCollection<Location> all)
    {
        var depth = 1;
        var byId = all.ToDictionary(x => x.Id);
        var current = byId.TryGetValue(locationId, out var n) ? n : null;
        var guard = 0;
        while (current?.ParentId is int pid && byId.TryGetValue(pid, out var parent) && guard++ < 50)
        {
            depth++;
            current = parent;
        }
        return depth;
    }

    public async Task DeleteLocationAsync(int locationId, CancellationToken cancellationToken)
    {
        if (locationId <= 0)
        {
            throw new ArgumentException("Lokasyon secimi zorunludur.");
        }

        var locations = await _repository.GetLocationsAsync(cancellationToken);
        var existingLocation = locations.FirstOrDefault(x => x.Id == locationId);
        if (existingLocation is null)
        {
            throw new ArgumentException("Secilen lokasyon bulunamadi.");
        }

        if (locations.Any(x => x.ParentId == locationId))
        {
            throw new ArgumentException("Secilen lokasyonun alt kirilimlari var. Once alt kirilimlari siliniz.");
        }

        var label = string.IsNullOrWhiteSpace(existingLocation.LocationName)
            ? existingLocation.LocationCode
            : $"{existingLocation.LocationCode} — {existingLocation.LocationName}";

        // FK kontrolü — Machine.LocationId NOT NULL FK var; bu lokasyon herhangi
        // bir makine tarafindan referansli ise SQL FK violation'a takiliriz.
        // Once kontrol et, kullaniciya "hangi makineler bagli" mesaji ver.
        var machines = await _repository.GetMachinesAsync(cancellationToken);
        var blockingMachines = machines
            .Where(m => m.LocationId == locationId)
            .Take(5)
            .Select(m => m.Name)
            .ToList();
        if (blockingMachines.Count > 0)
        {
            var blockingCount = machines.Count(m => m.LocationId == locationId);
            var sample = string.Join(", ", blockingMachines);
            var suffix = blockingCount > blockingMachines.Count ? $" (+{blockingCount - blockingMachines.Count} daha)" : "";
            throw new ArgumentException(
                $"'{label}' lokasyonu {blockingCount} makine tarafindan kullaniliyor; " +
                $"once makineleri baska bir lokasyona tasiyin. Ornek: {sample}{suffix}");
        }

        // 2026-08-26: FK_Personnel_Location eklendi. Machine'in aksine burada NULL'lama
        // (nullify) tercih edilmedi — personelin lokasyon atamasini sessizce silmek CLAUDE.md
        // "sessiz kirik" #3 (kullanici verisinin sessizce kaybolmasi) sinifina girer; bunun
        // yerine makine guard'iyla ayni desende reddedip nerede/kac kisi oldugunu soyleriz.
        var (personnelCount, personnelSample) = await _repository.GetPersonnelUsageByLocationIdAsync(locationId, cancellationToken);
        if (personnelCount > 0)
        {
            var sample = string.Join(", ", personnelSample);
            var suffix = personnelCount > personnelSample.Count ? $" (+{personnelCount - personnelSample.Count} daha)" : "";
            throw new ArgumentException(
                $"'{label}' lokasyonu {personnelCount} personel tarafindan kullaniliyor; " +
                $"once personelin lokasyonunu guncelleyin. Ornek: {sample}{suffix}");
        }

        await _repository.NullifyItemLocationsByLocationIdAsync(locationId, cancellationToken);
        await _repository.NullifyLocationHistoricalFkRefsAsync(locationId, cancellationToken);
        await _repository.DeleteLocationAsync(locationId, cancellationToken);
    }

    // ── Machine ────────────────────────────────────────────────────────────
    public async Task<IReadOnlyCollection<MachineDto>> GetMachinesAsync(CancellationToken cancellationToken)
    {
        var machines = await _repository.GetMachinesAsync(cancellationToken);
        var locations = await _repository.GetLocationsAsync(cancellationToken);
        var locationLookup = locations.ToDictionary(x => x.Id);

        return machines.Select(m =>
        {
            locationLookup.TryGetValue(m.LocationId, out var loc);
            return new MachineDto(
                m.Id,
                m.LocationId,
                loc?.LocationCode,
                loc?.LocationName,
                m.Code,
                m.Name,
                m.HourlyCapacity,
                m.SortOrder,
                m.IsActive);
        }).ToArray();
    }

    public async Task<int> CreateMachineAsync(CreateMachineRequest request, CancellationToken cancellationToken)
    {
        if (request.LocationId <= 0)
            throw new ArgumentException("Lokasyon secimi zorunludur.");
        if (string.IsNullOrWhiteSpace(request.Name))
            throw new ArgumentException("Makine adi zorunludur.");

        var locations = await _repository.GetLocationsAsync(cancellationToken);
        if (locations.All(l => l.Id != request.LocationId))
            throw new ArgumentException("Secilen lokasyon bulunamadi.");

        var existing = await _repository.GetMachinesAsync(cancellationToken);
        var name = request.Name.Trim();

        // Ayni isimli makine kontrolu
        if (existing.Any(m => string.Equals(m.Name?.Trim(), name, StringComparison.OrdinalIgnoreCase)))
        {
            throw new ArgumentException($"Aynı isimde başka bir makine zaten tanımlı: '{name}'");
        }

        // Code DB'de var ama UI'dan kaldirildi — auto-uretilir (MAC-{6-hex})
        var code = "MAC-" + Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
        for (var attempt = 0; attempt < 5 && existing.Any(m => string.Equals(m.Code, code, StringComparison.OrdinalIgnoreCase)); attempt++)
        {
            code = "MAC-" + Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
        }

        var machine = new Domain.Entities.Machine
        {
            LocationId = request.LocationId,
            Code = code,
            Name = name,
            HourlyCapacity = request.HourlyCapacity,
            SortOrder = request.SortOrder < 0 ? 0 : request.SortOrder,
            IsActive = request.IsActive
        };
        var newMachineId = await _repository.AddMachineAsync(machine, cancellationToken);

        // İşlem logu — yeni makine (ilk değer dökümüyle)
        _audit?.LogInsert("Machine", newMachineId, name, snapshot: machine);

        return newMachineId;
    }

    public async Task UpdateMachineAsync(UpdateMachineRequest request, CancellationToken cancellationToken)
    {
        if (request.Id <= 0)
            throw new ArgumentException("Makine secimi zorunludur.");
        if (request.LocationId <= 0)
            throw new ArgumentException("Lokasyon secimi zorunludur.");
        if (string.IsNullOrWhiteSpace(request.Name))
            throw new ArgumentException("Makine adi zorunludur.");

        var locations = await _repository.GetLocationsAsync(cancellationToken);
        if (locations.All(l => l.Id != request.LocationId))
            throw new ArgumentException("Secilen lokasyon bulunamadi.");

        var all = await _repository.GetMachinesAsync(cancellationToken);
        var existingMachine = all.FirstOrDefault(m => m.Id == request.Id);
        if (existingMachine is null)
            throw new ArgumentException("Guncellenecek makine bulunamadi.");

        var name = request.Name.Trim();

        // Ayni isimli baska makine var mi (kendisi haric)
        if (all.Any(m => m.Id != request.Id &&
                         string.Equals(m.Name?.Trim(), name, StringComparison.OrdinalIgnoreCase)))
        {
            throw new ArgumentException($"Aynı isimde başka bir makine zaten tanımlı: '{name}'");
        }

        // Mevcut kodu koru (UI'dan gelmiyor)
        var code = existingMachine.Code;

        var machine = new Domain.Entities.Machine
        {
            Id = request.Id,
            LocationId = request.LocationId,
            Code = code,
            Name = name,
            HourlyCapacity = request.HourlyCapacity,
            SortOrder = request.SortOrder < 0 ? 0 : request.SortOrder,
            IsActive = request.IsActive
        };
        await _repository.UpdateMachineAsync(machine, cancellationToken);

        // İşlem logu — yalnızca değişen alanlar (CompanyId yeni nesnede set edilmediği için hariç)
        if (_audit is not null)
        {
            try
            {
                var changes = AuditDiff.Compute(existingMachine, machine, "Machine", ignore: new[] { "CompanyId" });
                _audit.LogChanges("Machine", request.Id, name, changes);
            }
            catch { /* audit yazımı kaydı asla bozmaz */ }
        }
    }

    public async Task DeleteMachineAsync(int machineId, CancellationToken cancellationToken)
    {
        if (machineId <= 0)
            throw new ArgumentException("Makine secimi zorunludur.");

        // İşlem logu için silinen makinenin adını silmeden ÖNCE al (okunamazsa Id ile loglanır)
        string? machineName = null;
        if (_audit is not null)
        {
            try
            {
                var machines = await _repository.GetMachinesAsync(cancellationToken);
                machineName = machines.FirstOrDefault(m => m.Id == machineId)?.Name;
            }
            catch { }
        }

        await _repository.DeleteMachineAsync(machineId, cancellationToken);

        _audit?.LogDelete("Machine", machineId, machineName ?? ("#" + machineId));
    }

    public async Task CreateUnitAsync(
        CreateUnitRequest request,
        CancellationToken cancellationToken)
    {
        var unitCode = NormalizeMeasureUnitCode(request.Code);
        var unitName = NormalizeRequiredField(request.Name, 100, "Olcu birimi adi");
        var sortOrder = NormalizeSortOrder(request.SortOrder);

        var definitions = await _repository.GetUnitsAsync(cancellationToken);
        if (definitions.Any(x => string.Equals(x.Code, unitCode, StringComparison.OrdinalIgnoreCase)))
        {
            throw new ArgumentException("Ayni olcu birimi kodu ile kayit zaten mevcut.");
        }

        var definition = new Unit
        {
            Code = unitCode,
            Name = unitName,
            IntlCode = string.IsNullOrWhiteSpace(request.IntlCode) ? null : request.IntlCode.Trim(),
            SortOrder = sortOrder,
            IsActive = request.IsActive
        };

        await _repository.AddUnitAsync(definition, cancellationToken);
    }

    public async Task UpdateUnitAsync(
        UpdateUnitRequest request,
        CancellationToken cancellationToken)
    {
        if (request.Id <= 0)
        {
            throw new ArgumentException("Olcu birimi secimi zorunludur.");
        }

        var unitCode = NormalizeMeasureUnitCode(request.Code);
        var unitName = NormalizeRequiredField(request.Name, 100, "Olcu birimi adi");
        var sortOrder = NormalizeSortOrder(request.SortOrder);

        var definitions = await _repository.GetUnitsAsync(cancellationToken);
        var existing = definitions.FirstOrDefault(x => x.Id == request.Id);
        if (existing is null)
        {
            throw new ArgumentException("Secilen olcu birimi bulunamadi.");
        }

        if (definitions.Any(x =>
                x.Id != request.Id &&
                string.Equals(x.Code, unitCode, StringComparison.OrdinalIgnoreCase)))
        {
            throw new ArgumentException("Ayni olcu birimi kodu ile baska bir kayit mevcut.");
        }

        var definition = new Unit
        {
            Id = request.Id,
            Code = unitCode,
            Name = unitName,
            IntlCode = string.IsNullOrWhiteSpace(request.IntlCode) ? null : request.IntlCode.Trim(),
            SortOrder = sortOrder,
            IsActive = request.IsActive
        };

        await _repository.UpdateUnitAsync(definition, cancellationToken);
    }

    public async Task DeleteUnitAsync(int id, CancellationToken cancellationToken)
    {
        if (id <= 0)
        {
            throw new ArgumentException("Olcu birimi secimi zorunludur.");
        }

        var definitions = await _repository.GetUnitsAsync(cancellationToken);
        var existing = definitions.FirstOrDefault(x => x.Id == id);
        if (existing is null)
        {
            throw new ArgumentException("Secilen olcu birimi bulunamadi.");
        }

        // 2026-08-26: Onceden "MaterialDefinitions tablosunda unit_name yok" gerekcesiyle
        // devre disi birakilmis (if(false)) olu guard — Items.UnitId zaten yillardir gercek
        // bir int FK (bkz. FK_Items_Unit) ve kullanimda bir birim silinirse SQL FK ihlali
        // veriyordu. Burada en yaygin/dogrudan kullanim (malzeme karti) proaktif kontrol
        // edilir; ItemUnits/ItemKitLine/OperationMachineTime/DocumentLine/InventoryCountLine/
        // WorkOrder gibi diger 6 FK kaynagi icin genel FK-ihlali guvenlik agi (bkz.
        // SqlExceptionMessages + ApiExceptionMiddleware) devreye girer — hepsini burada tek
        // tek saymak asiri karmasiklik olurdu (KISS).
        var stockCards = await _repository.GetItemsAsync(cancellationToken);
        var usingItems = stockCards.Where(x => x.UnitId == id).Take(5).Select(x => x.Name).ToList();
        if (usingItems.Count > 0)
        {
            var totalUsing = stockCards.Count(x => x.UnitId == id);
            var sample = string.Join(", ", usingItems);
            var suffix = totalUsing > usingItems.Count ? $" (+{totalUsing - usingItems.Count} daha)" : "";
            throw new ArgumentException(
                $"'{existing.Code}' olcu birimi {totalUsing} malzeme kartinda kullaniliyor; " +
                $"once malzeme kartlarini baska bir birime tasiyin. Ornek: {sample}{suffix}");
        }

        await _repository.DeleteUnitAsync(id, cancellationToken);
    }

    public Task<IReadOnlyCollection<int>> GetUsedFeatureIdsInCombinationsAsync(int itemId, CancellationToken cancellationToken)
        => _repository.GetUsedFeatureIdsInCombinationsAsync(itemId, cancellationToken);

    public Task<IReadOnlyCollection<(int FeatureId, int ValueId)>> GetUsedFeatureValueIdsInCombinationsAsync(int itemId, CancellationToken cancellationToken)
        => _repository.GetUsedFeatureValueIdsInCombinationsAsync(itemId, cancellationToken);

    public Task<int> GetCombinationCountForItemAsync(int itemId, CancellationToken cancellationToken)
        => _repository.GetCombinationCountForItemAsync(itemId, cancellationToken);

    public async Task<IReadOnlyCollection<ItemUnitDto>> GetItemUnitsAsync(int itemId, CancellationToken cancellationToken)
    {
        var items = await _repository.GetItemUnitsAsync(itemId, cancellationToken);
        return items.Select(x => new ItemUnitDto(x.Id, x.ItemId, x.LineNo, x.UnitId, x.Multiplier)).ToList();
    }

    public async Task SaveItemUnitsAsync(int itemId, IReadOnlyCollection<SaveItemUnitItem> items, CancellationToken cancellationToken)
    {
        var conversions = items.Select(x => new ItemUnit
        {
            ItemId = itemId,
            UnitId = x.UnitId,
            Multiplier = x.Multiplier,
        }).ToList();
        await _repository.SaveItemUnitsAsync(itemId, conversions, cancellationToken);
    }

    public async Task<IReadOnlyCollection<ItemLocationDto>> GetItemLocationsAsync(int itemId, CancellationToken cancellationToken)
    {
        var links = await _repository.GetItemLocationsAsync(itemId, cancellationToken);
        if (links.Count == 0) return Array.Empty<ItemLocationDto>();

        // Lokasyon bilgilerini tek seferde cek (join yerine in-memory map)
        var allLocations = await _repository.GetLocationsAsync(cancellationToken);
        var locMap = allLocations.ToDictionary(l => l.Id);

        return links.Select(l =>
        {
            var loc = l.LocationId.HasValue ? locMap.GetValueOrDefault(l.LocationId.Value) : null;
            return new ItemLocationDto(
                l.Id, l.ItemId, l.LocationId,
                loc?.LocationCode ?? "",
                loc?.LocationName,
                loc?.LocationTypeCode ?? "",
                l.IsDefault, l.SortOrder, l.MinStock);
        }).ToList();
    }

    public async Task<IReadOnlyCollection<LocationTypeDto>> GetLocationTypesAsync(CancellationToken cancellationToken)
    {
        var items = await _repository.GetLocationTypesAsync(cancellationToken);
        return items.Select(x => new LocationTypeDto(x.Id, x.Code, x.Name, x.SortOrder, x.IsActive)).ToList();
    }

    public async Task<int> SaveLocationTypeAsync(SaveLocationTypeRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            throw new ArgumentException("Tip adi zorunludur.");

        var trimmedName = request.Name.Trim();
        var existing = await _repository.GetLocationTypesAsync(cancellationToken);

        // Ad uniqueness (kullanici kod girmiyor — uniqueness ad uzerinden)
        var nameClash = existing.FirstOrDefault(x =>
            string.Equals(x.Name?.Trim(), trimmedName, StringComparison.OrdinalIgnoreCase) &&
            (!request.Id.HasValue || x.Id != request.Id.Value));
        if (nameClash != null)
            throw new ArgumentException($"Ayni isimde baska bir tip zaten tanimli: '{trimmedName}'");

        // Kod auto-derive: update'te eski kodu koru, yeni'de ad'dan turet
        string normalizedCode;
        if (request.Id.HasValue)
        {
            var current = existing.FirstOrDefault(x => x.Id == request.Id.Value);
            normalizedCode = current?.Code ?? DeriveLocationTypeCode(trimmedName, existing);
        }
        else if (!string.IsNullOrWhiteSpace(request.Code))
        {
            normalizedCode = request.Code.Trim().ToUpperInvariant();
        }
        else
        {
            normalizedCode = DeriveLocationTypeCode(trimmedName, existing);
        }

        var clash = existing.FirstOrDefault(x =>
            string.Equals(x.Code, normalizedCode, StringComparison.OrdinalIgnoreCase) &&
            (!request.Id.HasValue || x.Id != request.Id.Value));
        if (clash != null)
            throw new ArgumentException($"'{normalizedCode}' kodu zaten kullaniliyor.");

        // Guncelleme + kod degisimi -> Location.LocationTypeCode'u da cascade guncelle
        string? oldCode = null;
        if (request.Id.HasValue)
        {
            var current = existing.FirstOrDefault(x => x.Id == request.Id.Value);
            if (current != null && !string.Equals(current.Code, normalizedCode, StringComparison.OrdinalIgnoreCase))
                oldCode = current.Code;
        }

        var entity = new LocationType
        {
            Id = request.Id ?? 0,
            Code = normalizedCode,
            Name = trimmedName,
            SortOrder = request.SortOrder,
            IsActive = request.IsActive,
        };
        var savedId = await _repository.UpsertLocationTypeAsync(entity, cancellationToken);

        if (oldCode != null)
        {
            await _repository.RenameLocationTypeCodeAsync(oldCode, normalizedCode, cancellationToken);
        }

        return savedId;
    }

    public async Task<(bool Success, string? Error)> DeleteLocationTypeAsync(int id, CancellationToken cancellationToken)
    {
        var types = await _repository.GetLocationTypesAsync(cancellationToken);
        var target = types.FirstOrDefault(x => x.Id == id);
        if (target == null) return (false, "Tip bulunamadi.");

        var usageCount = await _repository.CountLocationsOfTypeAsync(target.Code, cancellationToken);
        if (usageCount > 0)
        {
            // Engelleyen lokasyonlari bul ve ilk birkacini listele (kullaniciya ipucu)
            var allLocations = await _repository.GetLocationsAsync(cancellationToken);
            var blockers = allLocations
                .Where(l => string.Equals(l.LocationTypeCode, target.Code, StringComparison.OrdinalIgnoreCase))
                .Take(5)
                .Select(l => string.IsNullOrWhiteSpace(l.LocationName) ? l.LocationCode : $"{l.LocationCode} - {l.LocationName}")
                .ToList();

            var sample = blockers.Count > 0 ? " Ornek: " + string.Join(", ", blockers) : "";
            var suffix = usageCount > blockers.Count ? $" (+{usageCount - blockers.Count} daha)" : "";
            return (false,
                $"'{target.Name}' ({target.Code}) tipi {usageCount} lokasyonda kullaniliyor; " +
                $"once bu lokasyonlari baska tipe tasiyin veya silin.{sample}{suffix}");
        }

        await _repository.DeleteLocationTypeAsync(id, cancellationToken);
        return (true, null);
    }

    public async Task SaveItemLocationsAsync(int itemId, IReadOnlyCollection<SaveItemLocationItem> items, CancellationToken cancellationToken)
    {
        if (itemId <= 0) throw new ArgumentException("Malzeme karti secimi zorunludur.");
        // Dublicate location_id temizle — ilk gordugumuzu koru
        var seen = new HashSet<int>();
        var deduped = new List<SaveItemLocationItem>();
        foreach (var i in items)
        {
            if (i.LocationId <= 0) continue;
            if (seen.Add(i.LocationId)) deduped.Add(i);
        }

        var links = deduped.Select(x => new ItemLocation
        {
            ItemId = itemId,
            LocationId = x.LocationId,
            IsDefault = x.IsDefault,
            MinStock = x.MinStock,
        }).ToList();

        // Hicbiri default degilse, ilk satiri varsayilan yap
        if (links.Count > 0 && !links.Any(l => l.IsDefault))
        {
            var first = links[0];
            links[0] = new ItemLocation { ItemId = first.ItemId, LocationId = first.LocationId, IsDefault = true };
        }

        await _repository.SaveItemLocationsAsync(itemId, links, cancellationToken);
    }

    public Task<IReadOnlyCollection<string>> GetItemDocumentLocksAsync(int itemId, CancellationToken cancellationToken)
        => _repository.GetItemDocumentLocksAsync(itemId, cancellationToken);

    // 2026-07-25 — audit eklendi (CLAUDE.md "İşlem Log Modülü" zorunluluğu). Bu metod hem
    // eski MaterialController.SaveItemDocumentLocks hem yeni ItemDocumentLockController'ın
    // tekil kaydet ucu tarafından paylaşılır — audit burada olunca ikisi de kapsanır.
    // FilterValid: DB'ye yalnız bilinen 12 koddan biri yazılır (garbage/typo korunmaz);
    // mevcut 9-kodluk MaterialCardEdit çağrısı için davranış değişmez (hepsi geçerli kod).
    public async Task SaveItemDocumentLocksAsync(int itemId, IReadOnlyCollection<string> docTypes, CancellationToken cancellationToken)
    {
        var oldDocTypes = await _repository.GetItemDocumentLocksAsync(itemId, cancellationToken);
        var newDocTypes = ItemDocumentLockTypes.FilterValid(docTypes);
        await _repository.SaveItemDocumentLocksAsync(itemId, newDocTypes, cancellationToken);

        if (_audit is not null)
        {
            try
            {
                var change = BuildDocumentLockChange(oldDocTypes, newDocTypes);
                if (change is not null)
                {
                    var stockCards = await _repository.GetItemsAsync(cancellationToken);
                    var itemName = stockCards.FirstOrDefault(x => x.Id == itemId)?.Name;
                    _audit.LogChanges("Item", itemId, itemName, new[] { change });
                }
            }
            catch { /* audit yazımı kaydı asla bozmaz */ }
        }
    }

    public Task<IReadOnlyCollection<int>> GetLockedItemIdsByDocTypeAsync(string docType, CancellationToken cancellationToken)
        => _repository.GetLockedItemIdsByDocTypeAsync(docType, cancellationToken);

    public Task<IReadOnlyDictionary<int, IReadOnlyCollection<string>>> GetAllItemDocumentLocksGroupedAsync(CancellationToken cancellationToken)
        => _repository.GetAllItemDocumentLocksGroupedAsync(cancellationToken);

    public Task<IReadOnlyDictionary<string, int>> GetItemDocumentLockCountsByDocTypeAsync(CancellationToken cancellationToken)
        => _repository.GetItemDocumentLockCountsByDocTypeAsync(cancellationToken);

    public async Task<(IReadOnlyCollection<ItemDto> Items, int TotalCount)> GetItemsPagedByDocumentLockAsync(
        string? lockedDocType, string? search, int offset, int pageSize, CancellationToken cancellationToken)
    {
        var (cards, totalCount) = await _repository.GetItemsPagedByDocumentLockAsync(lockedDocType, search, offset, pageSize, cancellationToken);
        var dtos = cards.Select(x => new ItemDto(
            x.Id, x.Code, x.Name,
            x.TypeId, x.IsActive, x.Created, x.Updated,
            x.UnitId, x.Combinations, x.TaxRate,
            Barcode: x.Barcode)).ToList();
        return (dtos, totalCount);
    }

    public async Task<BulkItemDocumentLockResultDto> BulkSetItemDocumentLocksAsync(
        IReadOnlyCollection<int> itemIds, IReadOnlyCollection<string> docTypes, bool isLock, CancellationToken cancellationToken)
    {
        var distinctItemIds = (itemIds ?? Array.Empty<int>()).Where(id => id > 0).Distinct().ToList();
        var requestedDocTypes = ItemDocumentLockTypes.FilterValid(docTypes);
        if (distinctItemIds.Count == 0 || requestedDocTypes.Count == 0)
            return new BulkItemDocumentLockResultDto(distinctItemIds.Count, 0, requestedDocTypes);

        // Audit title icin tek seferde tum isim/kod bilgisini yukle (N+1 onleme — bkz. rapor 2026-05-17 madde 3.10 ruhu).
        var allItems = await _repository.GetItemsAsync(cancellationToken);
        var namesById = allItems.ToDictionary(x => x.Id, x => x.Name);
        // Mevcut kilit setlerini TEK sorguda al (N ayrı GetItemDocumentLocksAsync yerine) —
        // board listesi icin zaten var olan grouped okumayı burada da paylaşıyoruz.
        var lockMap = await _repository.GetAllItemDocumentLocksGroupedAsync(cancellationToken);

        var updatedCount = 0;
        foreach (var itemId in distinctItemIds)
        {
            var oldDocTypes = lockMap.TryGetValue(itemId, out var existing) ? existing : Array.Empty<string>();
            var oldSet = new HashSet<string>(oldDocTypes, StringComparer.OrdinalIgnoreCase);
            var newSet = new HashSet<string>(oldSet, StringComparer.OrdinalIgnoreCase);
            if (isLock) newSet.UnionWith(requestedDocTypes);
            else newSet.ExceptWith(requestedDocTypes);

            if (newSet.SetEquals(oldSet)) continue; // bu item icin fiili degisiklik yok

            var newDocTypes = newSet.ToList();
            await _repository.SaveItemDocumentLocksAsync(itemId, newDocTypes, cancellationToken);
            updatedCount++;

            if (_audit is not null)
            {
                try
                {
                    var change = BuildDocumentLockChange(oldDocTypes, newDocTypes);
                    if (change is not null)
                    {
                        namesById.TryGetValue(itemId, out var itemName);
                        _audit.LogChanges("Item", itemId, itemName, new[] { change },
                            detail: isLock ? "Toplu kilitleme" : "Toplu kilit kaldırma");
                    }
                }
                catch { /* audit yazımı kaydı asla bozmaz */ }
            }
        }

        return new BulkItemDocumentLockResultDto(distinctItemIds.Count, updatedCount, requestedDocTypes);
    }

    /// <summary>
    /// Belge kilidi eski/yeni kod kümesini tek alanlık okunur bir audit diff'ine çevirir.
    /// Değişiklik yoksa null (audit gürültüsü üretme).
    /// </summary>
    private static AuditFieldChange? BuildDocumentLockChange(
        IReadOnlyCollection<string> oldCodes, IReadOnlyCollection<string> newCodes)
    {
        var oldSet = new HashSet<string>(oldCodes ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);
        var newSet = new HashSet<string>(newCodes ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);
        if (oldSet.SetEquals(newSet)) return null;

        static string Describe(IEnumerable<string> codes)
        {
            var labels = codes.Select(ItemDocumentLockTypes.LabelFor).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
            return labels.Count > 0 ? string.Join(", ", labels) : "(yok)";
        }

        return new AuditFieldChange("DocumentLocks", "Belge Kilitleri", Describe(oldSet), Describe(newSet));
    }

    public async Task ConfigureItemAsync(ConfigureItemRequest request, CancellationToken cancellationToken)
    {
        if (request.ItemId <= 0)
        {
            throw new ArgumentException("Malzeme karti secimi zorunludur.");
        }

        var stockCards = await _repository.GetItemsAsync(cancellationToken);
        var stockCard = stockCards.FirstOrDefault(x => x.Id == request.ItemId);
        if (stockCard is null)
        {
            throw new ArgumentException("Secilen malzeme karti bulunamadi.");
        }

        if (!stockCard.IsActive)
        {
            throw new ArgumentException("Pasif malzeme karti icin yapilandirma ayari degistirilemez.");
        }

        await _repository.UpdateItemConfigurableStatusAsync(stockCard.Id, request.IsConfigurable, cancellationToken);

        if (!request.IsConfigurable)
        {
            return;
        }

        var selectedPropertyIds = request.FeatureIds
            .Where(x => x > 0)
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
            .Where(x => x.ItemId == stockCard.Id && x.IsActive)
            .Select(x => x.FeatureId)
            .ToHashSet();

        foreach (var propertyId in selectedPropertyIds)
        {
            if (alreadyLinkedPropertyIds.Contains(propertyId))
            {
                continue;
            }

            var mapping = new ItemFeatureMapping
            {
                ItemId = stockCard.Id,
                FeatureId = propertyId,
                FeatureValueId = null
            };

            await _repository.AddStockPropertyMappingAsync(mapping, cancellationToken);
        }
    }

    public async Task CreatePropertyAsync(CreateFeatureRequest request, CancellationToken cancellationToken)
    {
        var name = request.Name.Trim();

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Ozellik aciklamasi zorunludur.");
        }

        if (!Enum.IsDefined(request.DataType))
        {
            throw new ArgumentException("Ozellik veri tipi gecersiz.");
        }

        var properties = await _repository.GetPropertiesAsync(cancellationToken);
        if (properties.Any(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            throw new ArgumentException("Ayni ozellik adi ile kayit zaten mevcut.");
        }

        var property = new ItemFeature
        {
            Name = name,
            DataType = request.DataType
            // CompanyId repository tarafinda ResolveCurrentCompanyId() ile doldurulur
        };

        await _repository.AddPropertyAsync(property, cancellationToken);
    }

    public async Task CreateStockPropertyLinkAsync(
        CreateItemPropertyLinkRequest request,
        CancellationToken cancellationToken)
    {
        if (request.ItemId <= 0)
        {
            throw new ArgumentException("Stok karti secimi zorunludur.");
        }

        if (request.PropertyId <= 0)
        {
            throw new ArgumentException("Ozellik secimi zorunludur.");
        }

        var stockCards = await _repository.GetItemsAsync(cancellationToken);
        var properties = await _repository.GetPropertiesAsync(cancellationToken);
        var mappings = await _repository.GetStockPropertyMappingsAsync(cancellationToken);

        var stockCard = stockCards.FirstOrDefault(x => x.Id == request.ItemId);
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
            x.ItemId == stockCard.Id &&
            x.FeatureId == property.Id &&
            x.IsActive);

        if (hasExistingLink)
        {
            throw new ArgumentException("Bu stok karti icin secilen ozellik zaten eslestirilmis.");
        }

        var mapping = new ItemFeatureMapping
        {
            ItemId = stockCard.Id,
            FeatureId = property.Id,
            FeatureValueId = null
        };

        await _repository.AddStockPropertyMappingAsync(mapping, cancellationToken);
    }

    public async Task CreatePropertyValueAsync(CreateFeatureValueRequest request, CancellationToken cancellationToken)
    {
        var valueCode = request.Code.Trim().ToUpperInvariant();
        var valueDescription = request.Description.Trim();

        if (request.PropertyId <= 0)
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

        var propertyValue = new FeatureValue
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
        CreateItemFeatureMappingRequest request,
        CancellationToken cancellationToken)
    {
        if (request.ItemId <= 0)
        {
            throw new ArgumentException("Stok karti secimi zorunludur.");
        }

        if (request.FeatureId <= 0)
        {
            throw new ArgumentException("Ozellik secimi zorunludur.");
        }

        if (request.FeatureValueId <= 0)
        {
            throw new ArgumentException("Ozellik degeri secimi zorunludur.");
        }

        var stockCards = await _repository.GetItemsAsync(cancellationToken);
        var properties = await _repository.GetPropertiesAsync(cancellationToken);
        var propertyValues = await _repository.GetPropertyValuesAsync(cancellationToken);
        var mappings = await _repository.GetStockPropertyMappingsAsync(cancellationToken);

        var stockCard = stockCards.FirstOrDefault(x => x.Id == request.ItemId);
        if (stockCard is null)
        {
            throw new ArgumentException("Secilen stok karti bulunamadi.");
        }

        var property = properties.FirstOrDefault(x => x.Id == request.FeatureId);
        if (property is null)
        {
            throw new ArgumentException("Secilen ozellik bulunamadi.");
        }

        var selectedValue = propertyValues.FirstOrDefault(x => x.Id == request.FeatureValueId);
        if (selectedValue is null || selectedValue.PropertyId != property.Id)
        {
            throw new ArgumentException("Secilen ozellik degeri bu ozellige ait degil.");
        }

        var existingMappings = mappings
            .Where(x => x.ItemId == stockCard.Id && x.FeatureId == property.Id && x.IsActive)
            .ToArray();

        if (existingMappings.Length == 0)
        {
            throw new ArgumentException("Once stok karti ve ozelligi eslestiriniz.");
        }

        var existingLink = existingMappings
            .OrderByDescending(x => x.CreatedAt)
            .First();

        await _repository.UpdateStockPropertyMappingValueAsync(
            existingLink.Id,
            selectedValue.Id,
            cancellationToken);
    }

    private static string NormalizePropertyValue(
        ConfigurationFieldDataType dataType,
        CreateFeatureValueRequest request)
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
        // LocationTypes tablosu artik dinamik — kullanici kendi tip tanimlarini olusturabilir.
        // Validation: bos olmasin, 20 karakterden uzun olmasin, upper case normalize et.
        // AllowedLocationTypeCodes hardcode listesi kaldirildi.
        var normalizedTypeCode = NormalizeRequiredField(locationTypeCode, 20, "Lokasyon tipi");
        if (string.Equals(normalizedTypeCode, "AISLE", StringComparison.OrdinalIgnoreCase))
        {
            normalizedTypeCode = "SECTION";
        }
        return normalizedTypeCode.ToUpperInvariant();
    }

    private static string NormalizeMeasureUnitCode(string? unitCode)
    {
        return NormalizeRequiredField(unitCode, 20, "Olcu birimi kodu").ToUpperInvariant();
    }

    private async Task<(
        IReadOnlyCollection<FieldGroup> Groups,
        IReadOnlyCollection<MaterialCardDynamicFieldDefinition> Fields,
        IReadOnlyCollection<MaterialCardFieldOption> Options)> LoadMaterialCardDynamicSchemaAsync(
        CancellationToken cancellationToken)
    {
        var groups = await _repository.GetFieldGroupsAsync(cancellationToken);
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

    private static void ValidateParentLocation(int? parentId, IReadOnlyCollection<Location> locations)
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
        IReadOnlyCollection<Location> locations)
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

    /// <summary>
    /// Barkod benzersizligini dogrular (bos barkod serbest ve tekrar edebilir).
    /// Karsilastirma Trim()+case-insensitive; guncellemede kendi kaydi haric tutulur.
    /// </summary>
    private static void EnsureBarcodeUnique(string? normalizedBarcode, int? excludeItemId, IReadOnlyCollection<Item> items)
    {
        if (string.IsNullOrWhiteSpace(normalizedBarcode))
        {
            return;
        }

        var conflict = items.FirstOrDefault(x =>
            x.Id != excludeItemId &&
            !string.IsNullOrWhiteSpace(x.Barcode) &&
            string.Equals(x.Barcode!.Trim(), normalizedBarcode, StringComparison.OrdinalIgnoreCase));

        if (conflict is not null)
        {
            throw new ArgumentException(
                $"Bu barkod baska bir malzemede kullaniliyor: {conflict.Name} ({conflict.Code})");
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

    public async Task<(IReadOnlyCollection<ItemDto> Items, int TotalCount)> GetItemsPagedAsync(
        string? search, int offset, int pageSize, CancellationToken cancellationToken, string? groupCode = null)
    {
        var (cards, totalCount) = await _repository.GetItemsPagedAsync(search, offset, pageSize, cancellationToken, groupCode);
        var dtos = cards.Select(x => new ItemDto(
            x.Id, x.Code, x.Name,
            x.TypeId, x.IsActive, x.Created, x.Updated,
            x.UnitId, x.Combinations, x.TaxRate,
            Barcode: x.Barcode)).ToList();
        return (dtos, totalCount);
    }

    public async Task<IReadOnlyCollection<ItemDto>> GetItemsForLookupAsync(CancellationToken cancellationToken)
    {
        var cards = await _repository.GetItemsAsync(cancellationToken);
        return cards
            .Where(c => c.IsActive)
            .OrderBy(c => c.Code)
            .Select(c => new ItemDto(
                c.Id,
                c.Code,
                c.Name,
                null,
                c.IsActive,
                c.Created,
                c.Updated,
                c.UnitId,
                c.Combinations,
                // Barcode: projeksiyon bunu eskiden dolduruyordu (varsayilan null kaliyordu) —
                // mobil Stok Sorgu ekrani barkodu gosterebilsin diye eklendi. Alanin bos
                // birakilmasi cagiranlar icin davranis degisikligi degil (yalniz eksik veri
                // dolduruldu); ItemDto imzasi degismedi.
                Barcode: c.Barcode))
            .ToList();
    }

    public async Task<IReadOnlyCollection<CombinationLookupRow>> GetCombinationsForLookupAsync(
        string materialCode, CancellationToken cancellationToken)
        => await _repository.GetCombinationsByMaterialCodeAsync(materialCode, cancellationToken);

    public Task<IReadOnlyCollection<CombinationListItemDto>> GetAllCombinationsAsync(CancellationToken cancellationToken)
        => _repository.GetAllCombinationsAsync(cancellationToken);

    public async Task<ResolveCombinationResponse> ResolveOrCreateCombinationAsync(
        ResolveCombinationRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.MaterialCode))
            throw new ArgumentException("Malzeme kodu zorunludur.");
        if (request.Selections is null || request.Selections.Count == 0)
            throw new ArgumentException("En az bir özellik değeri seçmelisiniz.");

        // 1) Mevcut kombinasyonları çek ve dedup check — ID tabanli (CLAUDE.md
        //    "Standart kural: id tabanli FK"). FeatureValueId set'i karsilastirilir;
        //    deger adi/whitespace/case farklarindan tamamen bagimsiz.
        var existing = await _repository.GetCombinationsByMaterialCodeAsync(request.MaterialCode, cancellationToken);

        var requestSet = request.Selections
            .Select(s => s.ValueId)
            .Where(id => id > 0)
            .OrderBy(id => id)
            .ToArray();

        foreach (var combo in existing)
        {
            var comboSet = combo.FeatureValues
                .Select(fv => fv.FeatureValueId)
                .Where(id => id > 0)
                .OrderBy(id => id)
                .ToArray();

            if (comboSet.Length != requestSet.Length) continue;
            var matched = true;
            for (int i = 0; i < comboSet.Length; i++)
            {
                if (comboSet[i] != requestSet[i])
                {
                    matched = false;
                    break;
                }
            }
            if (matched)
            {
                // matched: mevcut kombinasyonun gercek ConfigId'sini don — yeni
                // kayit acilmaz, frontend mevcut koda yonlendirilir.
                return new ResolveCombinationResponse(
                    Matched: true,
                    ConfigId: combo.ConfigId,
                    Code: combo.Code,
                    Name: combo.Name);
            }
        }

        // 2) Eşleşme yok — yeni CONFIG üret
        var configName = string.Join(" | ",
            request.Selections.Select(s => $"{s.FeatureName}: {s.ValueName}"));
        var valueIds = request.Selections.Select(s => s.ValueId).ToArray();

        var (newId, newCode) = await _repository.AddProductConfigurationCombinationAsync(
            relatedMaterialCode: request.MaterialCode,
            configName: configName,
            valueIds: valueIds,
            isActive: true,
            cancellationToken: cancellationToken);

        return new ResolveCombinationResponse(
            Matched: false,
            ConfigId: newId,
            Code: newCode,
            Name: configName);
    }

    /* ── Ürün Ağacı (Reçete) ─────────────────────────────────────── */

    public async Task<IReadOnlyCollection<BOMDto>> GetBOMsAsync(CancellationToken cancellationToken)
    {
        var trees = await _repository.GetBOMsAsync(cancellationToken);
        // Items + ItemConfiguration lookup'lari ile enriched BOMDto map'le
        var items = await _repository.GetItemsAsync(cancellationToken);
        var itemById = items.ToDictionary(i => i.Id, i => i);
        // Note: ItemConfiguration lookup'u burada minimal — ConfigCode null doner;
        // tek-tek BOM lookup'larinda (GetBOMByItemAsync) JOIN ile gelir.
        return trees.Select(t => new BOMDto(
            Id:            t.Id,
            ItemId:        t.ItemId,
            ItemCode:      itemById.TryGetValue(t.ItemId, out var pi) ? pi.Code : "",
            ItemName:      itemById.TryGetValue(t.ItemId, out var pi2) ? (pi2.Name ?? pi2.Code) : "",
            ConfigId:      t.ConfigId,
            ConfigCode:    null,
            Description:   t.Description,
            ImageData:     t.ImageData,
            ImageMimeType: t.ImageMimeType,
            ImageFitMode:  t.ImageFitMode,
            ImageRotation: t.ImageRotation,
            Lines: t.Lines.Select(l => new BOMLineDto(
                Id:         l.Id,
                BOMId:      l.BOMId,
                ItemId:     l.ItemId,
                ItemCode:   itemById.TryGetValue(l.ItemId, out var ci) ? ci.Code : "",
                ItemName:   itemById.TryGetValue(l.ItemId, out var ci2) ? (ci2.Name ?? ci2.Code) : "",
                ConfigId:   l.ConfigId,
                ConfigCode: null,
                Quantity:   l.Quantity,
                ScrapRatio: l.ScrapRatio,
                LineGuid:   l.LineGuid,
                Note:       l.Note)).ToList(),
            VersionCode:   t.VersionCode)).ToList();
    }

    /// <summary>
    /// Legacy code-based lookup wrapper. Frontend halen materialCode/configCode ile
    /// sorguluyor olabilir; backend Items.code → ItemId resolve edip yeni
    /// FK-based GetBOMByItemAsync metodunu cagirir.
    /// </summary>
    public async Task<BOMWithNames?> GetBOMByCodeAsync(string materialCode, string? configCode, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(materialCode)) return null;
        var items = await _repository.GetItemsAsync(cancellationToken);
        var item = items.FirstOrDefault(i => string.Equals(i.Code, materialCode.Trim(), StringComparison.OrdinalIgnoreCase));
        if (item is null) return null;

        int? configId = null;
        if (!string.IsNullOrWhiteSpace(configCode))
        {
            // ItemConfiguration.RecordCode → Id resolve (config kodu CMB...) — best-effort
            var combos = await _repository.GetCombinationsByMaterialCodeAsync(materialCode.Trim(), cancellationToken);
            var match = combos.FirstOrDefault(c => string.Equals(c.Code, configCode.Trim(), StringComparison.OrdinalIgnoreCase));
            if (match is not null) configId = match.ConfigId;
        }

        return await _repository.GetBOMByItemAsync(item.Id, configId, cancellationToken);
    }

    /// <summary>
    /// PK lookup. Liste sayfasindan secilen BOM Id'si ile detay yukleme.
    /// </summary>
    public async Task<BOMWithNames?> GetBOMByIdAsync(int id, CancellationToken cancellationToken)
    {
        if (id <= 0) return null;
        // 2026-08-06 versiyonlama: Id dogrudan okunur. Eski yol (listeyi cek → Item'a
        // cozup GetBOMByItemAsync) versiyon kaydini acarken BAZ receteye dusuyordu
        // (ayrica tum BOM listesini bellege cekiyordu).
        return await _repository.GetBOMByIdWithNamesAsync(id, cancellationToken);
    }

    /// <summary>2026-08-06: mamul+kombinasyonun recete versiyon listesi (baz + versiyonlar).</summary>
    public async Task<IReadOnlyCollection<BomVersionSummaryDto>> GetBomVersionsAsync(
        int itemId, int? configId, CancellationToken cancellationToken)
    {
        if (itemId <= 0) return Array.Empty<BomVersionSummaryDto>();
        return await _repository.GetBomVersionsAsync(itemId, configId, cancellationToken);
    }

    /// <summary>
    /// 2026-08-06: kaynak receteden (baz veya baska versiyon) yeni bir DUZENLENEBILIR
    /// versiyon turetir. Kod kullanicidan gelir; ayni mamul+kombinasyonda tekil olmalidir
    /// (DB UX_BOM_Version da garanti eder — burasi kullanici dostu on-kontrol).
    /// Otomatik versiyonlama YOKTUR: her kayitta degil, yalniz bu metotla versiyon olusur.
    /// </summary>
    public async Task<int> DeriveBomVersionAsync(int sourceBomId, string versionCode, int? userId, CancellationToken cancellationToken)
    {
        versionCode = (versionCode ?? "").Trim();
        if (sourceBomId <= 0) throw new ArgumentException("Kaynak recete secilmelidir.");
        if (versionCode.Length == 0) throw new ArgumentException("Versiyon kodu bos olamaz.");
        if (versionCode.Length > 30) throw new ArgumentException("Versiyon kodu en fazla 30 karakter olabilir.");

        var source = await _repository.GetBOMByIdWithNamesAsync(sourceBomId, cancellationToken)
            ?? throw new ArgumentException("Kaynak recete bulunamadi veya pasif.");

        var versions = await _repository.GetBomVersionsAsync(source.ItemId, source.ConfigId, cancellationToken);
        if (versions.Any(v => string.Equals(v.VersionCode, versionCode, StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException($"'{versionCode}' kodlu versiyon bu mamulde zaten var. Farkli bir kod veriniz.");

        var entity = new BOM
        {
            ItemId        = source.ItemId,
            ConfigId      = source.ConfigId,
            Description   = source.Description,
            ImageData     = source.ImageData,
            ImageMimeType = source.ImageMimeType,
            ImageFitMode  = source.ImageFitMode,
            ImageRotation = source.ImageRotation,
            RoutingId     = source.RoutingId,
            VersionCode   = versionCode,
            ParentBomId   = sourceBomId,
            CreatedById   = userId,
            UpdatedById   = userId,
        };
        foreach (var l in source.Lines)
            entity.AddLine(BOMLine.Create(l.ItemId, l.ConfigId, l.Quantity, l.ScrapRatio, userId, l.Note, l.ComponentBomId),
                allowDuplicate: true); // kaynak zaten kayitli — kendi kural setinden gecmisti

        var newId = await _repository.AddBOMAsync(entity, cancellationToken);
        _audit?.LogInsert("Bom",newId,
            $"{source.ItemCode} [{versionCode}]",
            detail: $"Recete versiyonu turetildi (kaynak: {(source.VersionCode is null ? "baz recete" : source.VersionCode)}, {source.Lines.Count} bilesen)");
        return newId;
    }

    public async Task<int> SaveBOMAsync(SaveBOMRequest request, int? userId, CancellationToken cancellationToken,
        bool allowDuplicateComponents = false)
    {
        // 1) Items lookup — sadece bu kayda dahil olabilecek ID/Code'lari oku.
        //    Eski "tum tabloyu cek" (50K satir) yaklasimi atildi (rapor madde 3.10).
        //    a) ID yolu: request'te gelen tum ItemId'leri topla
        //    b) Code yolu: request'te gelen kod stringlerini cek → ayri sorgu
        //       (degisken sayida IN(...) parametre + sirket-tabanli filtre)
        var requestedIds = new List<int>();
        if (request.ItemId > 0) requestedIds.Add(request.ItemId);
        foreach (var line in request.Lines ?? Array.Empty<SaveBOMLineRequest>())
            if (line.ItemId > 0) requestedIds.Add(line.ItemId);

        var byIdSnapshot = await _repository.GetItemsByIdsAsync(requestedIds, cancellationToken);
        var activeById = byIdSnapshot.Where(x => x.IsActive).ToDictionary(x => x.Id);

        // Code yolu (legacy UI) — sadece koda dayanan istekler icin tum tabloyu degil,
        // sadece request'teki kodlari filtreleyen ek bir lookup yapariz. Mevcut
        // GetItemsAsync interface'i tek SQL ile tum tablo cektigi icin geri uyumda
        // simdilik onu kullaniyoruz; lookup esnek kalsin diye sadece kod yolu
        // gerekliyse cagrilir (yaygin yeni UI yolunda hic tetiklenmez).
        Dictionary<string, int>? activeIdByCode = null;
        async Task<Dictionary<string, int>> EnsureCodeLookupAsync()
        {
            if (activeIdByCode != null) return activeIdByCode;
            var all = await _repository.GetItemsAsync(cancellationToken);
            activeIdByCode = all.Where(x => x.IsActive)
                                .GroupBy(x => x.Code, StringComparer.OrdinalIgnoreCase)
                                .ToDictionary(g => g.Key, g => g.First().Id, StringComparer.OrdinalIgnoreCase);
            // Code lookup yolu degilse activeById'ye yeni ID'ler ekle (sonraki erisimler icin)
            foreach (var item in all.Where(x => x.IsActive))
                activeById.TryAdd(item.Id, item);
            return activeIdByCode;
        }

        // Mamul ItemId resolve — yeni UI ItemId gonderir, eski UI ParentMaterialCode gonderir.
        // Kullanici dostu hata mesajlari (rapor 2026-05-17 madde 3.11): teknik ID
        // ifsa etmeden anlasilir cumleler.
        int parentItemId = request.ItemId;
        if (parentItemId <= 0 && !string.IsNullOrWhiteSpace(request.ParentMaterialCode))
        {
            var codes = await EnsureCodeLookupAsync();
            if (!codes.TryGetValue(request.ParentMaterialCode.Trim(), out parentItemId))
                throw new ArgumentException(
                    $"'{request.ParentMaterialCode.Trim()}' kodlu mamul sistemde bulunamadi veya pasif durumda. Listeden gecerli bir mamul seciniz.");
        }
        if (parentItemId <= 0)
            throw new ArgumentException("Mamul secmek zorunludur. Listeden bir mamul kodu seciniz.");
        if (!activeById.ContainsKey(parentItemId))
        {
            // ID gonderildi ama lookup'ta yok (silinmis/pasif) — final dogrulama
            var all = await _repository.GetItemsByIdsAsync(new[] { parentItemId }, cancellationToken);
            if (!all.Any(x => x.IsActive && x.Id == parentItemId))
                throw new ArgumentException(
                    "Sectiginiz mamul aktif degil veya sistemden silinmis. Lutfen listeden gecerli bir kayit seciniz.");
            foreach (var item in all) activeById.TryAdd(item.Id, item);
        }

        // Config — config kodu verilmisse Id'ye COZULMEK ZORUNDA (2026-07-05).
        // Onceki davranis: eslesme yoksa sessizce ConfigId=null ile devam ediyordu;
        // bu, upsert'in (ItemId, ConfigId=NULL) hedefine kayip mamulun configsiz
        // recetesini EZMESINE yol acabiliyordu. Artik cozulemezse acik hata.
        int? parentConfigId = request.ConfigId;
        if (parentConfigId is null && !string.IsNullOrWhiteSpace(request.ConfigurationCode))
        {
            var parentItem = activeById[parentItemId];
            var combos = await _repository.GetCombinationsByMaterialCodeAsync(parentItem.Code, cancellationToken);
            var match = combos.FirstOrDefault(c => string.Equals(c.Code, request.ConfigurationCode.Trim(), StringComparison.OrdinalIgnoreCase));
            if (match is null)
                throw new ArgumentException(
                    $"'{request.ConfigurationCode.Trim()}' kodlu kombinasyon bu mamulde bulunamadı. Kombinasyon rehberinden geçerli bir kayıt seçiniz.");
            parentConfigId = match.ConfigId;
        }

        // Lines koleksiyonu — SaveBOMRequestValidator zaten validate ediyor
        // (NotNull + Count > 0), ama service standalone cagrilabildiginde
        // (orn. import) defansif: ayni kontrolu burada da koruyoruz.
        var lines = (request.Lines ?? Array.Empty<SaveBOMLineRequest>()).ToList();
        if (lines.Count == 0)
            throw new ArgumentException("Recetede en az bir bilesen olmalidir.");

        // Her bilesen icin ItemId resolve — cross-aggregate (Items) dogrulamasi
        // burada kalir (bilesenin aktif olup olmadigi servis sorumlulugu).
        // Numerik invariant'lar (Quantity>0, ScrapRatio>=0) Domain'de
        // BOMLine.EnsureValid icinde + validator katmaninda — ayni kontrol burada
        // tekrar edilmez.
        var resolvedLines = new List<(int ItemId, int? ConfigId, decimal Qty, decimal Scrap, string? Note, int? ComponentBomId)>(lines.Count);
        foreach (var line in lines)
        {
            int lineItemId = line.ItemId;
            if (lineItemId <= 0 && !string.IsNullOrWhiteSpace(line.ComponentMaterialCode))
            {
                var codes = await EnsureCodeLookupAsync();
                if (!codes.TryGetValue(line.ComponentMaterialCode.Trim(), out lineItemId))
                    throw new ArgumentException(
                        $"'{line.ComponentMaterialCode.Trim()}' kodlu bilesen sistemde bulunamadi veya pasif durumda. Bilesen alanindan gecerli bir malzeme seciniz.");
            }
            if (lineItemId <= 0)
                throw new ArgumentException("Her bilesen icin bir malzeme secmek zorunludur.");
            if (!activeById.ContainsKey(lineItemId))
            {
                // Edge case: line.ItemId initial snapshot'a girmemis olabilir (yeni
                // bilesen, ID hic gonderilmemis vs.) — explicit dogrulama
                var probe = await _repository.GetItemsByIdsAsync(new[] { lineItemId }, cancellationToken);
                if (!probe.Any(x => x.IsActive && x.Id == lineItemId))
                    throw new ArgumentException(
                        "Bilesen olarak secilen malzemelerden biri aktif degil veya sistemden silinmis. Lutfen bilesen listesini gozden geciriniz.");
                foreach (var item in probe) activeById.TryAdd(item.Id, item);
            }

            // Satir config'i de ayni sıkı kurala tabi (2026-07-05): kod verildiyse cozulmeli.
            int? lineConfigId = line.ConfigId;
            if (lineConfigId is null && !string.IsNullOrWhiteSpace(line.ComponentConfigCode))
            {
                var lineItem = activeById[lineItemId];
                var combos = await _repository.GetCombinationsByMaterialCodeAsync(lineItem.Code, cancellationToken);
                var match = combos.FirstOrDefault(c => string.Equals(c.Code, line.ComponentConfigCode.Trim(), StringComparison.OrdinalIgnoreCase));
                if (match is null)
                    throw new ArgumentException(
                        $"'{lineItem.Code}' bileşeni için '{line.ComponentConfigCode.Trim()}' kodlu kombinasyon bulunamadı. Satırı düzenleyip geçerli bir kombinasyon seçiniz.");
                lineConfigId = match.ConfigId;
            }

            // 2026-08-06: yari mamul bilesende sabitlenen recete/versiyon dogrulamasi —
            // verilen BOM Id gercekten O bilesene ait olmali (yanlis mamulun recetesi
            // sabitlenemez; UI hatasi/istemci beyani burada kesilir).
            int? componentBomId = line.ComponentBomId is > 0 ? line.ComponentBomId : null;
            if (componentBomId is not null)
            {
                var pinned = await _repository.GetBOMByIdWithNamesAsync(componentBomId.Value, cancellationToken);
                if (pinned is null || pinned.ItemId != lineItemId)
                    throw new ArgumentException(
                        "Bilesende secilen recete versiyonu bulunamadi veya bu bilesene ait degil. Versiyon secimini yenileyiniz.");
            }

            resolvedLines.Add((lineItemId, lineConfigId, line.Quantity, line.ScrapRatio,
                string.IsNullOrWhiteSpace(line.Note) ? null : line.Note.Trim(), componentBomId));
        }

        // Malzeme Belge Kilitleri ("recete") — BOMEdit.cshtml malzeme araması paylaşımlı
        // StockLookup uctan gecer (docType farkindaligi yok, custom-lookup bypass sinifi),
        // yani UI seviyesinde filtrelenemez. Header (hedef mamul) + tum bilesen satirlari
        // ID'ye cozuldukten SONRA (bu nokta) tek sorguyla kontrol edilir — gercek kapi burasi.
        var lockedForRecipe = await _repository.GetLockedItemIdsByDocTypeAsync(
            ItemDocumentLockTypes.RecipeCode, cancellationToken);
        if (lockedForRecipe.Count > 0)
        {
            if (lockedForRecipe.Contains(parentItemId))
            {
                var label = activeById.TryGetValue(parentItemId, out var pItem) ? pItem.Code : $"#{parentItemId}";
                throw new ArgumentException(
                    $"'{label}' malzemesi reçetede (BOM) seçim için kilitli — Malzeme Belge Kilitleri ekranından kilidi kaldırın.");
            }
            var lockedLine = resolvedLines.FirstOrDefault(l => lockedForRecipe.Contains(l.ItemId));
            if (lockedLine.ItemId > 0)
            {
                var label = activeById.TryGetValue(lockedLine.ItemId, out var lItem) ? lItem.Code : $"#{lockedLine.ItemId}";
                throw new ArgumentException(
                    $"'{label}' bileşeni reçetede (BOM) seçim için kilitli — Malzeme Belge Kilitleri ekranından kilidi kaldırın.");
            }
        }

        byte[]? imageData = null;
        string? imageMimeType = null;
        if (!string.IsNullOrWhiteSpace(request.ImageBase64))
        {
            imageData     = Convert.FromBase64String(request.ImageBase64);
            imageMimeType = string.IsNullOrWhiteSpace(request.ImageMimeType) ? "image/png" : request.ImageMimeType.Trim();
        }

        var fitMode  = string.IsNullOrWhiteSpace(request.ImageFitMode) ? "square" : request.ImageFitMode.Trim();
        // ImageRotation: yalnizca 0/90/180/270 kabul et; aksi durum 0'a normalize
        var rotation = request.ImageRotation;
        if (rotation != 0 && rotation != 90 && rotation != 180 && rotation != 270) rotation = 0;

        // UPSERT (2026-08-06 versiyonlama): kimlik artik (ItemId, ConfigId, VersionCode)
        // uclusudur. VersionCode NULL = baz recete (eski davranisla ayni hedef);
        // dolu = o kodlu versiyon guncellenir (yeni versiyon yalniz DeriveBomVersionAsync
        // ile acilir — Id'siz + eslesmeyen kod kaydi versiyonu SESSIZCE olusturmaz).
        var versionCode = string.IsNullOrWhiteSpace(request.VersionCode) ? null : request.VersionCode.Trim();
        int resolvedId = request.Id ?? 0;
        if (resolvedId <= 0)
        {
            if (versionCode is null)
            {
                var existing = await _repository.GetBOMByItemAsync(parentItemId, parentConfigId, cancellationToken);
                if (existing is not null)
                    resolvedId = existing.Id;
            }
            else
            {
                var versions = await _repository.GetBomVersionsAsync(parentItemId, parentConfigId, cancellationToken);
                var match = versions.FirstOrDefault(v =>
                    string.Equals(v.VersionCode, versionCode, StringComparison.OrdinalIgnoreCase));
                if (match is null)
                    throw new ArgumentException(
                        $"'{versionCode}' kodlu versiyon bulunamadi. Yeni versiyon 'Versiyon Turet' ile olusturulur.");
                resolvedId = match.Id;
            }
        }

        // Audit diff icin eski durumu MUTASYONDAN ONCE oku (CLAUDE.md audit kurali).
        BOMWithNames? beforeSave = resolvedId > 0
            ? await _repository.GetBOMByIdWithNamesAsync(resolvedId, cancellationToken)
            : null;

        // ── Cycle (dongusel bagimlilik) korumasi (rapor 2026-05-17 madde 3.1) ──
        // Save'den ONCE dogrulanir; ihlal varsa DB'ye yazilmaz. BFS depth cap 20.
        // Self-reference + indirect loop birlikte tek pass'te yakalanir.
        // BOMComponentItemIdsAsync recursive lookup icin lazy delegate.
        // visited cache: ayni alt-mamulun cocuklarini tekrar tekrar sorgulamayalim.
        var componentItemIds = resolvedLines.Select(l => l.ItemId).Distinct().ToList();
        var childrenCache = new Dictionary<int, IReadOnlyCollection<int>>();
        try
        {
            await BOM.EnsureNoCycleAsync(parentItemId, componentItemIds, async childId =>
            {
                if (childrenCache.TryGetValue(childId, out var cached)) return cached;
                var ids = await _repository.GetBOMComponentItemIdsAsync(childId, cancellationToken);
                childrenCache[childId] = ids;
                return ids;
            });
        }
        catch (CalibraHub.Domain.Common.DomainException dex)
        {
            // Domain hatasi -> Application katmaninda ArgumentException (controller 400 mapping'i ile uyumlu)
            throw new ArgumentException(dex.Message, dex);
        }

        // Domain entity'yi rich-domain pattern'i ile insa et:
        //   1) Header field'lari ile bos Lines koleksiyonu yarat.
        //   2) Her resolved satir icin BOMLine.Create -> EnsureValid invariant'tan gec.
        //   3) BOM.AddLine ile koleksiyona ekle (mukerrer kontrol).
        //   4) BOM.EnsureValid son kontrol (sifir bilesen, mukerrer, fit/rotation).
        // DomainException firlatilirsa controller'in beklemedigi exception olmasin
        // diye ArgumentException olarak yansitilir (Web 400 mapping uyumlu).
        var entity = new BOM
        {
            Id            = resolvedId,
            ItemId        = parentItemId,
            ConfigId      = parentConfigId,
            Description   = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim(),
            ImageData     = imageData,
            ImageMimeType = imageMimeType,
            ImageFitMode  = fitMode,
            ImageRotation = rotation,
            // 2026-05-20: header-level Routing FK. NULL ise is emri acilirken kullanici secer.
            // Cozumleme onceligi:
            //   1) request.RoutingId > 0  → dogrudan kullan
            //   2) request.RoutingCode dolu → Routing.Code -> Id lookup (standart rehber fallback)
            //   3) Aksi halde NULL.
            // Bulunamayan kod (kullanici sectigi rota silinmis/pasif) → kullanici dostu hata.
            RoutingId     = await ResolveRoutingIdAsync(request, cancellationToken),
            VersionCode   = versionCode,
            CreatedById   = userId,
            UpdatedById   = userId,
        };
        try
        {
            foreach (var l in resolvedLines)
                entity.AddLine(BOMLine.Create(l.ItemId, l.ConfigId, l.Qty, l.Scrap, userId, l.Note, l.ComponentBomId), allowDuplicateComponents);
            entity.EnsureValid(allowDuplicateComponents);
        }
        catch (CalibraHub.Domain.Common.DomainException dex)
        {
            throw new ArgumentException(dex.Message, dex);
        }

        // Audit basligi: "MAMULKODU" veya "MAMULKODU [VERSIYON]" — /AuditLog raporlamasi
        // "kim, ne zaman, hangi receteyi" sorusuna bu etiketle cevap verir.
        var parentCode = activeById.TryGetValue(parentItemId, out var parentSnap) ? parentSnap.Code : $"#{parentItemId}";
        var auditTitle = versionCode is null ? parentCode : $"{parentCode} [{versionCode}]";

        if (entity.Id <= 0)
        {
            var newId = await _repository.AddBOMAsync(entity, cancellationToken);
            _audit?.LogInsert("Bom",newId, auditTitle,
                detail: $"{resolvedLines.Count} bilesen",
                snapshot: BuildBomAuditSnapshot(entity, activeById));
            return newId;
        }

        await _repository.UpdateBOMAsync(entity, cancellationToken);

        // Yalnizca degisen alanlar loglanir (audit kural #3) — header + bilesen diff'i.
        if (_audit is not null)
        {
            try
            {
                var changes = BuildBomChanges(beforeSave, entity, activeById);
                if (changes.Count > 0)
                    _audit.LogChanges("Bom", entity.Id, auditTitle, changes);
            }
            catch { /* audit is akisini asla bozamaz */ }
        }
        return entity.Id;
    }

    /// <summary>Insert audit dokumu — "bos → deger" olarak yazilacak baslik + bilesen listesi.</summary>
    private static object BuildBomAuditSnapshot(BOM entity, IReadOnlyDictionary<int, Item> itemsById)
        => new
        {
            entity.Description,
            Versiyon = entity.VersionCode ?? "(baz)",
            Bilesenler = string.Join("; ", entity.Lines.Select(l =>
                $"{(itemsById.TryGetValue(l.ItemId, out var it) ? it.Code : "#" + l.ItemId)} x{l.Quantity}"
                + (l.ScrapRatio > 0 ? $" (fire {l.ScrapRatio})" : ""))),
        };

    /// <summary>
    /// Recete update diff'i — header alanlari + eklenen/cikan/degisen bilesenler.
    /// DocumentService.BuildLineChanges desenine paralel elle diff (reflection degil,
    /// satirlar kalem listesi oldugu icin).
    /// </summary>
    private static List<CalibraHub.Application.Auditing.AuditFieldChange> BuildBomChanges(
        BOMWithNames? before, BOM after, IReadOnlyDictionary<int, Item> itemsById)
    {
        var changes = new List<CalibraHub.Application.Auditing.AuditFieldChange>();
        string CodeOf(int itemId) => itemsById.TryGetValue(itemId, out var it) ? it.Code : "#" + itemId;

        if (!string.Equals(before?.Description ?? "", after.Description ?? "", StringComparison.Ordinal))
            changes.Add(new CalibraHub.Application.Auditing.AuditFieldChange(
                "Description", "Açıklama", before?.Description, after.Description));
        if ((before?.RoutingId) != after.RoutingId)
            changes.Add(new CalibraHub.Application.Auditing.AuditFieldChange(
                "RoutingId", "Rota", before?.RoutingCode ?? before?.RoutingId?.ToString(), after.RoutingId?.ToString()));

        var oldLines = (before?.Lines ?? Array.Empty<BOMLineWithName>())
            .ToDictionary(l => (l.ItemId, l.ConfigId ?? 0));
        var newLines = after.Lines.ToDictionary(l => (l.ItemId, l.ConfigId ?? 0));

        foreach (var (key, nl) in newLines)
        {
            if (!oldLines.TryGetValue(key, out var ol))
            {
                changes.Add(new CalibraHub.Application.Auditing.AuditFieldChange(
                    $"Line[{key.Item1}]", $"Bileşen — {CodeOf(key.Item1)}", null,
                    $"x{nl.Quantity}" + (nl.ScrapRatio > 0 ? $" (fire {nl.ScrapRatio})" : "")));
            }
            else if (ol.Quantity != nl.Quantity || ol.ScrapRatio != nl.ScrapRatio
                     || (ol.ComponentBomId) != nl.ComponentBomId
                     || !string.Equals(ol.Note ?? "", nl.Note ?? "", StringComparison.Ordinal))
            {
                changes.Add(new CalibraHub.Application.Auditing.AuditFieldChange(
                    $"Line[{key.Item1}]", $"Bileşen — {CodeOf(key.Item1)}",
                    $"x{ol.Quantity}" + (ol.ScrapRatio > 0 ? $" (fire {ol.ScrapRatio})" : "") + (ol.ComponentBomId is not null ? $" [recete #{ol.ComponentBomId}]" : ""),
                    $"x{nl.Quantity}" + (nl.ScrapRatio > 0 ? $" (fire {nl.ScrapRatio})" : "") + (nl.ComponentBomId is not null ? $" [recete #{nl.ComponentBomId}]" : "")));
            }
        }
        foreach (var (key, ol) in oldLines)
        {
            if (!newLines.ContainsKey(key))
                changes.Add(new CalibraHub.Application.Auditing.AuditFieldChange(
                    $"Line[{key.Item1}]", $"Bileşen — {CodeOf(key.Item1)}",
                    $"x{ol.Quantity}", null));
        }
        return changes;
    }

    public async Task DeleteBOMAsync(int id, int? userId, CancellationToken cancellationToken)
    {
        if (id <= 0) throw new ArgumentException("Silinecek recete secilmelidir.");
        // Silinen icerik dokumu icin SILMEDEN ONCE oku (audit kural: "ne kayboldu" izlenebilir).
        var doomed = _audit is not null
            ? await _repository.GetBOMByIdWithNamesAsync(id, cancellationToken)
            : null;
        await _repository.DeleteBOMAsync(id, userId, cancellationToken);
        if (doomed is not null)
        {
            var title = doomed.VersionCode is null ? doomed.ItemCode : $"{doomed.ItemCode} [{doomed.VersionCode}]";
            _audit?.LogDelete("Bom", id, title,
                detail: $"{doomed.Lines.Count} bilesen",
                snapshot: doomed.Lines.Select(l => new CalibraHub.Application.Auditing.AuditFieldChange(
                    $"Line[{l.ItemId}]", $"Bileşen — {l.ComponentMaterialCode}",
                    $"x{l.Quantity}" + (l.ScrapRatio > 0 ? $" (fire {l.ScrapRatio})" : ""), null)).ToList());
        }
    }

    /* ── Kit / Paket Urun ─────────────────────────────────────────── */

    public async Task<ItemKitDto?> GetKitByItemAsync(int itemId, CancellationToken cancellationToken)
    {
        if (itemId <= 0) return null;
        return await _repository.GetKitByItemAsync(itemId, cancellationToken);
    }

    public async Task<int> SaveKitAsync(SaveItemKitRequest request, int? userId, CancellationToken cancellationToken)
    {
        // 1) Items lookup — kit karti + bilesenler (sadece bu kayda dahil ID/Code'lar).
        var requestedIds = new List<int>();
        if (request.ItemId > 0) requestedIds.Add(request.ItemId);
        foreach (var line in request.Lines ?? Array.Empty<SaveItemKitLineRequest>())
            if (line.ItemId > 0) requestedIds.Add(line.ItemId);

        var byIdSnapshot = await _repository.GetItemsByIdsAsync(requestedIds, cancellationToken);
        var activeById = byIdSnapshot.Where(x => x.IsActive).ToDictionary(x => x.Id);

        // Code yolu (legacy UI) — sadece koda dayanan istekler icin lazy tetiklenir.
        Dictionary<string, int>? activeIdByCode = null;
        async Task<Dictionary<string, int>> EnsureCodeLookupAsync()
        {
            if (activeIdByCode != null) return activeIdByCode;
            var all = await _repository.GetItemsAsync(cancellationToken);
            activeIdByCode = all.Where(x => x.IsActive)
                                .GroupBy(x => x.Code, StringComparer.OrdinalIgnoreCase)
                                .ToDictionary(g => g.Key, g => g.First().Id, StringComparer.OrdinalIgnoreCase);
            foreach (var item in all.Where(x => x.IsActive))
                activeById.TryAdd(item.Id, item);
            return activeIdByCode;
        }

        // Kit karti ItemId resolve — yeni UI ItemId gonderir, eski UI MaterialCode.
        int kitItemId = request.ItemId;
        if (kitItemId <= 0 && !string.IsNullOrWhiteSpace(request.MaterialCode))
        {
            var codes = await EnsureCodeLookupAsync();
            if (!codes.TryGetValue(request.MaterialCode.Trim(), out kitItemId))
                throw new ArgumentException(
                    $"'{request.MaterialCode.Trim()}' kodlu kit karti sistemde bulunamadi veya pasif durumda.");
        }
        if (kitItemId <= 0)
            throw new ArgumentException("Kit malzeme karti secilmelidir.");
        if (!activeById.ContainsKey(kitItemId))
        {
            var probe = await _repository.GetItemsByIdsAsync(new[] { kitItemId }, cancellationToken);
            var found = probe.FirstOrDefault(x => x.IsActive && x.Id == kitItemId);
            if (found is null)
                throw new ArgumentException("Sectiginiz kit karti aktif degil veya sistemden silinmis.");
            activeById[found.Id] = found;
        }

        // PriceMode normalize (whitelist, 4 mod — 2026-08-03 Seq 1078) + moda gore ilgisiz
        // alanlari yok say: FixedPrice yalniz FixedPackage'da, bilesen UnitPrice yalniz
        // FixedComponent'te anlamlidir (asagida resolvedLines olustururken de ayni kural uygulanir).
        // Seq 1078 review BULGU #1: eski 2-mod istemci degerlerini ('Fixed'/'RollUp') acikca
        // yeni adlara alias'la (bilinmeyen degeri SESSIZCE FixedPackage'a dusurmek yerine —
        // o davranis mevcut kitleri re-save'de bozardi). Gercekten gecersiz deger -> hata.
        var rawMode = (request.PriceMode ?? string.Empty).Trim();
        if (string.Equals(rawMode, "Fixed", StringComparison.OrdinalIgnoreCase))
            rawMode = KitPriceMode.FixedPackage;
        else if (string.Equals(rawMode, "RollUp", StringComparison.OrdinalIgnoreCase))
            rawMode = KitPriceMode.ListComponent;
        var priceMode = KitPriceMode.All.FirstOrDefault(m =>
            string.Equals(rawMode, m, StringComparison.OrdinalIgnoreCase));
        if (priceMode is null)
            throw new ArgumentException(
                $"Gecersiz fiyat modu: '{request.PriceMode}'. Beklenen degerler: {string.Join(", ", KitPriceMode.All)}.");
        decimal? fixedPrice = priceMode == KitPriceMode.FixedPackage ? request.FixedPrice : null;
        var isFixedComponentMode = priceMode == KitPriceMode.FixedComponent;

        var lines = (request.Lines ?? Array.Empty<SaveItemKitLineRequest>()).ToList();
        if (lines.Count == 0)
            throw new ArgumentException("Kit icerisinde en az bir bilesen olmalidir.");

        var resolvedLines = new List<(int ItemId, int? ConfigId, decimal Qty, string? Note, decimal? UnitPrice, int? UnitId)>(lines.Count);
        foreach (var line in lines)
        {
            int lineItemId = line.ItemId;
            if (lineItemId <= 0 && !string.IsNullOrWhiteSpace(line.ComponentMaterialCode))
            {
                var codes = await EnsureCodeLookupAsync();
                if (!codes.TryGetValue(line.ComponentMaterialCode.Trim(), out lineItemId))
                    throw new ArgumentException(
                        $"'{line.ComponentMaterialCode.Trim()}' kodlu bilesen sistemde bulunamadi veya pasif durumda.");
            }
            if (lineItemId <= 0)
                throw new ArgumentException("Her bilesen icin bir malzeme secmek zorunludur.");
            if (!activeById.ContainsKey(lineItemId))
            {
                var probe = await _repository.GetItemsByIdsAsync(new[] { lineItemId }, cancellationToken);
                var found = probe.FirstOrDefault(x => x.IsActive && x.Id == lineItemId);
                if (found is null)
                    throw new ArgumentException(
                        "Bilesen olarak secilen malzemelerden biri aktif degil veya sistemden silinmis. Lutfen kit icerigini gozden geciriniz.");
                activeById[found.Id] = found;
            }

            // Kit kendi kartini bilesen alamaz (trivial dongu).
            if (lineItemId == kitItemId)
                throw new ArgumentException("Kit'in kendisi bilesen olarak eklenemez.");
            // Kit-in-kit YASAK (Faz 1): bir bilesen Kit tipi (TypeId=10) olamaz.
            if (CalibraHub.Domain.Enums.ItemTypeCatalog.IsKit(activeById[lineItemId].TypeId))
                throw new ArgumentException(
                    $"'{activeById[lineItemId].Code}' bir kit'tir; kit icinde baska bir kit bilesen olarak kullanilamaz.");

            // Satir config'i — kod verildiyse Id'ye cozulmek zorunda.
            int? lineConfigId = line.ConfigId;
            if (lineConfigId is null && !string.IsNullOrWhiteSpace(line.ComponentConfigCode))
            {
                var lineItem = activeById[lineItemId];
                var combos = await _repository.GetCombinationsByMaterialCodeAsync(lineItem.Code, cancellationToken);
                var match = combos.FirstOrDefault(c => string.Equals(c.Code, line.ComponentConfigCode.Trim(), StringComparison.OrdinalIgnoreCase));
                if (match is null)
                    throw new ArgumentException(
                        $"'{lineItem.Code}' bileseni icin '{line.ComponentConfigCode.Trim()}' kodlu kombinasyon bulunamadi.");
                lineConfigId = match.ConfigId;
            }

            // Bilesen elle birim fiyati — yalniz FixedComponent modda anlamli (diger modlarda
            // gonderilse bile yok sayilir; kaydedilen tanim daima mod ile tutarli kalsin).
            decimal? lineUnitPrice = null;
            if (isFixedComponentMode && line.UnitPrice.HasValue)
            {
                if (line.UnitPrice.Value < 0)
                    throw new ArgumentException("Bilesen birim fiyati negatif olamaz.");
                lineUnitPrice = line.UnitPrice.Value;
            }

            // Secili olcu birimi — dogrudan persist edilir (FK constraint gecersiz UnitId'yi
            // INSERT/UPDATE aninda reddeder; sessizce yanlis deger yazilmaz). NULL = bilesenin
            // kendi varsayilan birimi kabul edilir (miktar cevrimi bu asamada kapsam disi).
            int? lineUnitId = line.UnitId is > 0 ? line.UnitId : null;

            resolvedLines.Add((lineItemId, lineConfigId, line.Quantity,
                string.IsNullOrWhiteSpace(line.Note) ? null : line.Note.Trim(), lineUnitPrice, lineUnitId));
        }

        // UPSERT hedefi — kit kartinin mevcut aktif icerigi (varsa VersionNo++).
        var existing = await _repository.GetKitByItemAsync(kitItemId, cancellationToken);

        var entity = new ItemKit
        {
            Id          = existing?.Id ?? 0,
            ItemId      = kitItemId,
            PriceMode   = priceMode,
            FixedPrice  = fixedPrice,
            Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim(),
            CreatedById = userId,
            UpdatedById = userId,
        };
        try
        {
            foreach (var l in resolvedLines)
                entity.AddLine(ItemKitLine.Create(l.ItemId, l.ConfigId, l.Qty, userId, l.Note, l.UnitPrice, l.UnitId));
            entity.EnsureValid();
        }
        catch (CalibraHub.Domain.Common.DomainException dex)
        {
            throw new ArgumentException(dex.Message, dex);
        }

        var kitTitle = activeById.TryGetValue(kitItemId, out var kitItem)
            ? (kitItem.Name ?? kitItem.Code ?? ("#" + kitItemId))
            : ("#" + kitItemId);

        if (entity.Id <= 0)
        {
            var newId = await _repository.AddKitAsync(entity, cancellationToken);
            _audit?.LogInsert("ItemKit", newId, kitTitle,
                snapshot: new { entity.PriceMode, entity.FixedPrice, entity.Description, LineCount = entity.Lines.Count });
            return newId;
        }

        // Guncelleme oncesi eski header + kalem durumunu logla (CLAUDE.md audit kurali: yalnizca
        // degisen alanlar). Kalemler DELETE+INSERT ile degistigi icin (ItemKitLine sinif dokumani)
        // ID eslesmesi yerine (ItemId,ConfigId) icerik anahtariyla diff'lenir (ID-tabanli eslestirme
        // kurali — ItemId/ConfigId, kod/ad DEGIL).
        var oldHeader = existing is null ? null : new { existing.PriceMode, existing.FixedPrice, existing.Description };
        var newHeader = new { entity.PriceMode, entity.FixedPrice, entity.Description };
        if (_audit is not null)
        {
            var headerChanges = AuditDiff.Compute(oldHeader, newHeader, "ItemKit");
            var lineChanges = BuildKitLineChanges(existing?.Lines, resolvedLines, activeById);
            var allChanges = headerChanges.Concat(lineChanges).ToList();
            if (allChanges.Count > 0)
                _audit.LogChanges("ItemKit", entity.Id, kitTitle, allChanges);
        }

        await _repository.UpdateKitAsync(entity, cancellationToken);
        return entity.Id;
    }

    /// <summary>
    /// Kit bilesen satirlarinin ESKI (ItemKitLineDto) ile YENI (resolvedLines) halini
    /// (ItemId, ConfigId) icerik anahtariyla karsilastirir — UpdateKitAsync DELETE+INSERT
    /// yaptigindan satir Id'leri kalici degildir (ItemKitLine sinif dokumani). Yalniz
    /// Quantity/UnitPrice/UnitId degisimi veya ekleme/silme raporlanir (CLAUDE.md audit kurali #3).
    /// </summary>
    private static List<AuditFieldChange> BuildKitLineChanges(
        IReadOnlyCollection<ItemKitLineDto>? oldLines,
        IReadOnlyList<(int ItemId, int? ConfigId, decimal Qty, string? Note, decimal? UnitPrice, int? UnitId)> newLines,
        IReadOnlyDictionary<int, Item> itemsById)
    {
        var changes = new List<AuditFieldChange>();
        var oldByKey = (oldLines ?? Array.Empty<ItemKitLineDto>())
            .ToDictionary(l => (l.ItemId, ConfigId: l.ConfigId ?? 0));
        var newByKey = newLines.ToDictionary(l => (l.ItemId, ConfigId: l.ConfigId ?? 0));

        string CompLabel(int itemId) =>
            itemsById.TryGetValue(itemId, out var it) ? (it.Name ?? it.Code ?? ("#" + itemId)) : ("#" + itemId);
        static string Summary(decimal qty, decimal? unitPrice, int? unitId) =>
            (unitPrice.HasValue ? $"{AuditDiff.Normalize(qty)} adet × {AuditDiff.Normalize(unitPrice.Value)}" : $"{AuditDiff.Normalize(qty)} adet")
                + (unitId.HasValue ? $" (Birim #{unitId.Value})" : string.Empty);

        foreach (var (key, nl) in newByKey)
        {
            if (!oldByKey.TryGetValue(key, out var ol))
            {
                changes.Add(new AuditFieldChange("Lines", $"Bileşen Eklendi — {CompLabel(key.ItemId)}",
                    null, Summary(nl.Qty, nl.UnitPrice, nl.UnitId)));
                continue;
            }
            if (ol.Quantity != nl.Qty || (ol.UnitPrice ?? 0) != (nl.UnitPrice ?? 0) || (ol.UnitId ?? 0) != (nl.UnitId ?? 0))
            {
                changes.Add(new AuditFieldChange("Lines", $"Bileşen Değişti — {CompLabel(key.ItemId)}",
                    Summary(ol.Quantity, ol.UnitPrice, ol.UnitId), Summary(nl.Qty, nl.UnitPrice, nl.UnitId)));
            }
        }
        foreach (var (key, ol) in oldByKey)
        {
            if (!newByKey.ContainsKey(key))
                changes.Add(new AuditFieldChange("Lines", $"Bileşen Silindi — {CompLabel(key.ItemId)}",
                    Summary(ol.Quantity, ol.UnitPrice, ol.UnitId), null));
        }
        return changes;
    }

    public async Task DeleteKitAsync(int itemId, int? userId, CancellationToken cancellationToken)
    {
        if (itemId <= 0) return;
        await _repository.DeleteKitAsync(itemId, userId, cancellationToken);
    }

    public async Task<IReadOnlyList<ItemKitRevisionSummaryDto>> GetKitRevisionsAsync(
        int itemId, CancellationToken cancellationToken)
    {
        if (itemId <= 0) return Array.Empty<ItemKitRevisionSummaryDto>();
        return await _repository.GetKitRevisionsAsync(itemId, cancellationToken);
    }

    public async Task<ItemKitRevisionDetailDto?> GetKitRevisionDetailAsync(
        int revisionId, CancellationToken cancellationToken)
    {
        if (revisionId <= 0) return null;
        return await _repository.GetKitRevisionDetailAsync(revisionId, cancellationToken);
    }

    /// <summary>
    /// 2026-05-20: BOM.RoutingId cozumleyici. Standart rehber UI'sinde frontend
    /// hidden Id'yi her zaman doldurmayabilir (kullanici input'a elle kod yazip blur
    /// ettiyse cache miss olabilir). Bu durumda RoutingCode'u backend Routing.Code
    /// uzerinden lookup eder. Kod gonderildi ama eslesme yoksa: kullanici dostu hata.
    /// </summary>
    private async Task<int?> ResolveRoutingIdAsync(SaveBOMRequest request, CancellationToken ct)
    {
        if (request.RoutingId.HasValue && request.RoutingId.Value > 0)
            return request.RoutingId;
        if (string.IsNullOrWhiteSpace(request.RoutingCode))
            return null;
        var id = await _repository.GetRoutingIdByCodeAsync(request.RoutingCode.Trim(), ct);
        if (id is null)
            throw new ArgumentException(
                $"'{request.RoutingCode.Trim()}' kodlu rota sistemde bulunamadi veya pasif durumda. Listeden gecerli bir rota seciniz.");
        return id;
    }

    public async Task<BOMExplodeResultDto?> ExplodeBOMAsync(
        int parentItemId, decimal quantity, int? configId, CancellationToken cancellationToken)
    {
        if (parentItemId <= 0)
            throw new ArgumentException("Patlatma icin mamul ItemId zorunlu (>0).");
        if (quantity <= 0)
            throw new ArgumentException($"Patlatma miktari sifirdan buyuk olmalidir (su an: {quantity}).");

        const int maxDepth = 20;

        // Parent item dogrulamasi + display field icin tek satir cek.
        var parentSnapshot = await _repository.GetItemsByIdsAsync(new[] { parentItemId }, cancellationToken);
        var parent = parentSnapshot.FirstOrDefault(x => x.IsActive && x.Id == parentItemId);
        if (parent is null) return null;

        // Opsiyonel config kod cozumleme (UI dropdown ile gosterim icin display)
        string? configCode = null;
        if (configId.HasValue)
        {
            var combos = await _repository.GetCombinationsByMaterialCodeAsync(parent.Code, cancellationToken);
            configCode = combos.FirstOrDefault(c => c.ConfigId == configId.Value)?.Code;
        }

        // ── BFS patlatma ──
        // Bir kuyruk tasiyor: (ItemId, accumulated quantity, depth).
        // Her dugumde repo.GetBOMComponentLinesAsync ile cocuklar cekilir;
        // alt sevtide quantity *= parent.Qty * (1 + scrap). Aggregate map
        // (ItemId -> (totalQty, firstDepth, isLeaf)) sonucu duzlestirir.
        // Cycle koruma: visited set + maxDepth cap. Cycle olmadigi varsayilir
        // (SaveBOMAsync EnsureNoCycle ile zaten engelliyor) ama defansif.
        var aggregate = new Dictionary<int, (decimal Qty, int Depth, bool IsLeaf)>();
        // 2026-08-06 versiyonlama: cache anahtari (ItemId, PinnedBomId) — satirda
        // sabitlenmis alt versiyon (ComponentBomId) varsa o recete gezilir, yoksa BAZ.
        var componentsCache = new Dictionary<(int ItemId, int? BomId), IReadOnlyCollection<BOMComponentLineRow>>();
        async Task<IReadOnlyCollection<BOMComponentLineRow>> ResolveChildrenAsync(int itemId, int? pinnedBomId)
        {
            var key = (itemId, pinnedBomId);
            if (componentsCache.TryGetValue(key, out var cached)) return cached;
            var fetched = pinnedBomId is > 0
                ? await _repository.GetBOMComponentLinesByBomIdAsync(pinnedBomId.Value, cancellationToken)
                : await _repository.GetBOMComponentLinesAsync(itemId, cancellationToken);
            componentsCache[key] = fetched;
            return fetched;
        }

        var rootChildren = await ResolveChildrenAsync(parentItemId, null);
        var frontier = new List<(int ItemId, decimal Qty, int Depth, int? BomId)>();
        foreach (var child in rootChildren)
        {
            // 1. seviye quantity = istenen mamul adedi * line qty * (1 + scrap)
            var subQty = quantity * child.Quantity * (1m + child.ScrapRatio);
            frontier.Add((child.ItemId, subQty, 1, child.ComponentBomId));
        }

        var truncated = false;
        var maxDepthSeen = 0;

        for (var depth = 1; depth <= maxDepth && frontier.Count > 0; depth++)
        {
            maxDepthSeen = Math.Max(maxDepthSeen, depth);
            var nextFrontier = new List<(int ItemId, decimal Qty, int Depth, int? BomId)>();

            // Her seviyede frontier'i (ItemId, PinnedBomId) bazli grupla — ayni item
            // AYNI receteyle farkli yollardan geldiyse quantity toplanir, child lookup
            // tek kez yapilir. Farkli versiyona sabitlenmis yollar ayri gezilir.
            var grouped = frontier
                .GroupBy(f => (f.ItemId, f.BomId))
                .Select(g => (g.Key.ItemId, Qty: g.Sum(x => x.Qty), Depth: g.Min(x => x.Depth), g.Key.BomId))
                .ToList();

            foreach (var (childItemId, childQty, childDepth, childBomId) in grouped)
            {
                var children = await ResolveChildrenAsync(childItemId, childBomId);
                var isLeaf = children.Count == 0;

                // Aggregate ekle (ilk goruldugu depth korunur — daha sig kayit kazanir)
                if (aggregate.TryGetValue(childItemId, out var existing))
                {
                    aggregate[childItemId] = (existing.Qty + childQty,
                                              Math.Min(existing.Depth, childDepth),
                                              existing.IsLeaf || isLeaf);  // herhangi bir path'te leaf ise leaf
                }
                else
                {
                    aggregate[childItemId] = (childQty, childDepth, isLeaf);
                }

                if (isLeaf) continue;

                foreach (var grand in children)
                {
                    var grandQty = childQty * grand.Quantity * (1m + grand.ScrapRatio);
                    nextFrontier.Add((grand.ItemId, grandQty, childDepth + 1, grand.ComponentBomId));
                }
            }
            frontier = nextFrontier;
        }
        if (frontier.Count > 0) truncated = true;  // cap'e ulasildi, hala expand edilecek var

        // Display field zenginlestirme — tek toplu Items okumasi (N+1 yerine 1 query)
        var allItemIds = aggregate.Keys.ToList();
        var itemsForDisplay = await _repository.GetItemsByIdsAsync(allItemIds, cancellationToken);
        var itemDisplayMap = itemsForDisplay.ToDictionary(x => x.Id);

        var lines = aggregate
            .Where(kv => itemDisplayMap.ContainsKey(kv.Key))  // pasif/silinmis itemlari atla
            .Select(kv =>
            {
                var item = itemDisplayMap[kv.Key];
                return new BOMExplodeLineDto(
                    ItemId:        item.Id,
                    ItemCode:      item.Code,
                    ItemName:      item.Name ?? item.Code,
                    ConfigId:      null,    // shipping note: aggregation seviyesinde config-bazli ayrismiyoruz
                    ConfigCode:    null,
                    TotalQuantity: Math.Round(kv.Value.Qty, 6, MidpointRounding.AwayFromZero),
                    Depth:         kv.Value.Depth,
                    IsLeaf:        kv.Value.IsLeaf);
            })
            .OrderBy(l => l.Depth)
            .ThenBy(l => l.ItemCode)
            .ToList();

        return new BOMExplodeResultDto(
            ParentItemId:   parent.Id,
            ParentItemCode: parent.Code,
            ParentItemName: parent.Name ?? parent.Code,
            ConfigId:       configId,
            ConfigCode:     configCode,
            Quantity:       quantity,
            MaxDepth:       maxDepthSeen,
            Truncated:      truncated,
            Lines:          lines);
    }

    public Task<IReadOnlyCollection<WhereUsedItemDto>> GetWhereUsedAsync(
        int componentItemId, CancellationToken cancellationToken)
    {
        if (componentItemId <= 0)
            throw new ArgumentException("Where-used sorgusu icin bilesen ItemId zorunlu (>0).");
        return _repository.GetWhereUsedAsync(componentItemId, cancellationToken);
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

    public Task<IReadOnlyDictionary<int, IReadOnlyList<MaterialGroupMappingDto>>> GetMaterialGroupMappingsBatchAsync(
        IReadOnlyCollection<int> stockCardIds, CancellationToken cancellationToken)
        => _repository.GetMaterialGroupMappingsBatchAsync(stockCardIds, cancellationToken);

    public Task<IReadOnlyDictionary<int, IReadOnlyList<CalibraHub.Domain.Entities.ItemUnit>>> GetItemUnitsBatchAsync(
        IReadOnlyCollection<int> itemIds, CancellationToken cancellationToken)
        => _repository.GetItemUnitsBatchAsync(itemIds, cancellationToken);

    public Task<IReadOnlyDictionary<int, IReadOnlyList<CalibraHub.Domain.Entities.ItemFeatureMapping>>> GetItemFeatureMappingsBatchAsync(
        IReadOnlyCollection<int> itemIds, CancellationToken cancellationToken)
        => _repository.GetItemFeatureMappingsBatchAsync(itemIds, cancellationToken);

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

        await _repository.SaveMaterialGroupMappingsAsync(request.ItemId, mappings, cancellationToken);
    }
}
