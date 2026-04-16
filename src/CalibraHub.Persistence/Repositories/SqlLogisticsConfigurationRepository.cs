using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Application.Contracts;
using CalibraHub.Domain.Entities;
using CalibraHub.Domain.Enums;
using CalibraHub.Persistence.Database;
using CalibraHub.Persistence.Options;
using Microsoft.Data.SqlClient;
using System.Text;

namespace CalibraHub.Persistence.Repositories;

public sealed class SqlLogisticsConfigurationRepository : ILogisticsConfigurationRepository
{
    private readonly SqlServerConnectionFactory _connectionFactory;
    private readonly string _schema;
    private readonly string _stockCardsTableName;
    private readonly string _materialCardFieldGroupsTableName;
    private readonly string _materialCardFieldSettingsTableName;
    private readonly string _materialCardFieldOptionsTableName;
    private readonly string _propertiesTableName;
    private readonly string _propertyValuesTableName;
    private readonly string _stockPropertyMappingsTableName;
    private readonly string _productConfigurationTableName;
    private readonly string _warehouseLocationsTableName;
    private readonly string _measureUnitDefinitionsTableName;
    private readonly string _productTreesTableName;
    private readonly string _productTreeLinesTableName;
    private readonly string _materialGroupsTableName;
    private readonly string _materialGroupMappingsTableName;
    private readonly string _stockUnitConversionsTableName;

    public SqlLogisticsConfigurationRepository(
        SqlServerConnectionFactory connectionFactory,
        CalibraDatabaseOptions options)
    {
        _connectionFactory = connectionFactory;
        _schema = string.IsNullOrWhiteSpace(options.Schema) ? "dbo" : options.Schema.Trim();
        _stockCardsTableName = $"[{_schema}].[Items]";
        _materialCardFieldGroupsTableName = $"[{_schema}].[material_card_field_groups]";
        _materialCardFieldSettingsTableName = $"[{_schema}].[material_card_field_settings]";
        _materialCardFieldOptionsTableName = $"[{_schema}].[material_card_field_options]";
        _propertiesTableName = $"[{_schema}].[configuration_properties]";
        _propertyValuesTableName = $"[{_schema}].[configuration_property_values]";
        _stockPropertyMappingsTableName = $"[{_schema}].[stock_card_property_mappings]";
        _productConfigurationTableName = $"[{_schema}].[ProductConfiguration]";
        _warehouseLocationsTableName = $"[{_schema}].[WarehouseLocations]";
        _measureUnitDefinitionsTableName = $"[{_schema}].[measure_unit_definitions]";
        _productTreesTableName = $"[{_schema}].[ProductTrees]";
        _productTreeLinesTableName = $"[{_schema}].[ProductTreeLines]";
        _materialGroupsTableName    = $"[{_schema}].[MaterialGroups]";
        _materialGroupMappingsTableName = $"[{_schema}].[MaterialGroupMappings]";
        _stockUnitConversionsTableName = $"[{_schema}].[stock_unit_conversions]";
    }

    public async Task<IReadOnlyCollection<StockCard>> GetStockCardsAsync(CancellationToken cancellationToken)
    {
        var stockCards = new List<StockCard>();

        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();

        command.CommandText = $"""
            SELECT [id],
                   [material_code],
                   [material_name],
                   [material_description],
                   [is_active],
                   [created_at],
                   [created_by_user_id],
                   [modified_at],
                   [modified_by_user_id],
                   [image_data],
                   [image_mime_type],
                   [track_combinations],
                   ISNULL([tax_rate], 20) AS [tax_rate],
                   [material_type_id]
            FROM {_stockCardsTableName}
            ORDER BY [material_code];
            """;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var stockCard = new StockCard
            {
                Id = reader.GetInt32(0),
                MaterialCode = reader.GetString(1),
                MaterialName = reader.GetString(2),
                MaterialDescription = reader.IsDBNull(3) ? null : reader.GetString(3),
                CreatedDate = reader.IsDBNull(5) ? null : reader.GetDateTime(5),
                CreatedByUserId = reader.IsDBNull(6) ? null : reader.GetInt32(6),
                ModifiedDate = reader.IsDBNull(7) ? null : reader.GetDateTime(7),
                ModifiedByUserId = reader.IsDBNull(8) ? null : reader.GetInt32(8),
                ImageData = reader.IsDBNull(9) ? null : (byte[])reader.GetValue(9),
                ImageMimeType = reader.IsDBNull(10) ? null : reader.GetString(10),
                TrackCombinations = !reader.IsDBNull(11) && reader.GetBoolean(11),
                TaxRate = reader.IsDBNull(12) ? 20m : reader.GetDecimal(12),
                MaterialTypeId = reader.IsDBNull(13) ? null : reader.GetInt32(13)
            };

            if (!reader.GetBoolean(4))
            {
                stockCard.Deactivate();
            }

            stockCards.Add(stockCard);
        }

        return stockCards;
    }

    public async Task<IReadOnlyCollection<MaterialCardFieldGroup>> GetMaterialCardFieldGroupsAsync(
        CancellationToken cancellationToken)
    {
        var groups = new List<MaterialCardFieldGroup>();

        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT [id], [group_key], [group_label], [display_order], [is_active], [created_at], [updated_at], [screen_code], [layer_key]
            FROM {_materialCardFieldGroupsTableName}
            ORDER BY [display_order], [group_label];
            """;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var group = new MaterialCardFieldGroup
            {
                Id = reader.GetGuid(0),
                GroupKey = reader.GetString(1),
                GroupLabel = reader.GetString(2),
                DisplayOrder = reader.GetInt32(3),
                CreatedAt = reader.GetFieldValue<DateTime>(5),
                ScreenCode = reader.IsDBNull(7) ? "MaterialCards" : reader.GetString(7),
                LayerKey = reader.IsDBNull(8) ? null : reader.GetString(8),
            };

            if (!reader.GetBoolean(4))
            {
                group.SetActive(false);
            }

            groups.Add(group);
        }

        return groups;
    }

    public async Task<IReadOnlyCollection<MaterialCardDynamicFieldDefinition>> GetMaterialCardDynamicFieldDefinitionsAsync(
        CancellationToken cancellationToken)
    {
        var definitions = new List<MaterialCardDynamicFieldDefinition>();

        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT [id],
                   [group_id],
                   [field_key],
                   [field_label],
                   [data_type],
                   [is_visible],
                   [is_required],
                   [default_value],
                   [display_order],
                   [column_span],
                   [is_system],
                   [is_active],
                   [created_at],
                   [updated_at],
                   [screen_code],
                   [layer_key]
            FROM {_materialCardFieldSettingsTableName}
            ORDER BY [display_order], [field_label];
            """;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var definition = new MaterialCardDynamicFieldDefinition
            {
                Id = reader.GetGuid(0),
                GroupId = reader.IsDBNull(1) ? null : reader.GetGuid(1),
                FieldKey = reader.GetString(2),
                FieldLabel = reader.GetString(3),
                DataType = ParseMaterialCardDynamicFieldDataType(reader.IsDBNull(4) ? null : reader.GetString(4)),
                IsVisible = reader.GetBoolean(5),
                IsRequired = reader.GetBoolean(6),
                DefaultValue = reader.IsDBNull(7) ? null : reader.GetString(7),
                DisplayOrder = reader.GetInt32(8),
                ColumnSpan = reader.IsDBNull(9) ? 1 : reader.GetInt32(9),
                IsSystem = !reader.IsDBNull(10) && reader.GetBoolean(10),
                CreatedAt = reader.GetFieldValue<DateTime>(12),
                ScreenCode = reader.IsDBNull(14) ? "MaterialCards" : reader.GetString(14),
                LayerKey = reader.IsDBNull(15) ? null : reader.GetString(15),
            };

            if (!reader.GetBoolean(11))
            {
                definition.SetActive(false);
            }

            definitions.Add(definition);
        }

        return definitions;
    }

    public async Task<IReadOnlyCollection<MaterialCardFieldOption>> GetMaterialCardFieldOptionsAsync(
        CancellationToken cancellationToken)
    {
        var options = new List<MaterialCardFieldOption>();

        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT [id], [field_definition_id], [option_key], [option_label], [sort_order], [is_active], [created_at], [updated_at]
            FROM {_materialCardFieldOptionsTableName}
            ORDER BY [field_definition_id], [sort_order], [option_label];
            """;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var option = new MaterialCardFieldOption
            {
                Id = reader.GetGuid(0),
                FieldDefinitionId = reader.GetGuid(1),
                OptionKey = reader.GetString(2),
                OptionLabel = reader.GetString(3),
                SortOrder = reader.GetInt32(4),
                CreatedAt = reader.GetFieldValue<DateTime>(6)
            };

            if (!reader.GetBoolean(5))
            {
                option.SetActive(false);
            }

            options.Add(option);
        }

        return options;
    }

    public async Task UpsertMaterialCardFieldGroupAsync(MaterialCardFieldGroup group, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            IF EXISTS (SELECT 1 FROM {_materialCardFieldGroupsTableName} WHERE [id] = @Id)
            BEGIN
                UPDATE {_materialCardFieldGroupsTableName}
                SET
                    [screen_code] = @ScreenCode,
                    [layer_key] = @LayerKey,
                    [group_key] = @GroupKey,
                    [group_label] = @GroupLabel,
                    [display_order] = @DisplayOrder,
                    [is_active] = @IsActive,
                    [updated_at] = @UpdatedAt
                WHERE [id] = @Id;
            END
            ELSE
            BEGIN
                INSERT INTO {_materialCardFieldGroupsTableName}
                    ([id], [screen_code], [layer_key], [group_key], [group_label], [display_order], [is_active], [created_at], [updated_at])
                VALUES
                    (@Id, @ScreenCode, @LayerKey, @GroupKey, @GroupLabel, @DisplayOrder, @IsActive, @CreatedAt, @UpdatedAt);
            END;
            """;

        command.Parameters.Add(new SqlParameter("@Id", group.Id));
        command.Parameters.Add(new SqlParameter("@ScreenCode", group.ScreenCode ?? "MaterialCards"));
        command.Parameters.Add(new SqlParameter("@LayerKey", (object?)group.LayerKey ?? DBNull.Value));
        command.Parameters.Add(new SqlParameter("@GroupKey", group.GroupKey));
        command.Parameters.Add(new SqlParameter("@GroupLabel", group.GroupLabel));
        command.Parameters.Add(new SqlParameter("@DisplayOrder", group.DisplayOrder));
        command.Parameters.Add(new SqlParameter("@IsActive", group.IsActive));
        command.Parameters.Add(new SqlParameter("@CreatedAt", group.CreatedAt));
        command.Parameters.Add(new SqlParameter("@UpdatedAt", DateTime.Now));

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task UpsertMaterialCardDynamicFieldAsync(
        MaterialCardDynamicFieldDefinition field,
        IReadOnlyCollection<MaterialCardFieldOption> options,
        CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);

        await using (var fieldCommand = connection.CreateCommand())
        {
            fieldCommand.Transaction = transaction;
            fieldCommand.CommandText = $"""
                IF EXISTS (SELECT 1 FROM {_materialCardFieldSettingsTableName} WHERE [id] = @Id)
                BEGIN
                    UPDATE {_materialCardFieldSettingsTableName}
                    SET
                        [screen_code] = @ScreenCode,
                        [layer_key] = @LayerKey,
                        [group_id] = @GroupId,
                        [field_key] = @FieldKey,
                        [field_label] = @FieldLabel,
                        [data_type] = @DataType,
                        [is_visible] = @IsVisible,
                        [is_required] = @IsRequired,
                        [default_value] = @DefaultValue,
                        [display_order] = @DisplayOrder,
                        [column_span] = @ColumnSpan,
                        [is_system] = @IsSystem,
                        [is_active] = @IsActive,
                        [updated_at] = @UpdatedAt
                    WHERE [id] = @Id;
                END
                ELSE
                BEGIN
                    INSERT INTO {_materialCardFieldSettingsTableName}
                        ([id], [screen_code], [layer_key], [group_id], [field_key], [field_label], [data_type], [is_visible], [is_required], [default_value], [display_order], [column_span], [is_system], [is_active], [created_at], [updated_at])
                    VALUES
                        (@Id, @ScreenCode, @LayerKey, @GroupId, @FieldKey, @FieldLabel, @DataType, @IsVisible, @IsRequired, @DefaultValue, @DisplayOrder, @ColumnSpan, @IsSystem, @IsActive, @CreatedAt, @UpdatedAt);
                END;
                """;

            fieldCommand.Parameters.Add(new SqlParameter("@Id", field.Id));
            fieldCommand.Parameters.Add(new SqlParameter("@ScreenCode", field.ScreenCode ?? "MaterialCards"));
            fieldCommand.Parameters.Add(new SqlParameter("@LayerKey", (object?)field.LayerKey ?? DBNull.Value));
            fieldCommand.Parameters.Add(new SqlParameter("@GroupId", (object?)field.GroupId ?? DBNull.Value));
            fieldCommand.Parameters.Add(new SqlParameter("@FieldKey", field.FieldKey));
            fieldCommand.Parameters.Add(new SqlParameter("@FieldLabel", field.FieldLabel));
            fieldCommand.Parameters.Add(new SqlParameter("@DataType", ToMaterialCardDynamicFieldDataTypeValue(field.DataType)));
            fieldCommand.Parameters.Add(new SqlParameter("@IsVisible", field.IsVisible));
            fieldCommand.Parameters.Add(new SqlParameter("@IsRequired", field.IsRequired));
            fieldCommand.Parameters.Add(new SqlParameter("@DefaultValue", (object?)field.DefaultValue ?? DBNull.Value));
            fieldCommand.Parameters.Add(new SqlParameter("@DisplayOrder", field.DisplayOrder));
            fieldCommand.Parameters.Add(new SqlParameter("@ColumnSpan", field.ColumnSpan));
            fieldCommand.Parameters.Add(new SqlParameter("@IsSystem", field.IsSystem));
            fieldCommand.Parameters.Add(new SqlParameter("@IsActive", field.IsActive));
            fieldCommand.Parameters.Add(new SqlParameter("@CreatedAt", field.CreatedAt));
            fieldCommand.Parameters.Add(new SqlParameter("@UpdatedAt", DateTime.Now));

            await fieldCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        foreach (var option in options)
        {
            await using var optionCommand = connection.CreateCommand();
            optionCommand.Transaction = transaction;
            optionCommand.CommandText = $"""
                IF EXISTS (SELECT 1 FROM {_materialCardFieldOptionsTableName} WHERE [id] = @Id)
                BEGIN
                    UPDATE {_materialCardFieldOptionsTableName}
                    SET
                        [option_key] = @OptionKey,
                        [option_label] = @OptionLabel,
                        [sort_order] = @SortOrder,
                        [is_active] = @IsActive,
                        [updated_at] = @UpdatedAt
                    WHERE [id] = @Id;
                END
                ELSE
                BEGIN
                    INSERT INTO {_materialCardFieldOptionsTableName}
                        ([id], [field_definition_id], [option_key], [option_label], [sort_order], [is_active], [created_at], [updated_at])
                    VALUES
                        (@Id, @FieldDefinitionId, @OptionKey, @OptionLabel, @SortOrder, @IsActive, @CreatedAt, @UpdatedAt);
                END;
                """;

            optionCommand.Parameters.Add(new SqlParameter("@Id", option.Id));
            optionCommand.Parameters.Add(new SqlParameter("@FieldDefinitionId", option.FieldDefinitionId));
            optionCommand.Parameters.Add(new SqlParameter("@OptionKey", option.OptionKey));
            optionCommand.Parameters.Add(new SqlParameter("@OptionLabel", option.OptionLabel));
            optionCommand.Parameters.Add(new SqlParameter("@SortOrder", option.SortOrder));
            optionCommand.Parameters.Add(new SqlParameter("@IsActive", option.IsActive));
            optionCommand.Parameters.Add(new SqlParameter("@CreatedAt", option.CreatedAt));
            optionCommand.Parameters.Add(new SqlParameter("@UpdatedAt", DateTime.Now));

            await optionCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<IReadOnlyCollection<MaterialCardFieldSetting>> GetMaterialCardFieldSettingsAsync(
        CancellationToken cancellationToken)
    {
        var settings = new List<MaterialCardFieldSetting>();

        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT [id], [field_key], [field_label], [is_visible], [is_required], [display_order], [created_at], [updated_at]
            FROM {_materialCardFieldSettingsTableName}
            ORDER BY [display_order], [field_label];
            """;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            settings.Add(new MaterialCardFieldSetting
            {
                Id = reader.GetGuid(0),
                FieldKey = reader.GetString(1),
                FieldLabel = reader.GetString(2),
                IsVisible = reader.GetBoolean(3),
                IsRequired = reader.GetBoolean(4),
                DisplayOrder = reader.GetInt32(5),
                CreatedAt = reader.GetFieldValue<DateTime>(6),
                UpdatedAt = reader.GetFieldValue<DateTime>(7)
            });
        }

        return settings;
    }

    public async Task UpsertMaterialCardFieldSettingsAsync(
        IReadOnlyCollection<MaterialCardFieldSetting> settings,
        CancellationToken cancellationToken)
    {
        if (settings.Count == 0)
        {
            return;
        }

        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);

        foreach (var setting in settings)
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = $"""
                IF EXISTS (SELECT 1 FROM {_materialCardFieldSettingsTableName} WHERE [field_key] = @FieldKey)
                BEGIN
                    UPDATE {_materialCardFieldSettingsTableName}
                    SET
                        [field_label] = @FieldLabel,
                        [is_visible] = @IsVisible,
                        [is_required] = @IsRequired,
                        [display_order] = @DisplayOrder,
                        [updated_at] = @UpdatedAt
                    WHERE [field_key] = @FieldKey;
                END
                ELSE
                BEGIN
                    INSERT INTO {_materialCardFieldSettingsTableName}
                        ([id], [field_key], [field_label], [is_visible], [is_required], [display_order], [created_at], [updated_at])
                    VALUES
                        (@Id, @FieldKey, @FieldLabel, @IsVisible, @IsRequired, @DisplayOrder, @CreatedAt, @UpdatedAt);
                END;
                """;

            command.Parameters.Add(new SqlParameter("@Id", setting.Id));
            command.Parameters.Add(new SqlParameter("@FieldKey", setting.FieldKey));
            command.Parameters.Add(new SqlParameter("@FieldLabel", setting.FieldLabel));
            command.Parameters.Add(new SqlParameter("@IsVisible", setting.IsVisible));
            command.Parameters.Add(new SqlParameter("@IsRequired", setting.IsRequired));
            command.Parameters.Add(new SqlParameter("@DisplayOrder", setting.DisplayOrder));
            command.Parameters.Add(new SqlParameter("@CreatedAt", setting.CreatedAt));
            command.Parameters.Add(new SqlParameter("@UpdatedAt", setting.UpdatedAt));

            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<IReadOnlyCollection<ConfigurationProperty>> GetPropertiesAsync(CancellationToken cancellationToken)
    {
        var properties = new List<ConfigurationProperty>();

        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT [id], [code], [name], [data_type], [is_active], [created_at]
            FROM {_propertiesTableName}
            ORDER BY [code];
            """;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var dataTypeRaw = reader.GetString(3);
            if (!Enum.TryParse(dataTypeRaw, true, out ConfigurationFieldDataType dataType) ||
                !Enum.IsDefined(dataType))
            {
                dataType = ConfigurationFieldDataType.Text;
            }

            var property = new ConfigurationProperty
            {
                Id = reader.GetGuid(0),
                Code = reader.GetString(1),
                Name = reader.GetString(2),
                DataType = dataType,
                CreatedAt = reader.GetFieldValue<DateTime>(5)
            };

            if (!reader.GetBoolean(4))
            {
                property.Deactivate();
            }

            properties.Add(property);
        }

        return properties;
    }

    public async Task<IReadOnlyCollection<ConfigurationPropertyValue>> GetPropertyValuesAsync(CancellationToken cancellationToken)
    {
        var values = new List<ConfigurationPropertyValue>();

        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT [id], [property_id], [code], [description], [value], [sort_order], [is_active], [created_at]
            FROM {_propertyValuesTableName}
            ORDER BY [property_id], [sort_order], [code];
            """;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var value = new ConfigurationPropertyValue
            {
                Id = reader.GetGuid(0),
                PropertyId = reader.GetGuid(1),
                Code = reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
                Description = reader.IsDBNull(3) ? string.Empty : reader.GetString(3),
                Value = reader.GetString(4),
                SortOrder = reader.GetInt32(5),
                CreatedAt = reader.GetFieldValue<DateTime>(7)
            };

            if (!reader.GetBoolean(6))
            {
                value.Deactivate();
            }

            values.Add(value);
        }

        return values;
    }

    public async Task<IReadOnlyCollection<StockCardPropertyMapping>> GetStockPropertyMappingsAsync(CancellationToken cancellationToken)
    {
        var mappings = new List<StockCardPropertyMapping>();

        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT [id], [stock_card_id], [property_id], [property_value_id], [configuration_code], [text_value], [numeric_value], [date_value], [is_active], [created_at]
            FROM {_stockPropertyMappingsTableName}
            ORDER BY [created_at] DESC;
            """;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var mapping = new StockCardPropertyMapping
            {
                Id = reader.GetGuid(0),
                StockCardId = reader.GetInt32(1),
                PropertyId = reader.GetGuid(2),
                PropertyValueId = reader.IsDBNull(3) ? null : reader.GetGuid(3),
                ConfigurationCode = reader.IsDBNull(4) ? null : reader.GetString(4),
                TextValue = reader.IsDBNull(5) ? null : reader.GetString(5),
                NumericValue = reader.IsDBNull(6) ? null : reader.GetDecimal(6),
                DateValue = reader.IsDBNull(7) ? null : reader.GetDateTime(7),
                CreatedAt = reader.GetFieldValue<DateTime>(9)
            };

            if (!reader.GetBoolean(8))
            {
                mapping.Deactivate();
            }

            mappings.Add(mapping);
        }

        return mappings;
    }

    public async Task<IReadOnlyCollection<ProductConfigurationRecord>> GetProductConfigurationRecordsAsync(
        CancellationToken cancellationToken)
    {
        var records = new List<ProductConfigurationRecord>();

        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT [Id], [ParentId], [RecordType], [RecordCode], [RecordName], [DataType], [RelatedMaterialCode], [IsActive], [CreatedDate]
            FROM {_productConfigurationTableName}
            ORDER BY [RecordType], [ParentId], [RecordCode], [Id];
            """;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            records.Add(new ProductConfigurationRecord
            {
                Id = reader.GetInt32(0),
                ParentId = reader.IsDBNull(1) ? null : reader.GetInt32(1),
                RecordType = reader.GetString(2),
                RecordCode = reader.GetString(3),
                RecordName = reader.GetString(4),
                DataType = reader.IsDBNull(5) ? null : reader.GetString(5),
                RelatedMaterialCode = reader.IsDBNull(6) ? null : reader.GetString(6),
                IsActive = reader.GetBoolean(7),
                CreatedDate = reader.GetDateTime(8)
            });
        }

        return records;
    }

    public async Task<IReadOnlyCollection<MeasureUnitDefinition>> GetMeasureUnitDefinitionsAsync(
        CancellationToken cancellationToken)
    {
        var definitions = new List<MeasureUnitDefinition>();

        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT [Id], [UnitCode], [UnitName], [SortOrder], [IsActive],
                   CASE WHEN COL_LENGTH('{_schema}.measure_unit_definitions','IntlCode') IS NOT NULL THEN NULL ELSE NULL END
            FROM {_measureUnitDefinitionsTableName}
            ORDER BY [SortOrder], [UnitCode];
            """;

        // IntlCode kolonunu güvenli oku (eski DB'lerde olmayabilir)
        var hasIntlCode = false;
        try
        {
            await using var schemaCmd = connection.CreateCommand();
            schemaCmd.CommandText = $"SELECT COL_LENGTH('{_schema}.measure_unit_definitions','IntlCode')";
            var result = await schemaCmd.ExecuteScalarAsync(cancellationToken);
            hasIntlCode = result != null && result != DBNull.Value;
        }
        catch { /* ignore */ }

        if (hasIntlCode)
        {
            command.CommandText = $"""
                SELECT [Id], [UnitCode], [UnitName], [SortOrder], [IsActive], [IntlCode]
                FROM {_measureUnitDefinitionsTableName}
                ORDER BY [SortOrder], [UnitCode];
                """;
        }

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            definitions.Add(new MeasureUnitDefinition
            {
                Id = reader.GetInt32(0),
                UnitCode = reader.GetString(1),
                UnitName = reader.GetString(2),
                SortOrder = reader.GetInt32(3),
                IsActive = reader.GetBoolean(4),
                IntlCode = hasIntlCode && !reader.IsDBNull(5) ? reader.GetString(5) : null,
            });
        }

        return definitions;
    }

    public async Task<(int Id, string Code)> AddProductFeatureAsync(
        string name,
        string dataType,
        bool isActive,
        string? unitOfMeasure,
        CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SET TRANSACTION ISOLATION LEVEL SERIALIZABLE;
            BEGIN TRANSACTION;

            DECLARE @NextNo INT;
            DECLARE @GeneratedCode NVARCHAR(100);

            SELECT @NextNo = ISNULL(MAX(TRY_CAST(RIGHT([RecordCode], 3) AS INT)), 0) + 1
            FROM {_productConfigurationTableName} WITH (UPDLOCK, HOLDLOCK)
            WHERE [RecordType] = N'FEATURE'
              AND [RecordCode] LIKE N'OZ[0-9][0-9][0-9]';

            SET @GeneratedCode = N'OZ' + RIGHT(N'000' + CAST(@NextNo AS NVARCHAR(3)), 3);

            INSERT INTO {_productConfigurationTableName}
                ([ParentId], [RecordType], [RecordCode], [RecordName], [DataType], [RelatedMaterialCode], [IsActive], [CreatedDate])
            VALUES
                (NULL, N'FEATURE', @GeneratedCode, @RecordName, @DataType, @UnitOfMeasure, @IsActive, GETDATE());

            SELECT CAST(SCOPE_IDENTITY() AS INT) AS [Id], @GeneratedCode AS [Code];

            COMMIT TRANSACTION;
            """;

        command.Parameters.Add(new SqlParameter("@RecordName", name));
        command.Parameters.Add(new SqlParameter("@DataType", dataType));
        command.Parameters.Add(new SqlParameter("@IsActive", isActive));
        command.Parameters.Add(new SqlParameter("@UnitOfMeasure", (object?)unitOfMeasure ?? DBNull.Value));

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new InvalidOperationException("Ozellik kaydi olusturulamadi.");
        }

        return (reader.GetInt32(0), reader.GetString(1));
    }

    public async Task<(int Id, string Code)> AddProductValueAsync(
        int featureId,
        string name,
        bool isActive,
        string? aciklama,
        CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SET TRANSACTION ISOLATION LEVEL SERIALIZABLE;
            BEGIN TRANSACTION;

            DECLARE @NextNo INT;
            DECLARE @GeneratedCode NVARCHAR(100);

            SELECT @NextNo = ISNULL(MAX(TRY_CAST(RIGHT([RecordCode], 3) AS INT)), 0) + 1
            FROM {_productConfigurationTableName} WITH (UPDLOCK, HOLDLOCK)
            WHERE [RecordType] = N'VALUE'
              AND [ParentId] = @FeatureId
              AND [RecordCode] LIKE N'DG[0-9][0-9][0-9]';

            SET @GeneratedCode = N'DG' + RIGHT(N'000' + CAST(@NextNo AS NVARCHAR(3)), 3);

            INSERT INTO {_productConfigurationTableName}
                ([ParentId], [RecordType], [RecordCode], [RecordName], [DataType], [RelatedMaterialCode], [IsActive], [CreatedDate])
            VALUES
                (@FeatureId, N'VALUE', @GeneratedCode, @RecordName, NULL, @Aciklama, @IsActive, GETDATE());

            SELECT CAST(SCOPE_IDENTITY() AS INT) AS [Id], @GeneratedCode AS [Code];

            COMMIT TRANSACTION;
            """;

        command.Parameters.Add(new SqlParameter("@FeatureId", featureId));
        command.Parameters.Add(new SqlParameter("@RecordName", name));
        command.Parameters.Add(new SqlParameter("@IsActive", isActive));
        command.Parameters.Add(new SqlParameter("@Aciklama", (object?)aciklama ?? DBNull.Value));

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new InvalidOperationException("Deger kaydi olusturulamadi.");
        }

        return (reader.GetInt32(0), reader.GetString(1));
    }

    public async Task<(int Id, string Code)> AddProductConfigAsync(
        int valueId,
        string relatedMaterialCode,
        string name,
        bool isActive,
        CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SET TRANSACTION ISOLATION LEVEL SERIALIZABLE;
            BEGIN TRANSACTION;

            DECLARE @NextNo INT;
            DECLARE @GeneratedCode NVARCHAR(100);

            SELECT @NextNo = ISNULL(MAX(TRY_CAST(RIGHT([RecordCode], 3) AS INT)), 0) + 1
            FROM {_productConfigurationTableName} WITH (UPDLOCK, HOLDLOCK)
            WHERE [RecordType] = N'CONFIG'
              AND [RelatedMaterialCode] = @RelatedMaterialCode
              AND LEFT([RecordCode], LEN(@RelatedMaterialCode) + 1) = @RelatedMaterialCode + N'-';

            SET @GeneratedCode = @RelatedMaterialCode + N'-' + RIGHT(N'000' + CAST(@NextNo AS NVARCHAR(3)), 3);

            INSERT INTO {_productConfigurationTableName}
                ([ParentId], [RecordType], [RecordCode], [RecordName], [DataType], [RelatedMaterialCode], [IsActive], [CreatedDate])
            VALUES
                (@ValueId, N'CONFIG', @GeneratedCode, @RecordName, NULL, @RelatedMaterialCode, @IsActive, GETDATE());

            SELECT CAST(SCOPE_IDENTITY() AS INT) AS [Id], @GeneratedCode AS [Code];

            COMMIT TRANSACTION;
            """;

        command.Parameters.Add(new SqlParameter("@ValueId", valueId));
        command.Parameters.Add(new SqlParameter("@RelatedMaterialCode", relatedMaterialCode));
        command.Parameters.Add(new SqlParameter("@RecordName", name));
        command.Parameters.Add(new SqlParameter("@IsActive", isActive));

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new InvalidOperationException("Yapilandirma kaydi olusturulamadi.");
        }

        return (reader.GetInt32(0), reader.GetString(1));
    }

    public async Task<(int Id, string Code)> AddProductConfigurationCombinationAsync(
        string relatedMaterialCode,
        string configName,
        IReadOnlyCollection<int> valueIds,
        bool isActive,
        CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        var sql = new StringBuilder();
        sql.AppendLine("SET TRANSACTION ISOLATION LEVEL SERIALIZABLE;");
        sql.AppendLine("BEGIN TRANSACTION;");
        sql.AppendLine("DECLARE @NextNo INT;");
        sql.AppendLine("DECLARE @GeneratedCode NVARCHAR(100);");
        sql.AppendLine($"SELECT @NextNo = ISNULL(MAX(TRY_CAST(RIGHT([RecordCode], 3) AS INT)), 0) + 1 FROM {_productConfigurationTableName} WITH (UPDLOCK, HOLDLOCK) WHERE [RecordType] = N'CONFIG' AND [RelatedMaterialCode] = @RelatedMaterialCode AND LEFT([RecordCode], LEN(@RelatedMaterialCode) + 1) = @RelatedMaterialCode + N'-';");
        sql.AppendLine("SET @GeneratedCode = @RelatedMaterialCode + N'-' + RIGHT(N'000' + CAST(@NextNo AS NVARCHAR(3)), 3);");
        sql.AppendLine($"INSERT INTO {_productConfigurationTableName} ([ParentId], [RecordType], [RecordCode], [RecordName], [DataType], [RelatedMaterialCode], [IsActive], [CreatedDate]) VALUES (NULL, N'CONFIG', @GeneratedCode, @RecordName, NULL, @RelatedMaterialCode, @IsActive, GETDATE());");
        sql.AppendLine("DECLARE @ConfigId INT = SCOPE_IDENTITY();");

        var pIndex = 0;
        foreach (var valId in valueIds)
        {
            sql.AppendLine($"INSERT INTO {_productConfigurationTableName} ([ParentId], [RecordType], [RecordCode], [RecordName], [DataType], [RelatedMaterialCode], [IsActive], [CreatedDate]) VALUES (@ConfigId, N'CONFIG', CAST(NEWID() AS NVARCHAR(100)), CAST(@ValueId{pIndex} AS NVARCHAR(255)), NULL, @RelatedMaterialCode, 1, GETDATE());");
            command.Parameters.Add(new SqlParameter($"@ValueId{pIndex}", valId));
            pIndex++;
        }

        sql.AppendLine("SELECT @ConfigId AS [Id], @GeneratedCode AS [Code];");
        sql.AppendLine("COMMIT TRANSACTION;");

        command.CommandText = sql.ToString();
        command.Parameters.Add(new SqlParameter("@RelatedMaterialCode", relatedMaterialCode));
        command.Parameters.Add(new SqlParameter("@RecordName", configName));
        command.Parameters.Add(new SqlParameter("@IsActive", isActive));

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new InvalidOperationException("Kombinasyon kaydi olusturulamadi.");
        }

        return (reader.GetInt32(0), reader.GetString(1));
    }

    public async Task ReplaceProductFeatureStockLinksAsync(
        int featureId,
        string[] stockCodes,
        CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        var sql = new StringBuilder();
        sql.AppendLine("BEGIN TRANSACTION;");
        sql.AppendLine($"""
            DELETE FROM {_productConfigurationTableName}
            WHERE [RecordType] = N'FEATURE_STOCK'
              AND [ParentId] = @FeatureId;
            """);

        command.Parameters.Add(new SqlParameter("@FeatureId", featureId));

        var normalizedCodes = stockCodes
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        for (var index = 0; index < normalizedCodes.Length; index++)
        {
            sql.AppendLine($"""
                INSERT INTO {_productConfigurationTableName}
                    ([ParentId], [RecordType], [RecordCode], [RecordName], [DataType], [RelatedMaterialCode], [IsActive], [CreatedDate])
                VALUES
                    (@FeatureId, N'FEATURE_STOCK', @StockCode{index}, @StockCode{index}, NULL, @StockCode{index}, 1, GETDATE());
                """);

            command.Parameters.Add(new SqlParameter($"@StockCode{index}", normalizedCodes[index]));
        }

        sql.AppendLine("COMMIT TRANSACTION;");
        command.CommandText = sql.ToString();

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task UpdateProductFeatureAsync(int id, string name, string dataType, string? unitOfMeasure, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            UPDATE {_productConfigurationTableName}
            SET [RecordName]          = @RecordName,
                [DataType]            = @DataType,
                [RelatedMaterialCode] = @UnitOfMeasure
            WHERE [Id] = @Id
              AND [RecordType] = N'FEATURE';
            """;

        command.Parameters.Add(new SqlParameter("@Id", id));
        command.Parameters.Add(new SqlParameter("@RecordName", name));
        command.Parameters.Add(new SqlParameter("@DataType", dataType));
        command.Parameters.Add(new SqlParameter("@UnitOfMeasure", (object?)unitOfMeasure ?? DBNull.Value));

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task DeleteProductFeatureAsync(int id, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            BEGIN TRANSACTION;

            DELETE FROM {_productConfigurationTableName}
            WHERE [RecordType] = N'FEATURE_STOCK'
              AND [ParentId] = @Id;

            DELETE FROM {_productConfigurationTableName}
            WHERE [RecordType] = N'CONFIG'
              AND [ParentId] IN (
                    SELECT [Id]
                    FROM {_productConfigurationTableName}
                    WHERE [RecordType] = N'VALUE'
                      AND [ParentId] = @Id);

            DELETE FROM {_productConfigurationTableName}
            WHERE [RecordType] = N'VALUE'
              AND [ParentId] = @Id;

            DELETE FROM {_productConfigurationTableName}
            WHERE [Id] = @Id
              AND [RecordType] = N'FEATURE';

            COMMIT TRANSACTION;
            """;
        command.Parameters.Add(new SqlParameter("@Id", id));

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task DeleteProductValueAsync(int id, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            BEGIN TRANSACTION;

            DELETE FROM {_productConfigurationTableName}
            WHERE [RecordType] = N'CONFIG'
              AND [ParentId] = @Id;

            DELETE FROM {_productConfigurationTableName}
            WHERE [Id] = @Id
              AND [RecordType] = N'VALUE';

            COMMIT TRANSACTION;
            """;
        command.Parameters.Add(new SqlParameter("@Id", id));

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task UpdateProductValueAsync(int id, string name, string? aciklama, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            UPDATE {_productConfigurationTableName}
            SET [RecordName] = @RecordName,
                [RelatedMaterialCode] = @Aciklama
            WHERE [Id] = @Id
              AND [RecordType] = N'VALUE';
            """;
        command.Parameters.Add(new SqlParameter("@Id", id));
        command.Parameters.Add(new SqlParameter("@RecordName", name));
        command.Parameters.Add(new SqlParameter("@Aciklama", (object?)aciklama ?? DBNull.Value));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task DeleteProductConfigAsync(int id, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SET TRANSACTION ISOLATION LEVEL SERIALIZABLE;
            BEGIN TRANSACTION;

            DELETE FROM {_productConfigurationTableName}
            WHERE [RecordType] = N'CONFIG' AND [ParentId] = @Id;

            DELETE FROM {_productConfigurationTableName}
            WHERE [RecordType] = N'CONFIG' AND [Id] = @Id;

            COMMIT TRANSACTION;
            """;

        command.Parameters.Add(new SqlParameter("@Id", id));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyCollection<WarehouseLocation>> GetWarehouseLocationsAsync(CancellationToken cancellationToken)
    {
        var locations = new List<WarehouseLocation>();

        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT [Id],
                   [ParentId],
                   [LocationTypeCode],
                   [LocationCode],
                   [LocationName],
                   [SortOrder],
                   [MaxWeightCapacity],
                   [VolumeCapacity],
                   [IsActive]
            FROM {_warehouseLocationsTableName}
            ORDER BY [SortOrder], [LocationTypeCode], [LocationCode];
            """;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            locations.Add(new WarehouseLocation
            {
                Id = reader.GetInt32(0),
                ParentId = reader.IsDBNull(1) ? null : reader.GetInt32(1),
                LocationTypeCode = reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
                LocationCode = reader.IsDBNull(3) ? string.Empty : reader.GetString(3),
                LocationName = reader.IsDBNull(4) ? null : reader.GetString(4),
                SortOrder = reader.IsDBNull(5) ? 0 : reader.GetInt32(5),
                MaxWeightCapacity = reader.IsDBNull(6) ? null : reader.GetDecimal(6),
                VolumeCapacity = reader.IsDBNull(7) ? null : reader.GetDecimal(7),
                IsActive = !reader.IsDBNull(8) && reader.GetBoolean(8)
            });
        }

        return locations;
    }

    public async Task<int> AddStockCardAsync(StockCard stockCard, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            INSERT INTO {_stockCardsTableName}
                ([material_code], [material_name], [material_description], [material_type_id], [is_active], [created_at], [created_by_user_id], [image_data], [image_mime_type], [track_combinations], [tax_rate], [updated_at])
            VALUES
                (@MaterialCode, @MaterialName, @MaterialDescription, @MaterialTypeId, @IsActive, @CreatedDate, @CreatedByUserId, @ImageData, @ImageMimeType, @TrackCombinations, @TaxRate, GETDATE());
            SELECT CAST(SCOPE_IDENTITY() AS INT);
            """;

        command.Parameters.Add(new SqlParameter("@MaterialCode", stockCard.MaterialCode));
        command.Parameters.Add(new SqlParameter("@MaterialName", stockCard.MaterialName));
        command.Parameters.Add(new SqlParameter("@MaterialDescription", (object?)stockCard.MaterialDescription ?? DBNull.Value));
        command.Parameters.Add(new SqlParameter("@MaterialTypeId", (object?)stockCard.MaterialTypeId ?? DBNull.Value));
        command.Parameters.Add(new SqlParameter("@IsActive", stockCard.IsActive ? 1 : 0));
        command.Parameters.Add(new SqlParameter("@CreatedDate", (object?)stockCard.CreatedDate ?? DBNull.Value));
        command.Parameters.Add(new SqlParameter("@CreatedByUserId", (object?)stockCard.CreatedByUserId ?? DBNull.Value));
        var imgParam = new SqlParameter("@ImageData", System.Data.SqlDbType.VarBinary, -1);
        imgParam.Value = (object?)stockCard.ImageData ?? DBNull.Value;
        command.Parameters.Add(imgParam);
        command.Parameters.Add(new SqlParameter("@ImageMimeType", (object?)stockCard.ImageMimeType ?? DBNull.Value));
        command.Parameters.Add(new SqlParameter("@TrackCombinations", stockCard.TrackCombinations ? 1 : 0));
        command.Parameters.Add(new SqlParameter("@TaxRate", stockCard.TaxRate));

        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));
    }

    public async Task UpdateStockCardAsync(StockCard stockCard, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            UPDATE {_stockCardsTableName}
            SET
                [material_code] = @MaterialCode,
                [material_name] = @MaterialName,
                [material_description] = @MaterialDescription,
                [material_type_id] = @MaterialTypeId,
                [modified_at] = @ModifiedDate,
                [modified_by_user_id] = @ModifiedByUserId,
                [track_combinations] = @TrackCombinations,
                [tax_rate] = @TaxRate,
                [image_data] = CASE WHEN @UpdateImage = 1 THEN @ImageData ELSE [image_data] END,
                [image_mime_type] = CASE WHEN @UpdateImage = 1 THEN @ImageMimeType ELSE [image_mime_type] END,
                [updated_at] = GETDATE()
            WHERE [id] = @Id;
            """;

        command.Parameters.Add(new SqlParameter("@Id", stockCard.Id));
        command.Parameters.Add(new SqlParameter("@MaterialCode", stockCard.MaterialCode));
        command.Parameters.Add(new SqlParameter("@MaterialName", stockCard.MaterialName));
        command.Parameters.Add(new SqlParameter("@MaterialDescription", (object?)stockCard.MaterialDescription ?? DBNull.Value));
        command.Parameters.Add(new SqlParameter("@MaterialTypeId", (object?)stockCard.MaterialTypeId ?? DBNull.Value));
        command.Parameters.Add(new SqlParameter("@ModifiedDate", (object?)stockCard.ModifiedDate ?? DBNull.Value));
        command.Parameters.Add(new SqlParameter("@ModifiedByUserId", (object?)stockCard.ModifiedByUserId ?? DBNull.Value));
        command.Parameters.Add(new SqlParameter("@UpdateImage", stockCard.ImageData != null ? 1 : 0));
        var updImgParam = new SqlParameter("@ImageData", System.Data.SqlDbType.VarBinary, -1);
        updImgParam.Value = (object?)stockCard.ImageData ?? DBNull.Value;
        command.Parameters.Add(updImgParam);
        command.Parameters.Add(new SqlParameter("@ImageMimeType", (object?)stockCard.ImageMimeType ?? DBNull.Value));
        command.Parameters.Add(new SqlParameter("@TrackCombinations", stockCard.TrackCombinations ? 1 : 0));
        command.Parameters.Add(new SqlParameter("@TaxRate", stockCard.TaxRate));

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task UpdateStockCardActiveStatusAsync(int stockCardId, bool isActive, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            UPDATE {_stockCardsTableName}
            SET
                [is_active] = @IsActive,
                [modified_at] = @UpdatedAt,
                [updated_at] = @UpdatedAt
            WHERE [id] = @Id;
            """;

        command.Parameters.Add(new SqlParameter("@Id", stockCardId));
        command.Parameters.Add(new SqlParameter("@IsActive", isActive ? 1 : 0));
        command.Parameters.Add(new SqlParameter("@UpdatedAt", DateTime.Now));

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task DeleteStockCardAsync(int stockCardId, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);

        await using (var deleteMappingsCommand = connection.CreateCommand())
        {
            deleteMappingsCommand.Transaction = transaction;
            deleteMappingsCommand.CommandText = $"""
                DELETE FROM {_stockPropertyMappingsTableName}
                WHERE [stock_card_id] = @StockCardId;
                """;
            deleteMappingsCommand.Parameters.Add(new SqlParameter("@StockCardId", stockCardId));
            await deleteMappingsCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var deleteMaterialCardCommand = connection.CreateCommand())
        {
            deleteMaterialCardCommand.Transaction = transaction;
            deleteMaterialCardCommand.CommandText = $"""
                DELETE FROM {_stockCardsTableName}
                WHERE [id] = @StockCardId;
                """;
            deleteMaterialCardCommand.Parameters.Add(new SqlParameter("@StockCardId", stockCardId));
            await deleteMaterialCardCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    public async Task UpdateStockCardConfigurableStatusAsync(int stockCardId, bool isConfigurable, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            UPDATE {_stockCardsTableName}
            SET
                [updated_at] = @UpdatedAt
            WHERE [id] = @Id;
            """;

        command.Parameters.Add(new SqlParameter("@Id", stockCardId));
        command.Parameters.Add(new SqlParameter("@UpdatedAt", DateTime.Now));

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task AddWarehouseLocationAsync(WarehouseLocation location, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            INSERT INTO {_warehouseLocationsTableName}
                ([ParentId], [LocationTypeCode], [LocationCode], [LocationName], [SortOrder], [MaxWeightCapacity], [VolumeCapacity], [IsActive])
            VALUES
                (@ParentId, @LocationTypeCode, @LocationCode, @LocationName, @SortOrder, @MaxWeightCapacity, @VolumeCapacity, @IsActive);
            """;

        command.Parameters.Add(new SqlParameter("@ParentId", (object?)location.ParentId ?? DBNull.Value));
        command.Parameters.Add(new SqlParameter("@LocationTypeCode", location.LocationTypeCode));
        command.Parameters.Add(new SqlParameter("@LocationCode", location.LocationCode));
        command.Parameters.Add(new SqlParameter("@LocationName", (object?)location.LocationName ?? DBNull.Value));
        command.Parameters.Add(new SqlParameter("@SortOrder", location.SortOrder));
        command.Parameters.Add(new SqlParameter("@MaxWeightCapacity", (object?)location.MaxWeightCapacity ?? DBNull.Value));
        command.Parameters.Add(new SqlParameter("@VolumeCapacity", (object?)location.VolumeCapacity ?? DBNull.Value));
        command.Parameters.Add(new SqlParameter("@IsActive", location.IsActive));

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task UpdateWarehouseLocationAsync(WarehouseLocation location, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            UPDATE {_warehouseLocationsTableName}
            SET
                [ParentId] = @ParentId,
                [LocationTypeCode] = @LocationTypeCode,
                [LocationCode] = @LocationCode,
                [LocationName] = @LocationName,
                [SortOrder] = @SortOrder,
                [MaxWeightCapacity] = @MaxWeightCapacity,
                [VolumeCapacity] = @VolumeCapacity,
                [IsActive] = @IsActive
            WHERE [Id] = @Id;
            """;

        command.Parameters.Add(new SqlParameter("@Id", location.Id));
        command.Parameters.Add(new SqlParameter("@ParentId", (object?)location.ParentId ?? DBNull.Value));
        command.Parameters.Add(new SqlParameter("@LocationTypeCode", location.LocationTypeCode));
        command.Parameters.Add(new SqlParameter("@LocationCode", location.LocationCode));
        command.Parameters.Add(new SqlParameter("@LocationName", (object?)location.LocationName ?? DBNull.Value));
        command.Parameters.Add(new SqlParameter("@SortOrder", location.SortOrder));
        command.Parameters.Add(new SqlParameter("@MaxWeightCapacity", (object?)location.MaxWeightCapacity ?? DBNull.Value));
        command.Parameters.Add(new SqlParameter("@VolumeCapacity", (object?)location.VolumeCapacity ?? DBNull.Value));
        command.Parameters.Add(new SqlParameter("@IsActive", location.IsActive));

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task DeleteWarehouseLocationAsync(int locationId, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            DELETE FROM {_warehouseLocationsTableName}
            WHERE [Id] = @Id;
            """;

        command.Parameters.Add(new SqlParameter("@Id", locationId));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task AddMeasureUnitDefinitionAsync(MeasureUnitDefinition definition, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            INSERT INTO {_measureUnitDefinitionsTableName}
                ([UnitCode], [UnitName], [IntlCode], [SortOrder], [IsActive], [CreatedAt], [UpdatedAt])
            VALUES
                (@UnitCode, @UnitName, @IntlCode, @SortOrder, @IsActive, @CreatedAt, @UpdatedAt);
            """;

        var now = DateTime.Now;
        command.Parameters.Add(new SqlParameter("@UnitCode", definition.UnitCode));
        command.Parameters.Add(new SqlParameter("@UnitName", definition.UnitName));
        command.Parameters.Add(new SqlParameter("@IntlCode", (object?)definition.IntlCode ?? DBNull.Value));
        command.Parameters.Add(new SqlParameter("@SortOrder", definition.SortOrder));
        command.Parameters.Add(new SqlParameter("@IsActive", definition.IsActive));
        command.Parameters.Add(new SqlParameter("@CreatedAt", now));
        command.Parameters.Add(new SqlParameter("@UpdatedAt", now));

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task UpdateMeasureUnitDefinitionAsync(MeasureUnitDefinition definition, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            UPDATE {_measureUnitDefinitionsTableName}
            SET
                [UnitCode] = @UnitCode,
                [UnitName] = @UnitName,
                [IntlCode] = @IntlCode,
                [SortOrder] = @SortOrder,
                [IsActive] = @IsActive,
                [UpdatedAt] = @UpdatedAt
            WHERE [Id] = @Id;
            """;

        command.Parameters.Add(new SqlParameter("@Id", definition.Id));
        command.Parameters.Add(new SqlParameter("@UnitCode", definition.UnitCode));
        command.Parameters.Add(new SqlParameter("@UnitName", definition.UnitName));
        command.Parameters.Add(new SqlParameter("@IntlCode", (object?)definition.IntlCode ?? DBNull.Value));
        command.Parameters.Add(new SqlParameter("@SortOrder", definition.SortOrder));
        command.Parameters.Add(new SqlParameter("@IsActive", definition.IsActive));
        command.Parameters.Add(new SqlParameter("@UpdatedAt", DateTime.Now));

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task DeleteMeasureUnitDefinitionAsync(int id, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            DELETE FROM {_measureUnitDefinitionsTableName}
            WHERE [Id] = @Id;
            """;

        command.Parameters.Add(new SqlParameter("@Id", id));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyCollection<StockUnitConversion>> GetStockUnitConversionsAsync(int stockCardId, CancellationToken cancellationToken)
    {
        var results = new List<StockUnitConversion>();
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = $"""
            SELECT [id],[stock_card_id],[line_no],[unit_code],[multiplier]
            FROM {_stockUnitConversionsTableName}
            WHERE [stock_card_id] = @StockCardId
            ORDER BY [line_no];
            """;
        cmd.Parameters.Add(new SqlParameter("@StockCardId", stockCardId));
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(new StockUnitConversion
            {
                Id = reader.GetInt32(0),
                StockCardId = reader.GetInt32(1),
                LineNo = reader.GetInt32(2),
                UnitCode = reader.GetString(3),
                Multiplier = reader.GetDecimal(4),
            });
        }
        return results;
    }

    public async Task SaveStockUnitConversionsAsync(int stockCardId, IReadOnlyCollection<StockUnitConversion> conversions, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);

        // Mevcut satirlari sil
        await using (var delCmd = connection.CreateCommand())
        {
            delCmd.CommandText = $"DELETE FROM {_stockUnitConversionsTableName} WHERE [stock_card_id] = @StockCardId;";
            delCmd.Parameters.Add(new SqlParameter("@StockCardId", stockCardId));
            await delCmd.ExecuteNonQueryAsync(cancellationToken);
        }

        // Yeni satirlari ekle (ilk satir=0 master birim, sonrakiler 1..5)
        var lineNo = -1;
        foreach (var c in conversions)
        {
            lineNo++;
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = $"""
                INSERT INTO {_stockUnitConversionsTableName}
                    ([stock_card_id],[line_no],[unit_code],[multiplier])
                VALUES (@StockCardId, @LineNo, @UnitCode, @Multiplier);
                """;
            cmd.Parameters.Add(new SqlParameter("@StockCardId", stockCardId));
            cmd.Parameters.Add(new SqlParameter("@LineNo", lineNo));
            cmd.Parameters.Add(new SqlParameter("@UnitCode", c.UnitCode));
            cmd.Parameters.Add(new SqlParameter("@Multiplier", c.Multiplier));
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    public async Task AddPropertyAsync(ConfigurationProperty property, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            INSERT INTO {_propertiesTableName}
                ([id], [code], [name], [data_type], [is_active], [created_at], [updated_at])
            VALUES
                (@Id, @Code, @Name, @DataType, @IsActive, @CreatedAt, @UpdatedAt);
            """;

        command.Parameters.Add(new SqlParameter("@Id", property.Id));
        command.Parameters.Add(new SqlParameter("@Code", property.Code));
        command.Parameters.Add(new SqlParameter("@Name", property.Name));
        command.Parameters.Add(new SqlParameter("@DataType", property.DataType.ToString()));
        command.Parameters.Add(new SqlParameter("@IsActive", property.IsActive));
        command.Parameters.Add(new SqlParameter("@CreatedAt", property.CreatedAt));
        command.Parameters.Add(new SqlParameter("@UpdatedAt", DateTime.Now));

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task AddPropertyValueAsync(ConfigurationPropertyValue propertyValue, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            INSERT INTO {_propertyValuesTableName}
                ([id], [property_id], [code], [description], [value], [sort_order], [is_active], [created_at], [updated_at])
            VALUES
                (@Id, @PropertyId, @Code, @Description, @Value, @SortOrder, @IsActive, @CreatedAt, @UpdatedAt);
            """;

        command.Parameters.Add(new SqlParameter("@Id", propertyValue.Id));
        command.Parameters.Add(new SqlParameter("@PropertyId", propertyValue.PropertyId));
        command.Parameters.Add(new SqlParameter("@Code", propertyValue.Code));
        command.Parameters.Add(new SqlParameter("@Description", propertyValue.Description));
        command.Parameters.Add(new SqlParameter("@Value", propertyValue.Value));
        command.Parameters.Add(new SqlParameter("@SortOrder", propertyValue.SortOrder));
        command.Parameters.Add(new SqlParameter("@IsActive", propertyValue.IsActive));
        command.Parameters.Add(new SqlParameter("@CreatedAt", propertyValue.CreatedAt));
        command.Parameters.Add(new SqlParameter("@UpdatedAt", DateTime.Now));

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task AddStockPropertyMappingAsync(StockCardPropertyMapping mapping, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            INSERT INTO {_stockPropertyMappingsTableName}
                ([id], [stock_card_id], [property_id], [property_value_id], [configuration_code], [text_value], [numeric_value], [date_value], [is_active], [created_at], [updated_at])
            VALUES
                (@Id, @StockCardId, @PropertyId, @PropertyValueId, @ConfigurationCode, @TextValue, @NumericValue, @DateValue, @IsActive, @CreatedAt, @UpdatedAt);
            """;

        command.Parameters.Add(new SqlParameter("@Id", mapping.Id));
        command.Parameters.Add(new SqlParameter("@StockCardId", mapping.StockCardId));
        command.Parameters.Add(new SqlParameter("@PropertyId", mapping.PropertyId));
        command.Parameters.Add(new SqlParameter("@PropertyValueId", (object?)mapping.PropertyValueId ?? DBNull.Value));
        command.Parameters.Add(new SqlParameter("@ConfigurationCode", (object?)mapping.ConfigurationCode ?? DBNull.Value));
        command.Parameters.Add(new SqlParameter("@TextValue", (object?)mapping.TextValue ?? DBNull.Value));
        command.Parameters.Add(new SqlParameter("@NumericValue", (object?)mapping.NumericValue ?? DBNull.Value));
        command.Parameters.Add(new SqlParameter("@DateValue", (object?)mapping.DateValue ?? DBNull.Value));
        command.Parameters.Add(new SqlParameter("@IsActive", mapping.IsActive));
        command.Parameters.Add(new SqlParameter("@CreatedAt", mapping.CreatedAt));
        command.Parameters.Add(new SqlParameter("@UpdatedAt", DateTime.Now));

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task UpdateStockPropertyMappingValueAsync(
        Guid mappingId,
        Guid propertyValueId,
        string? configurationCode,
        string? textValue,
        decimal? numericValue,
        DateTime? dateValue,
        CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            UPDATE {_stockPropertyMappingsTableName}
            SET
                [property_value_id] = @PropertyValueId,
                [configuration_code] = @ConfigurationCode,
                [text_value] = @TextValue,
                [numeric_value] = @NumericValue,
                [date_value] = @DateValue,
                [updated_at] = @UpdatedAt
            WHERE [id] = @Id;
            """;

        command.Parameters.Add(new SqlParameter("@Id", mappingId));
        command.Parameters.Add(new SqlParameter("@PropertyValueId", propertyValueId));
        command.Parameters.Add(new SqlParameter("@ConfigurationCode", (object?)configurationCode ?? DBNull.Value));
        command.Parameters.Add(new SqlParameter("@TextValue", (object?)textValue ?? DBNull.Value));
        command.Parameters.Add(new SqlParameter("@NumericValue", (object?)numericValue ?? DBNull.Value));
        command.Parameters.Add(new SqlParameter("@DateValue", (object?)dateValue ?? DBNull.Value));
        command.Parameters.Add(new SqlParameter("@UpdatedAt", DateTime.Now));

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static MaterialCardDynamicFieldDataType ParseMaterialCardDynamicFieldDataType(string? rawValue) =>
        rawValue?.Trim().ToUpperInvariant() switch
        {
            "INTEGER" => MaterialCardDynamicFieldDataType.Integer,
            "DECIMAL" => MaterialCardDynamicFieldDataType.Decimal,
            "DATE" => MaterialCardDynamicFieldDataType.Date,
            "BOOLEAN" => MaterialCardDynamicFieldDataType.Boolean,
            "DROPDOWN" => MaterialCardDynamicFieldDataType.Dropdown,
            "MULTISELECT" => MaterialCardDynamicFieldDataType.MultiSelect,
            _ => MaterialCardDynamicFieldDataType.String
        };

    private static string ToMaterialCardDynamicFieldDataTypeValue(MaterialCardDynamicFieldDataType dataType) =>
        dataType switch
        {
            MaterialCardDynamicFieldDataType.Integer => "INTEGER",
            MaterialCardDynamicFieldDataType.Decimal => "DECIMAL",
            MaterialCardDynamicFieldDataType.Date => "DATE",
            MaterialCardDynamicFieldDataType.Boolean => "BOOLEAN",
            MaterialCardDynamicFieldDataType.Dropdown => "DROPDOWN",
            MaterialCardDynamicFieldDataType.MultiSelect => "MULTISELECT",
            _ => "STRING"
        };
        
    public async Task<IReadOnlyCollection<ProductTree>> GetProductTreesAsync(CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT
                t.[Id], t.[ParentMaterialCode], t.[ConfigurationCode], t.[Description],
                t.[ImageData], t.[ImageMimeType],
                l.[Id]       AS [LineId],
                l.[ProductTreeId],
                l.[ComponentMaterialCode],
                l.[ComponentConfigCode],
                l.[Quantity],
                l.[ScrapRatio],
                l.[LineGuid]
            FROM {_productTreesTableName} t
            LEFT JOIN {_productTreeLinesTableName} l ON l.[ProductTreeId] = t.[Id]
            ORDER BY t.[Id], l.[Id];
            """;

        var treesById = new Dictionary<int, ProductTree>();
        var linesByTreeId = new Dictionary<int, List<ProductTreeLine>>();

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var treeId = reader.GetInt32(0);
            if (!treesById.ContainsKey(treeId))
            {
                treesById[treeId] = new ProductTree
                {
                    Id                 = treeId,
                    ParentMaterialCode = reader.GetString(1),
                    ConfigurationCode  = reader.IsDBNull(2) ? null : reader.GetString(2),
                    Description        = reader.IsDBNull(3) ? null : reader.GetString(3),
                    ImageData          = reader.IsDBNull(4) ? null : (byte[])reader.GetValue(4),
                    ImageMimeType      = reader.IsDBNull(5) ? null : reader.GetString(5),
                };
                linesByTreeId[treeId] = new List<ProductTreeLine>();
            }

            if (!reader.IsDBNull(6))
            {
                linesByTreeId[treeId].Add(new ProductTreeLine
                {
                    Id                    = reader.GetInt32(6),
                    ProductTreeId         = reader.GetInt32(7),
                    ComponentMaterialCode = reader.GetString(8),
                    ComponentConfigCode   = reader.IsDBNull(9) ? null : reader.GetString(9),
                    Quantity              = reader.GetDecimal(10),
                    ScrapRatio            = reader.GetDecimal(11),
                    LineGuid              = reader.GetGuid(12),
                });
            }
        }

        foreach (var (treeId, lines) in linesByTreeId)
            ((List<ProductTreeLine>)treesById[treeId].Lines).AddRange(lines);

        return treesById.Values.ToList();
    }

    public async Task<ProductTreeWithNames?> GetProductTreeByCodeAsync(string materialCode, string? configCode, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();

        var configFilter    = string.IsNullOrWhiteSpace(configCode)
            ? "t.[ConfigurationCode] IS NULL"
            : "t.[ConfigurationCode] = @ConfigCode";
        var configFilterSub = string.IsNullOrWhiteSpace(configCode)
            ? "[ConfigurationCode] IS NULL"
            : "[ConfigurationCode] = @ConfigCode";

        command.CommandText = $"""
            SELECT
                t.[Id], t.[ParentMaterialCode], t.[ConfigurationCode], t.[Description],
                t.[ImageData], t.[ImageMimeType], t.[ImageFitMode],
                l.[ComponentMaterialCode],
                ISNULL(m.[material_name], l.[ComponentMaterialCode]) AS [ComponentMaterialName],
                l.[ComponentConfigCode],
                l.[Quantity],
                l.[ScrapRatio]
            FROM {_productTreesTableName} t
            LEFT JOIN {_productTreeLinesTableName} l  ON l.[ProductTreeId] = t.[Id]
            LEFT JOIN {_stockCardsTableName}        m ON m.[material_code]  = l.[ComponentMaterialCode]
            WHERE t.[ParentMaterialCode] = @MaterialCode
              AND {configFilter}
              AND t.[Id] = (
                  SELECT MAX([Id]) FROM {_productTreesTableName}
                  WHERE [ParentMaterialCode] = @MaterialCode AND {configFilterSub}
              )
            ORDER BY l.[Id];
            """;

        command.Parameters.Add(new SqlParameter("@MaterialCode", materialCode.Trim().ToUpperInvariant()));
        if (!string.IsNullOrWhiteSpace(configCode))
            command.Parameters.Add(new SqlParameter("@ConfigCode", configCode.Trim()));

        ProductTreeWithNames? result = null;
        var lines = new List<ProductTreeLineWithName>();

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            if (result is null)
            {
                result = new ProductTreeWithNames(
                    reader.GetInt32(0),
                    reader.GetString(1),
                    reader.IsDBNull(2)  ? null : reader.GetString(2),
                    reader.IsDBNull(3)  ? null : reader.GetString(3),
                    reader.IsDBNull(4)  ? null : (byte[])reader.GetValue(4),
                    reader.IsDBNull(5)  ? null : reader.GetString(5),
                    reader.IsDBNull(6)  ? null : reader.GetString(6),
                    lines);
            }

            if (!reader.IsDBNull(7))
            {
                lines.Add(new ProductTreeLineWithName(
                    reader.GetString(7),
                    reader.GetString(8),
                    reader.IsDBNull(9)  ? null : reader.GetString(9),
                    reader.GetDecimal(10),
                    reader.GetDecimal(11)));
            }
        }

        return result;
    }

    public async Task<int> AddProductTreeAsync(ProductTree tree, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var transaction = connection.BeginTransaction();

        int newId;
        await using (var cmd = connection.CreateCommand())
        {
            cmd.Transaction = transaction;
            cmd.CommandText = $"""
                INSERT INTO {_productTreesTableName}
                    ([ParentMaterialCode],[ConfigurationCode],[Description],[ImageData],[ImageMimeType],[ImageFitMode],[CreatedAt],[UpdatedAt])
                VALUES
                    (@ParentMaterialCode,@ConfigurationCode,@Description,@ImageData,@ImageMimeType,@ImageFitMode,GETDATE(),GETDATE());
                SELECT CAST(SCOPE_IDENTITY() AS INT);
                """;
            cmd.Parameters.Add(new SqlParameter("@ParentMaterialCode", tree.ParentMaterialCode));
            cmd.Parameters.Add(new SqlParameter("@ConfigurationCode", (object?)tree.ConfigurationCode ?? DBNull.Value));
            cmd.Parameters.Add(new SqlParameter("@Description",       (object?)tree.Description       ?? DBNull.Value));
            cmd.Parameters.Add(new SqlParameter("@ImageData",         (object?)tree.ImageData         ?? DBNull.Value) { SqlDbType = System.Data.SqlDbType.VarBinary });
            cmd.Parameters.Add(new SqlParameter("@ImageMimeType",     (object?)tree.ImageMimeType     ?? DBNull.Value));
            cmd.Parameters.Add(new SqlParameter("@ImageFitMode",      (object?)tree.ImageFitMode      ?? DBNull.Value));
            newId = (int)(await cmd.ExecuteScalarAsync(cancellationToken))!;
        }

        foreach (var line in tree.Lines)
        {
            await using var lineCmd = connection.CreateCommand();
            lineCmd.Transaction = transaction;
            lineCmd.CommandText = $"""
                INSERT INTO {_productTreeLinesTableName}
                    ([ProductTreeId],[ComponentMaterialCode],[ComponentConfigCode],[Quantity],[ScrapRatio],[LineGuid])
                VALUES
                    (@TreeId,@CompCode,@CompCfg,@Qty,@Scrap,@LineGuid);
                """;
            lineCmd.Parameters.Add(new SqlParameter("@TreeId",  newId));
            lineCmd.Parameters.Add(new SqlParameter("@CompCode", line.ComponentMaterialCode));
            lineCmd.Parameters.Add(new SqlParameter("@CompCfg",  (object?)line.ComponentConfigCode ?? DBNull.Value));
            lineCmd.Parameters.Add(new SqlParameter("@Qty",      line.Quantity));
            lineCmd.Parameters.Add(new SqlParameter("@Scrap",    line.ScrapRatio));
            lineCmd.Parameters.Add(new SqlParameter("@LineGuid", line.LineGuid == Guid.Empty ? Guid.NewGuid() : line.LineGuid));
            await lineCmd.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return newId;
    }

    public async Task UpdateProductTreeAsync(ProductTree tree, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var transaction = connection.BeginTransaction();

        await using (var cmd = connection.CreateCommand())
        {
            cmd.Transaction = transaction;
            cmd.CommandText = $"""
                UPDATE {_productTreesTableName}
                SET [ParentMaterialCode] = @ParentMaterialCode,
                    [ConfigurationCode]  = @ConfigurationCode,
                    [Description]        = @Description,
                    [ImageData]          = @ImageData,
                    [ImageMimeType]      = @ImageMimeType,
                    [ImageFitMode]       = @ImageFitMode,
                    [UpdatedAt]          = GETDATE()
                WHERE [Id] = @Id;
                """;
            cmd.Parameters.Add(new SqlParameter("@Id",                 tree.Id));
            cmd.Parameters.Add(new SqlParameter("@ParentMaterialCode", tree.ParentMaterialCode));
            cmd.Parameters.Add(new SqlParameter("@ConfigurationCode",  (object?)tree.ConfigurationCode ?? DBNull.Value));
            cmd.Parameters.Add(new SqlParameter("@Description",        (object?)tree.Description       ?? DBNull.Value));
            cmd.Parameters.Add(new SqlParameter("@ImageData",          (object?)tree.ImageData         ?? DBNull.Value) { SqlDbType = System.Data.SqlDbType.VarBinary });
            cmd.Parameters.Add(new SqlParameter("@ImageMimeType",      (object?)tree.ImageMimeType     ?? DBNull.Value));
            cmd.Parameters.Add(new SqlParameter("@ImageFitMode",       (object?)tree.ImageFitMode      ?? DBNull.Value));
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var deleteCmd = connection.CreateCommand())
        {
            deleteCmd.Transaction = transaction;
            deleteCmd.CommandText = $"DELETE FROM {_productTreeLinesTableName} WHERE [ProductTreeId] = @TreeId;";
            deleteCmd.Parameters.Add(new SqlParameter("@TreeId", tree.Id));
            await deleteCmd.ExecuteNonQueryAsync(cancellationToken);
        }

        foreach (var line in tree.Lines)
        {
            await using var lineCmd = connection.CreateCommand();
            lineCmd.Transaction = transaction;
            lineCmd.CommandText = $"""
                INSERT INTO {_productTreeLinesTableName}
                    ([ProductTreeId],[ComponentMaterialCode],[ComponentConfigCode],[Quantity],[ScrapRatio],[LineGuid])
                VALUES
                    (@TreeId,@CompCode,@CompCfg,@Qty,@Scrap,@LineGuid);
                """;
            lineCmd.Parameters.Add(new SqlParameter("@TreeId",  tree.Id));
            lineCmd.Parameters.Add(new SqlParameter("@CompCode", line.ComponentMaterialCode));
            lineCmd.Parameters.Add(new SqlParameter("@CompCfg",  (object?)line.ComponentConfigCode ?? DBNull.Value));
            lineCmd.Parameters.Add(new SqlParameter("@Qty",      line.Quantity));
            lineCmd.Parameters.Add(new SqlParameter("@Scrap",    line.ScrapRatio));
            lineCmd.Parameters.Add(new SqlParameter("@LineGuid", line.LineGuid == Guid.Empty ? Guid.NewGuid() : line.LineGuid));
            await lineCmd.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    public async Task DeleteProductTreeAsync(int id, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            DELETE FROM {_productTreeLinesTableName} WHERE [ProductTreeId] = @Id;
            DELETE FROM {_productTreesTableName}     WHERE [Id] = @Id;
            """;
        command.Parameters.Add(new SqlParameter("@Id", id));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyCollection<CombinationLookupRow>> GetCombinationsByMaterialCodeAsync(
        string materialCode,
        CancellationToken cancellationToken)
    {
        // Adım 1: Kök CONFIG kayıtlarını çek (ParentId IS NULL)
        var combos = new Dictionary<int, CombinationLookupRow>();

        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var cmd1 = connection.CreateCommand();
        cmd1.CommandText = $"""
            SELECT [Id], [RecordCode], [RecordName]
            FROM {_productConfigurationTableName}
            WHERE [RecordType] = 'CONFIG'
              AND [ParentId] IS NULL
              AND [RelatedMaterialCode] = @mc
              AND [IsActive] = 1
            ORDER BY [RecordCode];
            """;
        var p1 = cmd1.CreateParameter(); p1.ParameterName = "@mc"; p1.Value = materialCode;
        cmd1.Parameters.Add(p1);

        await using (var r1 = await cmd1.ExecuteReaderAsync(cancellationToken))
        {
            while (await r1.ReadAsync(cancellationToken))
                combos[r1.GetInt32(0)] = new CombinationLookupRow(r1.GetString(1),
                    r1.IsDBNull(2) ? r1.GetString(1) : r1.GetString(2),
                    new List<(string Feature, string Value)>());
        }

        if (combos.Count == 0) return Array.Empty<CombinationLookupRow>();

        // Adım 2: Alt CONFIG → VALUE → FEATURE join ile özellik-değer çiftleri
        await using var cmd2 = connection.CreateCommand();
        cmd2.CommandText = $"""
            SELECT
                ch.[ParentId]            AS ConfigId,
                f.[RecordName]           AS FeatureName,
                TRIM(SUBSTRING(v.[RecordName], CHARINDEX('||', v.[RecordName]) + 2, 500)) AS ValueDesc
            FROM {_productConfigurationTableName} ch
            JOIN {_productConfigurationTableName} v
                ON v.[Id] = TRY_CAST(ch.[RecordName] AS INT) AND v.[RecordType] = 'VALUE'
            JOIN {_productConfigurationTableName} f
                ON f.[Id] = v.[ParentId] AND f.[RecordType] = 'FEATURE'
            WHERE ch.[RecordType] = 'CONFIG'
              AND ch.[ParentId] IN ({string.Join(",", combos.Keys)})
              AND ch.[RelatedMaterialCode] = @mc
            ORDER BY ch.[ParentId], f.[RecordCode];
            """;
        var p2 = cmd2.CreateParameter(); p2.ParameterName = "@mc"; p2.Value = materialCode;
        cmd2.Parameters.Add(p2);

        await using (var r2 = await cmd2.ExecuteReaderAsync(cancellationToken))
        {
            while (await r2.ReadAsync(cancellationToken))
            {
                var configId  = r2.GetInt32(0);
                var feature   = r2.IsDBNull(1) ? "" : r2.GetString(1);
                var valueDesc = r2.IsDBNull(2) ? "" : r2.GetString(2);
                if (combos.TryGetValue(configId, out var row))
                    ((List<(string, string)>)row.FeatureValues).Add((feature, valueDesc));
            }
        }

        return combos.Values.ToList();
    }

    public async Task<IReadOnlyCollection<MaterialGroup>> GetMaterialGroupsAsync(int? category, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = category.HasValue
            ? $"SELECT [Id],[GroupCategory],[GroupCode],[GroupDescription] FROM {_materialGroupsTableName} WHERE [GroupCategory]=@Cat ORDER BY [GroupCode];"
            : $"SELECT [Id],[GroupCategory],[GroupCode],[GroupDescription] FROM {_materialGroupsTableName} ORDER BY [GroupCategory],[GroupCode];";
        if (category.HasValue)
            command.Parameters.Add(new SqlParameter("@Cat", category.Value));
        var result = new List<MaterialGroup>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            result.Add(new MaterialGroup
            {
                Id              = reader.GetInt32(0),
                GroupCategory   = reader.GetByte(1),
                GroupCode       = reader.GetString(2),
                GroupDescription = reader.IsDBNull(3) ? null : reader.GetString(3)
            });
        return result;
    }

    public async Task AddMaterialGroupAsync(MaterialGroup group, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"INSERT INTO {_materialGroupsTableName} ([GroupCategory],[GroupCode],[GroupDescription]) VALUES (@Cat,@Code,@Desc);";
        command.Parameters.Add(new SqlParameter("@Cat",  group.GroupCategory));
        command.Parameters.Add(new SqlParameter("@Code", group.GroupCode));
        command.Parameters.Add(new SqlParameter("@Desc", (object?)group.GroupDescription ?? DBNull.Value));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task UpdateMaterialGroupAsync(MaterialGroup group, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"UPDATE {_materialGroupsTableName} SET [GroupCategory]=@Cat,[GroupCode]=@Code,[GroupDescription]=@Desc WHERE [Id]=@Id;";
        command.Parameters.Add(new SqlParameter("@Id",  group.Id));
        command.Parameters.Add(new SqlParameter("@Cat", group.GroupCategory));
        command.Parameters.Add(new SqlParameter("@Code", group.GroupCode));
        command.Parameters.Add(new SqlParameter("@Desc", (object?)group.GroupDescription ?? DBNull.Value));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task DeleteMaterialGroupAsync(int id, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"DELETE FROM {_materialGroupsTableName} WHERE [Id]=@Id;";
        command.Parameters.Add(new SqlParameter("@Id", id));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyCollection<MaterialGroupMappingDto>> GetMaterialGroupMappingsAsync(int stockCardId, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT m.[SlotOrder], m.[GroupCode], g.[GroupDescription]
            FROM {_materialGroupMappingsTableName} m
            LEFT JOIN {_materialGroupsTableName} g ON g.[GroupCode] = m.[GroupCode] AND g.[GroupCategory] = m.[SlotOrder]
            WHERE m.[StockCardId] = @StockCardId
            ORDER BY m.[SlotOrder];
            """;
        command.Parameters.Add(new SqlParameter("@StockCardId", stockCardId));
        var result = new List<MaterialGroupMappingDto>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            result.Add(new MaterialGroupMappingDto(reader.GetByte(0), reader.GetString(1), reader.IsDBNull(2) ? null : reader.GetString(2)));
        return result;
    }

    public async Task SaveMaterialGroupMappingsAsync(int stockCardId, IReadOnlyCollection<(int Slot, string Code)> mappings, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var transaction = connection.BeginTransaction();

        await using (var delCmd = connection.CreateCommand())
        {
            delCmd.Transaction = transaction;
            delCmd.CommandText = $"DELETE FROM {_materialGroupMappingsTableName} WHERE [StockCardId]=@StockCardId;";
            delCmd.Parameters.Add(new SqlParameter("@StockCardId", stockCardId));
            await delCmd.ExecuteNonQueryAsync(cancellationToken);
        }

        foreach (var (slot, code) in mappings)
        {
            await using var insCmd = connection.CreateCommand();
            insCmd.Transaction = transaction;
            insCmd.CommandText = $"INSERT INTO {_materialGroupMappingsTableName} ([StockCardId],[SlotOrder],[GroupCode]) VALUES (@StockCardId,@Slot,@Code);";
            insCmd.Parameters.Add(new SqlParameter("@StockCardId", stockCardId));
            insCmd.Parameters.Add(new SqlParameter("@Slot",        slot));
            insCmd.Parameters.Add(new SqlParameter("@Code",        code));
            await insCmd.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }
}
